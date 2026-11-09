using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.Adapter;
using AutoMod.App;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>排班界面的公共工具：常用选项列表；数据入口和结构检查转到 App.Session。</summary>
public static class RulesUi
{
    public static RulesData D => Session.D;
    public static GameWorld World => Session.World;
    public static List<string> RuleErrors(Schedule s, WorkRule r) => Session.RuleErrors(s, r);
    public static int ScheduleErrors(Schedule s) => Session.ScheduleErrors(s);
    public static void Changed() => Session.Changed();
    public static void InvalidateWorld() => Session.InvalidateWorld();
    public static string ContainerName(int uid) => Session.ContainerName(uid);

    // ---------- 选项 ----------

    /// <summary>
    /// 当前视角：排班 Id = 这个排班能看到的（本排班 + 全局，本排班的在前）；为空 = 只看全局。
    /// 排班编辑器、卡片按所属排班设置；编辑全局挑选器、自定义属性时设为空。
    /// </summary>
    public static string View { get; set; }

    public static List<SelectorDef> VisibleSelectors => Scope.Selectors(D, View);
    public static List<ObjectDef> Constants => Scope.Objects(D, View);

    /// <summary>设备名：店里已经起了的，加上排班里用到、店里还没有的（引用 "#名字"，显示“【名字】”）。</summary>
    public static List<(string id, string name)> DeviceNames()
    {
        var names = App.ExampleSetup.DeviceNames();
        foreach (var n in D.Selectors.SelectMany(Check.SelectorRefs).Concat(D.Schedules.SelectMany(x => x.Rules).SelectMany(Check.RuleRefs)).Where(Places.IsName).Select(Places.NameOf))
            if (!names.Contains(n)) names.Add(n);
        return names.Select(n => (Places.Ref(n), DeviceLabel(n))).ToList();
    }

    public static string DeviceLabel(string name) => Domain.L.T("schedule.device_label", name);

    /// <summary>可作为目标的：容器的设备名（店里还没有的也列上）+ 选出容器的筛选器。</summary>
    public static List<(string id, string name)> Destinations() =>
        DeviceNames().Where(x => World.Named(Places.NameOf(x.id)) is not { Count: > 0 } named || named.All(o => Catalog.Matches(World, o, Catalog.Container))).Concat(VisibleSelectors.Where(s => Eval.YieldsBoxes(s, World)).Select(s => (s.Id, s.Name + Domain.L.T("schedule.selector_suffix")))).ToList();

    /// <summary>工具：水龙头这类固定设施、设备名（它本身和它里面的）、筛选器（它选出的）。</summary>
    public static List<(string id, string name)> ToolSources() =>
        Fixtures.All.Select(f => (f.id, f.name))
            .Concat(DeviceNames())
            .Concat(VisibleSelectors.Where(s => !s.IsNumber).Select(s => (s.Id, s.Name + Domain.L.T("schedule.selector_suffix"))))
            .ToList();

    /// <summary>目标（容器对象或挑选器）此刻的容器上有哪些格子（机器窗口的面板）；当前选中的不在里面也列上。</summary>
    public static List<(string id, string name)> SlotsOf(string target, string current)
    {
        var list = new List<(string id, string name)>();
        try
        {
            if (!string.IsNullOrEmpty(target))
                foreach (var box in Eval.Boxes(new EvalCtx(World, D), target).Take(40))
                    foreach (var sl in World.Slots(box))
                        if (list.All(x => x.id != sl.id)) list.Add(sl);
        }
        catch { }
        if (!string.IsNullOrEmpty(current) && list.All(x => x.id != current)) list.Add((current, SlotNames.Name(current, D, new[] { target })));
        return list;
    }

    /// <summary>对象列表此刻选出的机器上有哪些按钮（显示文字）；当前选中的排第一。</summary>
    public static List<(string id, string name)> ButtonsOf(string list, string current)
    {
        var names = new List<string>();
        try
        {
            if (!string.IsNullOrEmpty(list))
                foreach (var it in Eval.List(new EvalCtx(World, D), list).Take(20))
                    foreach (var b in World.Buttons(it))
                        if (!names.Contains(b)) names.Add(b);
        }
        catch { }
        if (!string.IsNullOrEmpty(current)) { names.Remove(current); names.Insert(0, current); }
        return names.Select(n => (n, Domain.L.T("text.bracket", MachineButtons.DisplayName(n)))).ToList();
    }

