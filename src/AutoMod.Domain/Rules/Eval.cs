using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>挑选器的值：对象列表或数值。</summary>
public class SelVal
{
    public List<object> Items;
    public double? Num;
    public bool IsNumber;

    public static readonly SelVal Empty = new() { Items = new List<object>() };
}

/// <summary>
/// 求值上下文：一条规则内的快照。挑选器、自定义属性首次求值后缓存，
/// 规则开始时新建或 Clear()。记事本、系统变量只在执行前提和记录里可用。
/// </summary>
public class EvalCtx
{
    public IWorld W;
    public RulesData D;
    public Dictionary<string, double> Memo;
    public Func<string, double?> Sys;

    internal readonly Dictionary<string, SelVal> SelCache = new();
    internal readonly HashSet<string> Evaluating = new();
    internal readonly Dictionary<(object, string), double?> PropCache = new();

    public EvalCtx(IWorld w, RulesData d)
    {
        W = w;
        D = d;
    }

    public void Clear()
    {
        SelCache.Clear();
        PropCache.Clear();
    }
}

public static class Eval
{
    // ---------- 表达式 ----------

    /// <summary>表达式求值：先乘除后加减；任一项为空或除以 0 → 空。</summary>
    public static double? Expr(EvalCtx c, Expr e, object item = null)
    {
        if (e?.Terms == null || e.Terms.Count == 0) return null;
        var vals = new List<double>();
        foreach (var t in e.Terms)
        {
            var v = TermValue(c, t, item);
            if (v is not { } x || double.IsNaN(x)) return null;
            vals.Add(x);
        }
        // 第一遍：乘除
        var stack = new List<double> { vals[0] };
        var adds = new List<string>();
        for (int i = 1; i < vals.Count; i++)
        {
            string op = i - 1 < e.Ops.Count ? e.Ops[i - 1] : "+";
            if (op == "*") stack[^1] *= vals[i];
            else if (op == "/")
            {
                if (vals[i] == 0) return null;
                stack[^1] /= vals[i];
            }
            else { stack.Add(vals[i]); adds.Add(op); }
        }
        // 第二遍：加减
        double r = stack[0];
        for (int i = 0; i < adds.Count; i++) r = adds[i] == "-" ? r - stack[i + 1] : r + stack[i + 1];
        return r;
    }

    private static double? TermValue(EvalCtx c, Term t, object item)
    {
        switch (t.Kind)
        {
            case TermKind.Num: return t.Num;
            case TermKind.Const:
                return c.D.Objects.FirstOrDefault(x => x.Id == t.Ref)?.Value;
            case TermKind.Own: return item == null ? null : Prop(c, item, t.Ref);
            case TermKind.Stat: return Number(c, t.Ref);
            case TermKind.Note: return c.Memo != null && t.Ref != null && c.Memo.TryGetValue(t.Ref, out var m) ? m : null;
            case TermKind.Sys: return c.Sys?.Invoke(t.Ref);
            default: return null;
        }
    }

    // ---------- 属性 ----------

    /// <summary>属性值：内置属性由世界提供；自定义属性按公式计算（不适用的类型为空）。</summary>
    public static double? Prop(EvalCtx c, object item, string prop)
    {
        if (item == null || string.IsNullOrEmpty(prop)) return null;
        var def = c.D.Properties.FirstOrDefault(p => p.Id == prop);
        if (def == null)
        {
            // 位置属性：只看它在哪里，两种世界一样算
            switch (prop)
            {
                case "inStorage": return c.W.Parent(item) is { } p && Catalog.Matches(c.W, p, "storage") ? 1 : 0; // 直接放在储藏区里（游戏的商品类型 STORAGE）
                case "inBatterySlot": return c.W.Parent(item) is { } q && c.W.Contents(q, "battery").Any(x => Equals(c.W.Key(x), c.W.Key(item))) ? 1 : 0;
            }
            // 派生属性（电量% 等）由目录计算，世界只提供基础值
            if (Catalog.Prop(prop)?.Ratio is var (num, den))
                return c.W.Num(item, num) is { } a && c.W.Num(item, den) is { } b && b > 0 ? a * 100 / b : null;
            return c.W.Num(item, prop);
        }
        var key = (c.W.Key(item), prop);
        if (c.PropCache.TryGetValue(key, out var cached)) return cached;
        double? v = Catalog.Matches(c.W, item, def.ItemKind) ? Expr(c, def.Formula, item) : null;
        c.PropCache[key] = v;
        return v;
    }

    // ---------- 条件 ----------

