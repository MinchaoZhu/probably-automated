using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>筛选器的上下级。只看范围（From）：排除、条件、件数里的引用不算。</summary>
public static class SelectorFamily
{
    /// <summary>全部上级，从根到近；循环引用时访问过的跳过。</summary>
    public static List<SelectorDef> Ancestors(RulesData d, SelectorDef s)
    {
        var seen = new HashSet<string> { s.Id };
        var list = new List<SelectorDef>();
        void Visit(SelectorDef x)
        {
            foreach (var id in x.From)
            {
                var p = d.Selectors.FirstOrDefault(v => v.Id == id);
                if (p == null || !seen.Add(id)) continue;
                Visit(p);
                list.Add(p);
            }
        }
        Visit(s);
        return list;
    }

    /// <summary>直接子筛选器：列表顺序，只取 view（排班 Id；为空 = 只看全局）能看到的。</summary>
    public static List<SelectorDef> Children(RulesData d, SelectorDef s, string view) =>
        d.Selectors.Where(x => x.Id != s.Id && x.From.Contains(s.Id) && Scope.Visible(d, view, x.Id)).ToList();

    /// <summary>来源是结果为物品列表的筛选器：范围方式和槽位都不起作用，只在列表内筛选（见 Eval.Expand）。</summary>
    public static bool IsItemList(RulesData d, string src, IWorld w)
    {
        if (string.IsNullOrEmpty(src) || Places.IsShop(src) || Places.IsDevice(src)) return false;
        var s = Defs.Selector(d, src); // 设备参数是容器，列表参数看它的值
        return s != null && !s.IsNumber && !Eval.YieldsBoxes(s, w);
    }
}

/// <summary>筛选器对话框里的一项：一份草稿和它打开时的样子。</summary>
public class SelectorDraft
{
    public SelectorDef Def;

    /// <summary>已保存的原件；新建的为空。</summary>
    public SelectorDef Orig;

    public bool NameTouched;
    public bool More;
    internal string Baseline;

    public bool IsNew => Orig == null;
    public bool Dirty => IsNew || Defs.Json(Def) != Baseline;
}

/// <summary>
/// 筛选器对话框的一组草稿：上下级在同一个对话框里编辑，保存时一起写入，取消时全部放弃。
/// 草稿不写进规则数据；求值、检查、显示名称都用 Overlay（规则数据 + 草稿）。
/// </summary>
public class SelectorDrafts
{
    private readonly RulesData _data;
    private readonly IWorld _world;
    private readonly string _view;
    private readonly List<SelectorDraft> _created = new();
    private readonly List<SelectorDraft> _removed = new();

    /// <summary>显示顺序：上级在上，子筛选器在下。已显示的不移除。</summary>
    public List<SelectorDraft> Entries { get; } = new();

    /// <summary>展开的一项；全部收起时为空。</summary>
    public SelectorDraft Active { get; private set; }

    public bool AnyDirty => _removed.Count > 0 || Entries.Any(e => e.Dirty);

    private SelectorDrafts(RulesData data, IWorld world, string view)
    {
        _data = data;
        _world = world;
        _view = view;
    }

    /// <summary>编辑已有筛选器：上级在上方，它自己展开，直接子筛选器在下方。</summary>
    public static SelectorDrafts Edit(RulesData data, IWorld world, SelectorDef s, string view)
    {
        var r = new SelectorDrafts(data, world, view);
        var e = r.Wrap(s);
        r.Entries.Add(e);
        r.Expand(e);
        return r;
    }

    /// <summary>新建：只有这一项。</summary>
    public static SelectorDrafts New(RulesData data, IWorld world, SelectorDef def, string view)
    {
        var r = new SelectorDrafts(data, world, view);
        var e = new SelectorDraft { Def = def, Baseline = "" };
        r.Entries.Add(e);
        r._created.Add(e);
        r.Active = e;
        return r;
    }

    private SelectorDraft Wrap(SelectorDef s)
    {
        var def = Defs.Clone(s);
        def.Where ??= new CondGroup();
        if (def.From.Count > 0 && def.From.All(f => SelectorFamily.IsItemList(_data, f, _world))) ClearScopeMode(def);
        return new SelectorDraft
        {
            Def = def, Orig = s, NameTouched = true,
            More = def.Minus != null || def.Where.Items.Any(c => c.Group != null),
            Baseline = Defs.Json(def),
        };
    }

    /// <summary>来源全是物品列表时，范围方式不起作用，清掉残留的槽位和“本身”（结果不变）。</summary>
    public static void ClearScopeMode(SelectorDef d)
    {
        d.Slot = null;
        if (d.Kind == Catalog.Self) d.Kind = Catalog.Any;
    }

    /// <summary>规则数据 + 草稿：未保存的改动、新建项都在里面，标记删除的不在。</summary>
    public RulesData Overlay()
    {
        var byId = Entries.ToDictionary(e => e.Def.Id);
        var gone = _removed.Select(e => e.Def.Id).ToHashSet();
        var list = _data.Selectors.Where(x => !gone.Contains(x.Id)).Select(x => byId.TryGetValue(x.Id, out var e) ? e.Def : x).ToList();
        list.AddRange(Entries.Where(e => _data.Selectors.All(x => x.Id != e.Def.Id)).Select(e => e.Def));
        return new RulesData { Objects = _data.Objects, Properties = _data.Properties, Selectors = list, Schedules = _data.Schedules };
    }

    public void Collapse() => Active = null;