    /// <summary>可作为物品列表的：设备名（叫这个名字的东西本身）+ 物品筛选器。</summary>
    public static List<(string id, string name)> ItemSelectors => DeviceNames().Concat(VisibleSelectors.Where(s => !s.IsNumber).Select(s => (s.Id, s.Name))).ToList();

    public static string Name(string id) => AutoMod.Domain.Rules.Text.Name(D, id);

    /// <summary>见过的具体种类 → 大类（列类型选项时记下，本次游戏会话内有效）。</summary>
    private static readonly Dictionary<string, string> KindGroups = new();

    public static void NoteKindGroup(string kind, object item) => KindGroups[kind] = World.GroupOf(item);

    /// <summary>类型选项的大类；“全部物品”这类通用选项为空，只在“全部”下显示。</summary>
    public static string KindGroup(string id) =>
        ItemGroups.OfTraitOption(id) ?? (id != null && KindGroups.TryGetValue(id, out var g) ? g : null);

    /// <summary>
    /// 某类型可用的属性：内置 + 适用的自定义属性 + 游戏标签（排在最后）。给了来源里的东西时，游戏标签取自它们
    /// （“全部物品”也能列出来源里水瓶的含水量）；没给时取店里这类物品。
    /// </summary>
    public static List<string> PropsFor(string kind, IReadOnlyList<object> sourceItems = null) =>
        Catalog.PropsFor(kind, World).Concat(D.Properties.Where(p => kind == Catalog.Any || kind == null || p.ItemKind == kind || p.ItemKind == Catalog.Any
            || Catalog.Satisfies(kind, p.ItemKind, World)).Select(p => p.Id))
            .Concat(sourceItems != null && sourceItems.Count > 0 ? SourceTagProps(kind, sourceItems) : GameTagProps(kind)).Distinct().ToList();

    /// <summary>
    /// 筛选器的来源里此刻有的东西（来源是设备名：里面的东西，或只取某个槽位的，选“本身”时是它们自己；来源是筛选器：它选出的），最多 40 件。
    /// </summary>
    /// <param name="data">求值用的数据（对话框的草稿）；为空 = 规则数据。</param>
    public static List<object> SourceItems(SelectorDef d, RulesData data = null)
    {
        var list = new List<object>();
        data ??= D;
        try
        {
            var ctx = new EvalCtx(World, data);
            foreach (var src in d.From)
            {
                if (Places.IsShop(src))
                {
                    list.AddRange(string.IsNullOrEmpty(d.Slot) ? World.Shop().Take(40)
                        : World.Shop().Where(x => Catalog.Matches(World, x, Catalog.Container)).SelectMany(x => World.Contents(x, d.Slot)).Take(40));
                    if (list.Count >= 40) break;
                    continue;
                }
                var sel = data.Selectors.FirstOrDefault(x => x.Id == src);
                bool boxes = Places.IsName(src) || (sel != null && Eval.YieldsBoxes(sel, World));
                bool self = d.Kind == Catalog.Self && string.IsNullOrEmpty(d.Slot); // 选本身：来源里的就是它们自己
                foreach (var x in Eval.Boxes(ctx, src))
                {
                    list.AddRange(boxes && !self ? World.Contents(x, d.Slot) : new[] { x });
                    if (list.Count >= 40) break;
                }
                if (list.Count >= 40) break;
            }
        }
        catch { }
        return list.Take(40).ToList();
    }

    // ---------- 当前结果 ----------

    /// <summary>下拉列表旁的信息面板显示几件。</summary>
    private const int TipLines = 6;

    /// <summary>折叠行上的件数或数值。</summary>
    public static string CountText(EvalCtx ctx, string id)
    {
        try
        {
            if (SelectorPreview.Of(ctx, id, 0) is not { } p) return "";
            if (p.IsNumber) return NumberText(p);
            return p.Count == 0 ? Domain.L.T("selector.scope_empty") : Domain.L.N("selector.scope_count", p.Count, "");
        }
        catch { return ""; }
    }