    public static bool Cond(EvalCtx c, Cond cond, object item)
    {
        if (cond == null) return true;
        if (cond.Group != null) return Group(c, cond.Group, item);
        if (!string.IsNullOrEmpty(cond.Preset))
        {
            var p = Catalog.Preset(cond.Preset);
            return p != null && (p.Applies == null || Catalog.HasTrait(c.W, item, p.Applies)) && Group(c, p.Where, item);
        }
        if (cond.Prop == Catalog.TypeProp && cond.Text != null)
        {
            // 类型条件：是 / 不是某类型（类别、种类、商品类型、物品家族）
            bool match = Catalog.Matches(c.W, item, cond.Text);
            return cond.Op == "!=" ? !match : match;
        }
        if (cond.Text != null)
        {
            bool eq = c.W.Str(item, cond.Prop) == cond.Text;
            return cond.Op == "!=" ? !eq : eq;
        }
        return Vocab.Compare(Prop(c, item, cond.Prop), cond.Op, Expr(c, cond.Right, item));
    }

    public static bool Group(EvalCtx c, CondGroup g, object item)
    {
        if (g?.Items == null || g.Items.Count == 0) return true;
        return g.IsAny ? g.Items.Any(x => Cond(c, x, item)) : g.Items.All(x => Cond(c, x, item));
    }

    // ---------- 挑选器 ----------

    public static SelectorDef Sel(EvalCtx c, string id) => Defs.Selector(c.D, id);
    public static ObjectDef Obj(EvalCtx c, string id) => c.D.Objects.FirstOrDefault(o => o.Id == id);

    /// <summary>挑选器是否产出容器列表（类型为容器类型或“容器本身”）。</summary>
    /// <summary>
    /// 挑选器选出的是容器（可以往里放、在里面找）：类型是容器类别；有世界时，具体种类按游戏里这类物品是不是容器判断
    /// （例如“饲料分配器”“鱼缸”）。
    /// </summary>
    public static bool YieldsBoxes(SelectorDef s, IWorld w = null) =>
        s != null && !s.IsNumber && (Catalog.IsContainerType(s.Kind)
            || w != null && !string.IsNullOrEmpty(s.Kind) && s.Kind.Split('|').All(k => !Catalog.IsTrait(k) && Catalog.Closure(w.KindTraits(k)).Contains(Catalog.Container)));

    public static SelVal Selector(EvalCtx c, string id)
    {
        if (id == null) return SelVal.Empty;
        if (c.SelCache.TryGetValue(id, out var cached)) return cached;
        var s = Sel(c, id);
        using var _p = c.Evaluating.Count == 0 ? Perf.Begin("selector") : default; // 只计最外层：来源筛选器的用时已经含在里面
        if (s == null || !c.Evaluating.Add(id)) return SelVal.Empty; // 不存在，或意外的循环引用
        try
        {
            var v = Compute(c, s);
            c.SelCache[id] = v;
            return v;
        }
        finally { c.Evaluating.Remove(id); }
    }

    /// <summary>
    /// 物品列表为空时的说明：设备名“店内没有【X】”；筛选器“「X」当前为空（来源中有 3 件，都不满足条件）”。
    /// </summary>
    public static string Explain(EvalCtx c, string id)
    {
        if (Places.IsName(id)) return L.T("eval.no_named", Places.NameOf(id));
        var s = Sel(c, id);
        if (s == null || s.IsNumber) return null;
        var seen = new HashSet<object>();
        int n = 0;
        foreach (var src in s.From)
            foreach (var x in Expand(c, s, src))
                if (seen.Add(c.W.Key(x)) && (s.Kind == Catalog.Self || Catalog.Matches(c.W, x, s.Kind))) n++;
        return n == 0 ? L.T("eval.empty_no_items", s.Name) : L.N("eval.empty_not_matching", n, s.Name);
    }

    /// <summary>对象列表；统计值挑选器返回空列表。</summary>
    public static List<object> List(EvalCtx c, string id) => Selector(c, id) is { IsNumber: false } v ? v.Items : new List<object>();

    /// <summary>数值；列表取数量。</summary>
    public static double? Number(EvalCtx c, string id)
    {
        var v = Selector(c, id);
        return v.IsNumber ? v.Num : v.Items.Count;
    }

    /// <summary>目标容器：起了这个设备名的东西，或筛选器的结果。</summary>
    public static IReadOnlyList<object> Boxes(EvalCtx c, string id)
    {
        if (string.IsNullOrEmpty(id) || Obj(c, id) != null || Places.IsShop(id)) return Array.Empty<object>();
        if (Places.IsName(id)) return c.W.Named(Places.NameOf(id));
        return List(c, id);
    }

