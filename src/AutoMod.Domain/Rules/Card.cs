using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>规则句子里的一段：普通文字或可点的词。</summary>
public class Seg
{
    public string Text = "";

    /// <summary>text = 普通文字；list = 对象列表（挑选器）；box = 容器；op = 动作；cond = 条件类型；complex = 复杂条件（不可点）。</summary>
    public string Kind = SegKind.Text;

    /// <summary>可点的词属于第几个动作（-1 = 执行前提）。</summary>
    public int Action = -1;

    /// <summary>可点的词对应的参数：list / to / fallback / tool / out / supply / oldTo / pre。</summary>
    public string Param;

    /// <summary>还没选（显示“（选择…）”）。</summary>
    public bool Empty;
}

public static class SegKind
{
    public const string Text = "text";
    public const string List = "list";
    public const string Box = "box";
    public const string Op = "op";
    public const string Cond = "cond";
    public const string Type = "type";
    public const string Complex = "complex";
}

/// <summary>
/// 排班卡片：卡片上显示的参数、容器，规则的一句话说明，以及卡片上允许的编辑。
/// </summary>
public static class Card
{
    // ================= 参数与容器 =================

    /// <summary>排班的规则（连带挑选器）用到的全部定义 id。</summary>
    public static HashSet<string> Uses(RulesData d, Schedule s) => Check.Closure(d, s.Rules.SelectMany(Check.RuleRefs));

    /// <summary>参数：排班用到的参数，按定义顺序。</summary>
    public static List<ObjectDef> Parameters(RulesData d, Schedule s)
    {
        var used = Uses(d, s);
        return d.Objects.Where(o => used.Contains(o.Id)).ToList();
    }

    /// <summary>设备名：排班用到的设备名（按出现顺序，去重）。</summary>
    public static List<string> DeviceNames(RulesData d, Schedule s) =>
        Uses(d, s).Where(Places.IsName).Select(Places.NameOf).Distinct().ToList();

    /// <summary>有几个排班用到这个定义（大于 1 时卡片上标“共用”）。</summary>
    public static int SharedBy(RulesData d, string id) => d.Schedules.Count(s => Uses(d, s).Contains(id));

    // ================= 一句话说明 =================

    public static string ParamName(string param) => L.T("card.param." + param);

    /// <summary>执行前提能否在卡片上编辑：没有前提，或只有一个“列表 不为空 / 为空 / 数量 &gt; N / 数量 &lt; N”。</summary>
    public static bool SimplePre(RulesData d, WorkRule r) =>
        r.Pre.Count == 0 || (r.Pre.Count == 1 && r.Pre[0].RefKind == PreRef.Selector
            && r.Pre[0].Where == null
            && (r.Pre[0].Ref == null || Defs.Selector(d, r.Pre[0].Ref) is { IsNumber: false })
            && r.Pre[0].Test is PreTest.NotEmpty or PreTest.Empty or PreTest.CountGt or PreTest.CountLt
            && (r.Pre[0].Test is PreTest.NotEmpty or PreTest.Empty || r.Pre[0].Right?.Terms is { Count: 1 } t && t[0].Kind == TermKind.Num));

    public static string CondText(WorkRule r)
    {
        if (r.Pre.Count == 0) return L.T("card.cond.every_shift");
        var p = r.Pre[0];
        double n = p.Right?.Terms?.FirstOrDefault()?.Num ?? 0;
        return p.Test switch
        {
            PreTest.NotEmpty => L.T("card.cond.not_empty"),
            PreTest.Empty => L.T("card.cond.empty"),
            PreTest.CountGt => L.N("card.cond.gt", n),
            PreTest.CountLt => L.N("card.cond.lt", n),
            _ => p.Test,
        };
    }