    private static string NumberText(SelectorPreview p) =>
        p.Number is { } v ? Domain.L.T("selector.result_number", v.ToString("0.##")) : Domain.L.T("selector.result_no_value");

    /// <summary>筛选器或设备名此刻的结果，多行文字；不是这两者时为空。</summary>
    /// <param name="withPlace">每件后面带上所在容器（下拉列表旁的面板窄，不带）。</param>
    public static string PreviewText(EvalCtx ctx, string id, int limit, bool withPlace = true)
    {
        try
        {
            if (SelectorPreview.Of(ctx, id, limit) is not { } p) return null;
            if (p.IsNumber) return NumberText(p);
            if (p.Count == 0) return p.EmptyReason ?? Domain.L.T("selector.scope_empty");
            var lines = new List<string> { Domain.L.N("selector.scope_count", p.Count, "") };
            foreach (var x in p.Items)
            {
                string at = withPlace && World.Parent(x) is GameItem box ? World.DistinctName(box) : null;
                lines.Add(at == null ? World.Describe(x) : World.Describe(x) + Domain.L.T("text.dot_sep") + at);
            }
            if (p.Count > p.Items.Count) lines.Add(Domain.L.N("selector.result_more", p.Count));
            return string.Join("\n", lines);
        }
        catch { return null; }
    }

    /// <summary>刚加的下拉框：悬停某一行时显示该项的当前结果。ids 和选项一一对应。</summary>
    /// <param name="data">求值用的数据；为空 = 规则数据。</param>
    public static void AttachPreview(CustomUIBuilder b, IReadOnlyList<string> ids, Func<RulesData> data = null) =>
        DropdownRowInfo.Attach(b.Last?.dropdown, () =>
        {
            var ctx = new EvalCtx(World, data?.Invoke() ?? D);
            var seen = new Dictionary<int, string>();
            return i =>
            {
                if (i < 0 || i >= ids.Count || string.IsNullOrEmpty(ids[i])) return null;
                if (!seen.TryGetValue(i, out var text)) seen[i] = text = PreviewText(ctx, ids[i], TipLines, false);
                return text;
            };
        });

    /// <summary>来源里这些东西的类别（含上级），常用条件按它过滤。</summary>
    public static HashSet<string> TraitsOf(IEnumerable<object> items) =>
        new(items.SelectMany(i => Catalog.Closure(World.Traits(i))));

    private static IEnumerable<string> SourceTagProps(string kind, IReadOnlyList<object> items)
    {
        try
        {
            var gw = World;
            var match = items.Where(i => string.IsNullOrEmpty(kind) || kind == Catalog.Any || kind == Catalog.Self || Catalog.Matches(gw, i, kind)).OfType<GameItem>().Take(12);
            return PropBindings.RawTagProps(match, ShowAllTags);
        }
        catch { return Enumerable.Empty<string>(); }
    }

    /// <summary>品质等级属性的等级名（从好到差）；不是等级属性为空。</summary>
    public static List<string> Grades(string prop) => Quality.Grades(prop);

    /// <summary>条件里列不列内部标签（类别标记、编号、贴图……）；对话框里的开关，默认看配置。</summary>
    public static bool ShowAllTags = Settings.ShowAllTags;

    /// <summary>店里这类物品（最多看 8 件）身上的游戏标签；“全部物品”不列（太多）。</summary>
    private static IEnumerable<string> GameTagProps(string kind)
    {
        if (string.IsNullOrEmpty(kind) || kind == Catalog.Any || kind == Catalog.Self || !GameApi.InStore) return Enumerable.Empty<string>();
        try
        {
            var gw = new GameWorld();
            var items = GameApi.CollectAll().Where(i => Catalog.Matches(gw, i, kind)).Take(8);
            return PropBindings.RawTagProps(items, ShowAllTags);
        }
        catch { return Enumerable.Empty<string>(); }
    }

    public static string PropName(string id) => AutoMod.Domain.Rules.Text.PropName(D, id);

    public static int IndexOf<T>(IList<T> list, Func<T, bool> pred) => Ui.IndexOf(list, pred);
}