    /// <summary>展开一项：把它还没显示的上级插到上方、直接子筛选器插到下方。</summary>
    public void Expand(SelectorDraft e)
    {
        Active = e;
        var view = Overlay();
        foreach (var p in SelectorFamily.Ancestors(view, e.Def))
            if (Entries.All(x => x.Def.Id != p.Id)) Entries.Insert(Entries.IndexOf(e), Wrap(p));
        int at = Entries.IndexOf(e) + 1;
        foreach (var c in SelectorFamily.Children(view, e.Def, _view))
            if (Entries.All(x => x.Def.Id != c.Id)) Entries.Insert(at++, Wrap(c));
    }

    /// <summary>新建子筛选器并展开；上级的结果是数值时不能作为范围，返回空。</summary>
    /// <param name="fallbackOwner">上级是全局时子筛选器的所属排班（当前打开的流程；为空 = 全局）。</param>
    public SelectorDraft AddChild(SelectorDraft parent, string fallbackOwner)
    {
        if (parent.Def.IsNumber) return null;
        var def = new SelectorDef { Id = Defs.NewId("s"), From = { parent.Def.Id }, Kind = Catalog.Any, Where = new CondGroup(), Owner = parent.Def.Owner ?? fallbackOwner };
        var e = new SelectorDraft { Def = def, Baseline = "" };
        int at = Entries.IndexOf(parent);
        for (int i = at + 1; i < Entries.Count; i++)
            if (Entries[i].Def.From.Contains(parent.Def.Id)) at = i;
        Entries.Insert(at + 1, e);
        _created.Add(e);
        Active = e;
        return e;
    }

    /// <summary>复制成新草稿，插在原项下方并展开。</summary>
    public SelectorDraft Copy(SelectorDraft source, string fallbackOwner)
    {
        var def = Defs.Clone(source.Def);
        def.Id = Defs.NewId("s");
        def.Owner = fallbackOwner ?? source.Def.Owner;
        var e = new SelectorDraft { Def = def, Baseline = "", NameTouched = true, More = source.More };
        Entries.Insert(Entries.IndexOf(source) + 1, e);
        var view = Overlay();
        string name = L.T("selector.copy_name", source.Def.Name);
        for (int i = 2; Scope.NameConflict(view, true, name, def.Owner, def.Id) != null; i++) name = L.T("selector.copy_name_seq", source.Def.Name, i);
        def.Name = name;
        _created.Add(e);
        Active = e;
        return e;
    }

    /// <summary>删除一项。仍被引用（含其他草稿）时不删除，返回提示；已保存的项在保存时才真正删除。</summary>
    public string Remove(SelectorDraft e)
    {
        var refs = Check.Referrers(Overlay(), e.Def.Id);
        if (refs.Count > 0) return L.T("selector.delete_still_used", L.Join("list", refs.Take(4)));
        int at = Entries.IndexOf(e);
        Entries.RemoveAt(at);
        _created.Remove(e);
        if (!e.IsNew) _removed.Add(e);
        if (Active == e) Active = null;
        return null;
    }

    /// <summary>第一个不能保存的项和原因；都能保存时为空。</summary>
    public (SelectorDraft at, string why)? Validate()
    {
        foreach (var e in Entries) e.Def.Name = (e.Def.Name ?? "").Trim();
        var view = Overlay();
        foreach (var e in Entries)
        {
            if (e.Def.Name.Length == 0) return (e, L.T("object_dlg.name_empty"));
            if (Scope.NameConflict(view, true, e.Def.Name, e.Def.Owner, e.Def.Id) is { } conflict) return (e, conflict + "。");
            if (e.Def.From.Count == 0) return (e, L.T("selector.save_no_scope"));
        }
        return null;
    }

    /// <summary>写入规则数据：有实际改动的项、新建项、标记删除的项。校验不通过时什么都不写，展开出错的项并返回原因。</summary>
    public string Commit()
    {
        if (Validate() is { } bad)
        {
            Active = bad.at;
            return bad.why;
        }
        foreach (var e in Entries)
        {
            var d = e.Def;
            if (!d.IsNumber) d.Agg = null;
            else { d.Sort = null; d.Take = null; d.Minus = null; }
            if (!e.Dirty) continue;
            int i = _data.Selectors.FindIndex(x => x.Id == d.Id);
            if (i >= 0) _data.Selectors[i] = d;
            else _data.Selectors.Add(d);
        }
        // 对话框打开期间任务可能改过：仍被引用的不删
        foreach (var e in _removed)
            if (Check.Referrers(_data, e.Def.Id).Count == 0) _data.Selectors.RemoveAll(x => x.Id == e.Def.Id);
        Scope.Normalize(_data);
        return null;
    }

    /// <summary>从任务里新建时，任务引用的筛选器：最后新建的物品筛选器；都不是物品筛选器时取最初新建的那一项。</summary>
    public SelectorDef TaskTarget =>
        _created.LastOrDefault(e => Entries.Contains(e) && !e.Def.IsNumber)?.Def ?? _created.FirstOrDefault(Entries.Contains)?.Def;
}

/// <summary>筛选器或设备名此刻的结果：用当前世界求值，不改变任何物品。</summary>
public class SelectorPreview
{
    public bool IsNumber;
    public double? Number;
    public int Count;

    /// <summary>前几件，顺序就是筛选器输出的顺序。</summary>
    public List<object> Items = new();

    /// <summary>结果为空时的原因。</summary>
    public string EmptyReason;

    /// <summary>不是筛选器或设备名时为空。</summary>
    public static SelectorPreview Of(EvalCtx c, string id, int limit)
    {
        if (Defs.Selector(c.D, id) == null) return null;
        var v = Eval.Selector(c, id);
        if (v.IsNumber) return new SelectorPreview { IsNumber = true, Number = v.Num };
        var p = new SelectorPreview { Count = v.Items.Count, Items = v.Items.Take(limit).ToList() };
        if (p.Count == 0) p.EmptyReason = Eval.Explain(c, id);
        return p;
    }
}