    /// <summary>规则的一句话：第一行是执行前提，之后每个动作一行。</summary>
    public static List<List<Seg>> Sentence(RulesData d, Schedule s, WorkRule r)
    {
        var lines = new List<List<Seg>>();
        var pre = new List<Seg>();
        if (r.Pre.Count == 0)
            pre.AddRange(Fill("card.tpl.cond_colon", new() { ["cond"] = new Seg { Text = L.T("card.cond.every_shift_run"), Kind = SegKind.Cond } }));
        else if (SimplePre(d, r))
            pre.AddRange(Fill("card.tpl.when", new()
            {
                ["list"] = Ref(d, r.Pre[0].Ref, SegKind.List, -1, "pre"),
                ["cond"] = new Seg { Text = CondText(r), Kind = SegKind.Cond },
            }));
        else
            pre.AddRange(Fill("card.tpl.complex_colon", new()
            {
                ["complex"] = new Seg { Text = L.T("card.cond.when", L.Join(r.Join == "any" ? "or" : "and", r.Pre.Select(p => Text.Pre(d, s, p)))), Kind = SegKind.Complex },
            }));
        lines.Add(pre);

        for (int i = 0; i < r.Actions.Count; i++)
        {
            var a = r.Actions[i];
            var op = Catalog.Op(a.Verb);
            Seg S(string param, string id, string kind) => Ref(d, id, kind, i, param);
            var O = new Seg { Text = op.Mechanism == Mechanism.Apply ? L.T("card.op.apply") : op.Name, Kind = SegKind.Op, Action = i };
            string times = a.Times > 1 && a.Verb != ActVerb.Toggle && (op.Mechanism is Mechanism.Apply or Mechanism.Trigger)
                ? L.N("card.times", a.Times)
                : "";

            List<Seg> line = op.Mechanism switch
            {
                Mechanism.Place => Fill("card.tpl.place", new() { ["list"] = S("list", a.List, SegKind.List), ["op"] = O, ["to"] = S("to", a.To, SegKind.Box) }),
                Mechanism.Swap => Fill("card.tpl.swap", new() { ["list"] = S("list", a.List, SegKind.List), ["op"] = O, ["to"] = S("to", a.To, SegKind.List) }),
                Mechanism.Apply => Fill("card.tpl.apply", new() { ["tool"] = S("tool", a.Tool, SegKind.Box), ["op"] = O, ["list"] = S("list", a.List, SegKind.List) }),
                Mechanism.Trigger when a.Verb == ActVerb.Press => Fill("card.tpl.press", new()
                {
                    ["op"] = new Seg { Text = L.T("card.op.press"), Kind = SegKind.Op, Action = i },
                    ["list"] = S("list", a.List, SegKind.List),
                    ["button"] = new Seg { Text = string.IsNullOrEmpty(a.Button) ? L.T("card.select_button") : L.T("text.bracket", Text.ButtonName(a.Button.Trim('[', ']', '【', '】'))), Kind = SegKind.Type, Action = i, Param = "button", Empty = string.IsNullOrEmpty(a.Button) },
                }),
                Mechanism.Trigger => Fill("card.tpl.trigger", new() { ["op"] = O, ["list"] = S("list", a.List, SegKind.List) }),
                Mechanism.Annotate => Fill(a.Verb == ActVerb.Unmark ? "card.tpl.unmark" : "card.tpl.mark", new() { ["list"] = S("list", a.List, SegKind.List), ["op"] = O, ["text"] = new Seg { Text = a.Text } }),
                _ => new List<Seg> { new Seg { Text = Text.Action(d, s, a), Kind = SegKind.Complex } },
            };
            if (!string.IsNullOrEmpty(times)) line.Add(new Seg { Text = times });
            lines.Add(line);
        }
        return lines;
    }

    private static List<Seg> Fill(string key, Dictionary<string, Seg> slots) =>
        L.Template(key).Select(p => p.slot == null ? new Seg { Text = p.text }
            : slots.TryGetValue(p.slot, out var seg) ? seg : new Seg { Text = p.slot }).ToList();

