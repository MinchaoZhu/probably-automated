using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 子流程：一个流程的任务里“执行流程【X】”，把 X 的任务按顺序做一遍。X 的本流程参数（数值、设备、列表）
/// 可以由调用方覆盖，不覆盖用参数自己的值。调用不能成环，嵌套不超过 MaxDepth 层，所以执行必然结束。
/// </summary>
public static class Calls
{
    /// <summary>最多嵌套几层子流程（直接指派的流程是第 0 层）。</summary>
    public const int MaxDepth = 3;

    public enum ParamKind { None, Number, List, Device }

    /// <summary>被调用的流程；不是执行流程或流程已删除时为空。</summary>
    public static Schedule Callee(RulesData d, ActionDef a) =>
        a?.Verb == ActVerb.Call && !string.IsNullOrEmpty(a.Flow) ? d.Schedules.FirstOrDefault(s => s.Id == a.Flow) : null;

    /// <summary>流程 s 的任务直接调用的流程（去重，按出现顺序）。</summary>
    public static List<Schedule> Callees(RulesData d, Schedule s) =>
        s.Rules.SelectMany(r => r.Actions).Select(a => Callee(d, a)).Where(x => x != null).Distinct().ToList();

    /// <summary>流程 s 和它（直接、间接）调用的全部子流程，s 在最前。</summary>
    public static List<Schedule> WithCallees(RulesData d, Schedule s) =>
        d.Schedules.Where(x => x.Id != s.Id && Reaches(d, s, x.Id)).Prepend(s).ToList();

    /// <summary>调用流程 s 的流程。</summary>
    public static List<Schedule> Callers(RulesData d, Schedule s) =>
        d.Schedules.Where(x => x.Rules.Any(r => r.Actions.Any(a => a.Verb == ActVerb.Call && a.Flow == s.Id))).ToList();