    private static SelVal Compute(EvalCtx c, SelectorDef s)
    {
        // ① 来源 ② 展开（按 uid 去重，保留首次出现顺序）
        var items = new List<object>();
        var seen = new HashSet<object>();
        foreach (var src in s.From)
            foreach (var x in Expand(c, s, src))
                if (seen.Add(c.W.Key(x))) items.Add(x);

        // ③ 类型 ④ 条件
        IEnumerable<object> q = items;
        if (s.Kind != Catalog.Self) q = q.Where(x => Catalog.Matches(c.W, x, s.Kind));
        q = q.Where(x => Group(c, s.Where, x));
        var list = q.ToList();

        // ⑤ 排除
        if (!string.IsNullOrEmpty(s.Minus))
        {
            var minus = new HashSet<object>(List(c, s.Minus).Select(c.W.Key));
            list = list.Where(x => !minus.Contains(c.W.Key(x))).ToList();
        }

        // ⑥b 统计
        if (s.IsNumber)
        {
            var agg = s.Agg ?? new AggSpec();
            if (agg.Fn == AggFn.Count) return new SelVal { IsNumber = true, Num = list.Count };
            var vals = list.Select(x => Prop(c, x, agg.Prop)).Where(v => v.HasValue && !double.IsNaN(v.Value)).Select(v => v.Value).ToList();
            double? r = vals.Count == 0 ? null : agg.Fn switch
            {
                AggFn.Min => vals.Min(), AggFn.Max => vals.Max(), AggFn.Sum => vals.Sum(), AggFn.Avg => vals.Average(), _ => null,
            };
            return new SelVal { IsNumber = true, Num = r };
        }

        // ⑥a 排序（稳定；相等按 uid 升序）+ 截取
        if (s.Sort != null && !string.IsNullOrEmpty(s.Sort.Prop))
        {
            var keyed = list.Select(x => (x, k: Prop(c, x, s.Sort.Prop) ?? double.NegativeInfinity, uid: c.W.Uid(x))).ToList();
            keyed = (s.Sort.Desc ? keyed.OrderByDescending(t => t.k) : keyed.OrderBy(t => t.k)).ThenBy(t => t.uid).ToList();
            list = keyed.Select(t => t.x).ToList();
        }
        if (s.Take != null)
        {
            int n = (int)Math.Floor(Expr(c, s.Take.N) ?? 0);
            list = s.Take.Mode == TakeMode.From
                ? (n >= 1 ? list.Skip(n - 1).ToList() : new List<object>())
                : list.Take(Math.Max(0, n)).ToList();
        }
        return new SelVal { Items = list };
    }

    /// <summary>来源展开。</summary>
    public static IEnumerable<object> Expand(EvalCtx c, SelectorDef s, string src)
    {
        IReadOnlyList<object> source;
        bool containers;
        if (Places.IsShop(src))
        {
            // 全店：给了槽位就是店里各个容器的这个槽位；没给就是店里所有东西（再按类型、条件筛）
            if (string.IsNullOrEmpty(s.Slot))
            {
                foreach (var x in c.W.Shop()) yield return x;
                yield break;
            }
            foreach (var box in c.W.Shop())
                if (Catalog.Matches(c.W, box, Catalog.Container))
                    foreach (var x in c.W.Contents(box, s.Slot)) yield return x;
            yield break;
        }
        if (Places.IsName(src))
        {
            source = c.W.Named(Places.NameOf(src));
            containers = true;
        }
        else if (Obj(c, src) != null) yield break;
        else
        {
            var ss = Sel(c, src);
            if (ss == null || ss.IsNumber) yield break;
            source = List(c, src);
            containers = YieldsBoxes(ss, c.W);
        }
        if (!containers)
        {
            foreach (var x in source) yield return x; // 物品列表：在列表内筛选
            yield break;
        }
        foreach (var box in source)
        {
            if (!string.IsNullOrEmpty(s.Slot))
            {
                foreach (var x in c.W.Contents(box, s.Slot)) yield return x;
            }
            else if (s.Kind == Catalog.Self)
            {
                yield return box; // 范围是“本身”：容器自己
            }
            else
            {
                foreach (var x in c.W.Contents(box, null)) yield return x; // 直接内容物
            }
        }
    }

    public enum ExpandKind { None, Slot, Self, Contents }

    /// <summary>来源展开的结构化种类（本身、内容物、槽位）。</summary>
    public static (ExpandKind kind, string slot) Expand(RulesData d, SelectorDef s, string src)
    {
        var slot = s.Slot == "battery" ? L.T("eval.slot.battery") : s.Slot == "filter" ? L.T("eval.slot.filter") : s.Slot != null && s.Slot.StartsWith(SlotNames.Prefix) ? SlotNames.Name(s.Slot, d, s.From) : null;
        if (Places.IsShop(src)) return slot == null ? (ExpandKind.None, null) : (ExpandKind.Slot, slot);
        if (!Places.IsName(src) && !YieldsBoxes(d.Selectors.FirstOrDefault(x => x.Id == src))) return (ExpandKind.None, null);
        return slot != null ? (ExpandKind.Slot, slot) : s.Kind == Catalog.Self ? (ExpandKind.Self, null) : (ExpandKind.Contents, null);
    }

    /// <summary>一个来源在某筛选器里怎么展开，接在来源名字后面：本身 / 内的物品 / 的某槽位；物品列表和全店为空。</summary>
    public static string ExpandWord(RulesData d, SelectorDef s, string src)
    {
        var (kind, slot) = Expand(d, s, src);
        return kind switch
        {
            ExpandKind.Slot => L.T("eval.expand.slot", slot),
            ExpandKind.Self => L.T("eval.expand.self"),
            ExpandKind.Contents => L.T("eval.expand.contents"),
            _ => "",
        };
    }
}
