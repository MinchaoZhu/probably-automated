using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>定义的通用操作：深拷贝、新 Id、不重名的名字、改写定义里的引用。</summary>
public static class Defs
{
    public static T Clone<T>(T x) => JsonCodec.Clone(x);

    /// <summary>序列化（结构比较用）。</summary>
    public static string Json<T>(T x) => JsonCodec.Serialize(x);

    /// <summary>
    /// 按 Id 找筛选器。设备名（"#名字"）自带一个不存档的筛选器：全店叫这个名字的东西本身。
    /// </summary>
    public static SelectorDef Selector(RulesData d, string id) =>
        string.IsNullOrEmpty(id) ? null : Places.IsName(id) ? NameSelector(id)
        : d.Selectors.FirstOrDefault(s => s.Id == id) ?? (d.Objects.FirstOrDefault(o => o.Id == id) is { IsRef: true } p ? ParamSelector(d, p) : null);

    /// <summary>
    /// 设备、列表参数自带的筛选器（不存档）：从参数取（设备参数取本身，列表参数的类型照它的值），
    /// 让检查、类型判断把参数当设备名 / 筛选器看。
    /// </summary>
    public static SelectorDef ParamSelector(RulesData d, ObjectDef p) =>
        new()
        {
            Id = p.Id, Name = p.Name, From = { p.Id }, Owner = p.Owner,
            Kind = p.Type == ParamType.Device ? Catalog.Self : d.Selectors.FirstOrDefault(s => s.Id == p.Ref)?.Kind ?? Catalog.Any,
        };

    /// <summary>设备或列表参数；别的为空。</summary>
    public static ObjectDef RefParam(RulesData d, string id) =>
        string.IsNullOrEmpty(id) ? null : d.Objects.FirstOrDefault(o => o.Id == id && o.IsRef);

    /// <summary>设备名自带的筛选器：从【名字】选本身。</summary>
    public static SelectorDef NameSelector(string id) =>
        new() { Id = id, Name = $"【{Places.NameOf(id)}】", From = { id }, Kind = Catalog.Self };

    /// <summary>
    /// 设备名展开成筛选器：新建一个属于排班 s 的筛选器（从【名字】选本身），s 里直接用【名字】当物品列表的地方
    /// （动作的对象、交换的另一组、执行条件）改成它。放入的目标、工具不变。
    /// </summary>
    public static SelectorDef ExpandName(RulesData d, Schedule s, string name)
    {
        string id = Places.Ref(name);
        var sel = NameSelector(id);
        sel.Id = NewId("s");
        sel.Name = UniqueName(d.Selectors.Select(x => x.Name), name);
        sel.Owner = s.Id;
        d.Selectors.Add(sel);
        foreach (var r in s.Rules)
        {
            foreach (var p in r.Pre) if (p.RefKind == PreRef.Selector && p.Ref == id) p.Ref = sel.Id;
            foreach (var a in r.Actions)
            {
                if (a.List == id) a.List = sel.Id;
                if (a.Verb == ActVerb.Swap && a.To == id) a.To = sel.Id;
            }
        }
        return sel;
    }

    /// <summary>
    /// 设备名转为参数：在排班 s 里新建一个设备参数（名字同设备名，值是这个设备名），s 的任务和本排班筛选器里
    /// 直接引用这个设备名的地方都改成引用参数。
    /// </summary>
    public static ObjectDef DeviceToParam(RulesData d, Schedule s, string name)
    {
        string id = Places.Ref(name);
        string pname = name;
        for (int i = 2; Scope.NameConflict(d, false, pname, s.Id) != null; i++) pname = L.T("transfer.dup_suffix", name, i);
        var p = new ObjectDef { Id = NewId("o"), Name = pname, Owner = s.Id, Type = ParamType.Device, Ref = id };
        d.Objects.Add(p);
        string M(string x) => x == id ? p.Id : x;
        foreach (var r in s.Rules) MapRule(r, M);
        foreach (var x in d.Selectors.Where(x => x.Owner == s.Id)) MapSelector(x, M);
        return p;
    }

    /// <summary>新排班的建立时间。</summary>
    public static long Now() => DateTime.UtcNow.Ticks;

    /// <summary>选排班时的顺序：新建的在前（建立时间从新到旧），同时间的按排班列表顺序。执行顺序仍是排班列表的顺序。</summary>
    public static List<Schedule> PickOrder(RulesData d) =>
        d.Schedules.Select((s, i) => (s, i)).OrderByDescending(t => t.s.Created).ThenBy(t => t.i).Select(t => t.s).ToList();

    public static string NewId(string prefix) => prefix + "_" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>名字已被占用时加“（2）”“（3）”……</summary>
    public static string UniqueName(IEnumerable<string> existing, string name)
    {
        var set = new HashSet<string>(existing);
        if (!set.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string cand = name + L.T("text.paren", i);
            if (!set.Contains(cand)) return cand;
        }
    }

    /// <summary>改写引用（m：旧 Id → 新 Id）。</summary>
    public static void MapExpr(Expr e, Func<string, string> m)
    {
        if (e?.Terms == null) return;
        foreach (var t in e.Terms) if (t.Kind != TermKind.Num && t.Kind != TermKind.Sys) t.Ref = m(t.Ref);
    }

    public static void MapCond(Cond c, Func<string, string> m)
    {
        if (c == null) return;
        if (c.Group != null) { foreach (var x in c.Group.Items) MapCond(x, m); return; }
        c.Prop = m(c.Prop);
        MapExpr(c.Right, m);
    }

    public static void MapSelector(SelectorDef s, Func<string, string> m)
    {
        s.From = s.From.Select(m).ToList();
        s.Minus = m(s.Minus);
        foreach (var c in s.Where.Items) MapCond(c, m);
        if (s.Sort != null) s.Sort.Prop = m(s.Sort.Prop);
        if (s.Take != null) MapExpr(s.Take.N, m);
        if (s.Agg != null) s.Agg.Prop = m(s.Agg.Prop);
    }

    public static void MapAction(ActionDef a, Func<string, string> m)
    {
        a.List = m(a.List); a.To = m(a.To); a.Tool = m(a.Tool); a.Note = m(a.Note);
        MapExpr(a.Value, m);
        if (a.Args != null) foreach (var x in a.Args) x.Ref = m(x.Ref); // 参数 id 是被调用流程的，不在这里换
    }

    public static void MapRule(WorkRule r, Func<string, string> m)
    {
        foreach (var p in r.Pre) { p.Ref = m(p.Ref); MapExpr(p.Right, m); MapCond(p.Where, m); }
        foreach (var a in r.Actions) MapAction(a, m);
    }
}