    private static Seg Ref(RulesData d, string id, string kind, int action, string param)
    {
        bool exists = !string.IsNullOrEmpty(id) && (
            Places.IsShop(id) || Places.IsName(id) || Fixtures.Is(id) ||
            d.Objects.Any(o => o.Id == id) ||
            d.Selectors.Any(s => s.Id == id) ||
            d.Properties.Any(p => p.Id == id)
        );
        string name = exists ? Text.Name(d, id) : null;
        bool empty = !exists;
        return new Seg { Text = empty ? L.T("card.select_param", ParamName(param)) : name, Kind = kind, Action = action, Param = param, Empty = empty };
    }

    /// <summary>规则用到的对象列表的定义（卡片上展开时显示，参数改了会跟着变）。</summary>
    public static List<string> ListDefs(RulesData d, WorkRule r)
    {
        var ids = r.Pre.Where(p => p.RefKind == PreRef.Selector).Select(p => p.Ref)
            .Concat(r.Actions.SelectMany(a => new[] { a.List, a.Verb == ActVerb.Swap ? a.To : null, a.Tool })).Where(x => x != null).Distinct();
        return ids.Select(id => d.Selectors.FirstOrDefault(x => x.Id == id)).Where(x => x != null)
            .Select(x =>
            {
                // 用到的常量带上当前值（卡片上改了参数，这里马上看得到）
                var consts = Check.Closure(d, new[] { x.Id }).Select(c => d.Objects.FirstOrDefault(o => o.Id == c)).Where(o => o != null).ToList();
                string vals = consts.Count == 0 ? "" : L.T("text.paren", L.Join("list2", consts.Select(o => $"{o.Name} {o.Value:0.##}{o.Unit}")));
                return L.T("text.pair", x.Name, Text.SelectorShort(d, x)) + vals;
            }).ToList();
    }

    // ================= 卡片上的编辑 =================

    /// <summary>读写动作的某个参数（list / to / tool / button）。</summary>
    public static string Get(ActionDef a, string param) => param switch
    {
        "list" => a.List, "to" => a.To, "tool" => a.Tool, "button" => a.Button, _ => null,
    };

    public static void Set(ActionDef a, string param, string value)
    {
        switch (param)
        {
            case "list": a.List = value; break;
            case "to": a.To = value; break;
            case "tool": a.Tool = value; break;
            case "button": a.Button = value; break;
        }
    }

    /// <summary>换动作：新动作也有的参数原样保留（对象、目标、工具……）。</summary>
    public static ActionDef SwitchOp(RulesData d, ActionDef a, string verb, IWorld w = null)
    {
        var op = Catalog.Op(verb);
        var n = new ActionDef { Verb = verb };
        if (op.Mechanism == Mechanism.Note) { n.Note = a.Note; n.Value = a.Value; return n; }
        n.List = a.List;
        if (op.Mechanism is Mechanism.Place or Mechanism.Swap) n.To = a.To;
        if (op.Mechanism == Mechanism.Apply) { n.Tool = a.Tool; n.Times = Math.Max(1, a.Times); }
        if (op.Mechanism == Mechanism.Trigger) { n.Times = verb == ActVerb.Toggle ? 1 : Math.Max(1, a.Times); n.Button = a.Button; }
        if (op.Mechanism == Mechanism.Annotate) n.Text = string.IsNullOrEmpty(a.Text) ? "已处理" : a.Text;
        return n;
    }

    /// <summary>设置简单的执行前提：test 为空 = 每个班次都做。</summary>
    public static void SetPre(WorkRule r, string test, double n = 0)
    {
        if (test == null) { r.Pre.Clear(); return; }
        string sel = r.Pre.FirstOrDefault(p => p.RefKind == PreRef.Selector)?.Ref;
        r.Pre.Clear();
        r.Pre.Add(new PreCond { RefKind = PreRef.Selector, Ref = sel, Test = test, Right = Expr.Of(n) });
    }

    /// <summary>规则在排班里上移（-1）/ 下移（+1）。</summary>
    public static bool Move(Schedule s, WorkRule r, int delta)
    {
        int i = s.Rules.IndexOf(r), j = i + delta;
        if (i < 0 || j < 0 || j >= s.Rules.Count) return false;
        s.Rules.RemoveAt(i);
        s.Rules.Insert(j, r);
        return true;
    }
}