    /// <summary>从 from 出发（含经过别的子流程）会不会调用到 target。</summary>
    public static bool Reaches(RulesData d, Schedule from, string target)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<Schedule>(new[] { from });
        while (stack.Count > 0)
        {
            var s = stack.Pop();
            if (!seen.Add(s.Id)) continue;
            foreach (var c in Callees(d, s))
            {
                if (c.Id == target) return true;
                stack.Push(c);
            }
        }
        return false;
    }

    /// <summary>流程往下最多套几层子流程（不调用别的流程为 0；成环时按已走过的算，不会死循环）。</summary>
    public static int Height(RulesData d, Schedule s) => Height(d, s, new HashSet<string>());

    private static int Height(RulesData d, Schedule s, HashSet<string> path)
    {
        if (!path.Add(s.Id)) return 0;
        int h = 0;
        foreach (var c in Callees(d, s)) h = System.Math.Max(h, 1 + Height(d, c, path));
        path.Remove(s.Id);
        return h;
    }

    // ================= 参数 =================

    /// <summary>被调用流程的本流程参数的种类（不是它的本流程参数为 None）。</summary>
    public static ParamKind KindOf(RulesData d, Schedule s, string id) =>
        d.Objects.FirstOrDefault(o => o.Id == id && o.Owner == s.Id) is not { } o ? ParamKind.None
        : o.Type == ParamType.Device ? ParamKind.Device
        : o.Type == ParamType.List ? ParamKind.List
        : ParamKind.Number;

    /// <summary>调用时可以覆盖的：被调用流程的本流程参数（全局参数是几个流程共用的，不能在一次调用里换掉）。</summary>
    public static List<string> Params(RulesData d, Schedule s) =>
        d.Objects.Where(o => o.Owner == s.Id).Select(o => o.Id).ToList();

    /// <summary>去掉调用里已经对不上的覆盖：参数删了、改成全局了，或类型和覆盖的值对不上。</summary>
    public static void DropStaleArgs(RulesData d)
    {
        foreach (var a in d.Schedules.SelectMany(s => s.Rules).SelectMany(r => r.Actions).Where(a => a.Args != null))
        {
            if (Callee(d, a) is not { } callee) continue;
            a.Args.RemoveAll(x => !Fits(d, callee, x));
            if (a.Args.Count == 0) a.Args = null;
        }
    }

    /// <summary>覆盖和参数对得上：参数是被调用流程的本流程参数，数值参数给数，设备、列表参数给引用。</summary>
    private static bool Fits(RulesData d, Schedule callee, CallArg x) => KindOf(d, callee, x.Param) switch
    {
        ParamKind.None => false,
        ParamKind.Number => x.Num != null,
        _ => !string.IsNullOrEmpty(x.Ref),
    };

    /// <summary>参数的名字。</summary>
    public static string ParamName(RulesData d, string id) => Text.Name(d, id);

    /// <summary>默认值（参数自己的值）的写法：数值带单位（“8级”），设备、列表写【名字】。</summary>
    public static string DefaultText(RulesData d, Schedule s, string id) =>
        d.Objects.FirstOrDefault(o => o.Id == id) is { } o ? Text.ParamValue(d, o) : "";

    /// <summary>调用里覆盖这个参数的值；没覆盖为空。</summary>
    public static CallArg ArgOf(ActionDef a, string param) => a.Args?.FirstOrDefault(x => x.Param == param);

    /// <summary>覆盖（ref、num 都为空 = 恢复默认）。</summary>
    public static void SetArg(ActionDef a, string param, string reference, double? num)
    {
        a.Args ??= new List<CallArg>();
        a.Args.RemoveAll(x => x.Param == param);
        if (reference != null || num != null) a.Args.Add(new CallArg { Param = param, Ref = reference, Num = num });
        if (a.Args.Count == 0) a.Args = null;
    }

    /// <summary>覆盖值的写法：【筛选器】、【设备】、数值。</summary>
    public static string ArgText(RulesData d, Schedule callee, CallArg x)
    {
        if (x.Num is { } n)
        {
            var o = d.Objects.FirstOrDefault(v => v.Id == x.Param);
            return $"{n:0.##}{o?.Unit}";
        }
        return L.T("text.bracket", Text.Name(d, x.Ref));
    }

    /// <summary>调用里有效的覆盖（参数还在、类型对得上），按参数的定义顺序。</summary>
    public static List<CallArg> Overrides(RulesData d, Schedule callee, ActionDef a)
    {
        var order = Params(d, callee);
        return (a.Args ?? new List<CallArg>()).Where(x => Fits(d, callee, x)).OrderBy(x => order.IndexOf(x.Param)).ToList();
    }

    /// <summary>调用里引用的调用方定义（覆盖用的筛选器、设备名）。</summary>
    public static IEnumerable<string> ArgRefs(ActionDef a) =>
        a.Args?.Where(x => !string.IsNullOrEmpty(x.Ref)).Select(x => x.Ref) ?? Enumerable.Empty<string>();

    // ================= 执行与检查 =================

    /// <summary>
    /// 调用那一刻在调用方求出传入的值：设备名、筛选器、调用方的参数（调用方自己是子流程、参数又是传进来的，原样转交）。
    /// </summary>
    public static Dictionary<string, ArgVal> Bind(EvalCtx caller, Schedule callee, ActionDef a)
    {
        var args = new Dictionary<string, ArgVal>();
        foreach (var x in Overrides(caller.D, callee, a))
        {
            if (x.Num is { } n) { args[x.Param] = new ArgVal { Num = n }; continue; }
            if (caller.Arg(x.Ref) is { Items: not null } passed) { args[x.Param] = passed; continue; }
            if (Defs.Selector(caller.D, x.Ref) is not { IsNumber: false } sel) continue;
            bool boxes = Places.IsDevice(x.Ref) || Eval.YieldsBoxes(sel, caller.W);
            args[x.Param] = new ArgVal { Items = (boxes ? Eval.Boxes(caller, x.Ref) : Eval.List(caller, x.Ref)).ToList(), Boxes = boxes, Ref = x.Ref };
        }
        return args;
    }

    /// <summary>执行流程这个动作的结构错误。</summary>
    public static List<string> Errors(RulesData d, Schedule s, ActionDef a)
    {
        var e = new List<string>();
        var callee = Callee(d, a);
        if (callee == null) { e.Add(L.T(string.IsNullOrEmpty(a.Flow) ? "check.err.call_no_flow" : "check.err.call_missing")); return e; }
        if (callee.Id == s.Id || Reaches(d, callee, s.Id)) { e.Add(L.T("check.err.call_cycle", callee.Name)); return e; }
        if (1 + Height(d, callee) > MaxDepth) e.Add(L.T("check.err.call_depth", MaxDepth));
        foreach (var x in Overrides(d, callee, a))
            if (x.Num == null && Defs.Selector(d, x.Ref) is not { IsNumber: false })
                e.Add(L.T("check.err.call_arg_missing", ParamName(d, x.Param)));
        return e;
    }
}
