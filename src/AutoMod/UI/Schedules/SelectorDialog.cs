using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>
/// 筛选器对话框：范围 → 物品类型 → 条件 → 选取（或统计数值），名字在最上面。在草稿上编辑，保存时写回。
/// 筛选器之间可以按任意顺序引用，只是不能循环。
/// </summary>
public static class SelectorDialog
{
    private const string Id = "am_seldlg";
    public const string DialogId = Id;
    private static SelectorDef _d, _orig;
    private static bool _nameTouched;

    /// <summary>条件里展开“更多”（条件组、排除）。</summary>
    private static bool _more;

    /// <summary>保存后的回调（在任务里新建时，把新筛选器填进任务）。</summary>
    private static Action<SelectorDef> _onSaved;

    private static RulesData D => RulesUi.D;

    /// <param name="owner">所属排班 Id；为空 = 全局。</param>
    /// <param name="onSaved">保存后调用（在任务里新建时用）。</param>
    public static void New(string owner = null, Action<SelectorDef> onSaved = null)
    {
        _orig = null;
        _d = new SelectorDef { Id = Defs.NewId("s"), From = { Places.Shop }, Kind = Catalog.Any, Where = new CondGroup(), Owner = owner };
        _nameTouched = false;
        _more = false;
        _onSaved = onSaved;
        AutoName();
        Ui.Close(Id); // 新开的对话框从顶部开始；之后的重建保留滚动位置
        Show();
    }

    public static void Edit(SelectorDef s)
    {
        _orig = s;
        _d = Defs.Clone(s);
        _d.Where ??= new CondGroup();
        _nameTouched = true;
        _more = _d.Minus != null || _d.Where.Items.Any(c => c.Group != null);
        _onSaved = null;
        Ui.Close(Id);
        Show();
    }

    private static int MyIndex => _orig == null ? D.Selectors.Count : D.Selectors.IndexOf(_orig);

    private static void AutoName()
    {
        if (_nameTouched) return;
        string from = Domain.L.Join("list", _d.From.Select(f => Places.IsShop(f) ? Domain.L.T("filter.auto_name.shop") : RulesUi.Name(f)));
        string conds = string.Join("", _d.Where.Items.Where(c => c.Preset != null && c.Preset != "alive").Select(c => Catalog.Preset(c.Preset)?.Name ?? ""));
        string kind = _d.Kind == Catalog.Self ? "" : _d.Kind == Catalog.Any ? Domain.L.T("filter.auto_name.items") : Catalog.TypeName(_d.Kind).Replace("任意", "");
        string target = Domain.L.T("filter.auto_name.target", conds, kind).Trim();
        if (string.IsNullOrEmpty(target)) target = Domain.L.T("filter.auto_name.items");

        if (_d.Kind == Catalog.Self)
        {
            _d.Name = Domain.L.T("filter.auto_name.self", from);
        }
        else if (_d.IsNumber)
        {
            var agg = _d.Agg ?? new AggSpec();
            if (agg.Fn == AggFn.Count)
            {
                _d.Name = Domain.L.T("filter.auto_name.agg_count", from, target);
            }
            else
            {
                string fn = Vocab.AggName(agg.Fn).Replace("值", "");
                string prop = RulesUi.PropName(agg.Prop);
                _d.Name = Domain.L.T("filter.auto_name.agg_prop", from, target, fn, prop);
            }
        }
        else
        {
            _d.Name = Domain.L.T("filter.auto_name.items_in", from, target);
        }

        if (string.IsNullOrEmpty(_d.Name)) _d.Name = Domain.L.T("filter.auto_name.default");
    }

    private static void Rebuild()
    {
        AutoName();
        Show();
    }

    /// <summary>
    /// 可选的类型：全部物品、容器本身、物品类别、种类（常见的 + 来源里实际出现的）、
    /// 来源里出现过的商品类型、容器类别。
    /// </summary>
    private static List<(string id, string name)> KindOptions()
    {
        var list = new List<(string id, string name)> { (Catalog.Any, Domain.L.T("kind.any")) };
        list.AddRange(Catalog.Traits.Where(t => !Catalog.IsContainerType(t.Id)).Select(t => (t.Id, t.Name)));
        // 范围里实际有的东西：全店就是店里所有东西，设备名、筛选器是它们里面的东西
        var kinds = new List<string>();
        var types = new List<string>();
        try
        {
            var ctx = new EvalCtx(RulesUi.World, D);
            var items = _d.From.SelectMany(src => Places.IsShop(src)
                ? RulesUi.World.Shop()
                : Eval.Boxes(ctx, src).SelectMany(box => RulesUi.World.Contents(box, null)));
            foreach (var child in items)
            {
                var k = RulesUi.World.Kind(child);
                if (!kinds.Contains(k)) kinds.Add(k);
                foreach (var t in RulesUi.World.Traits(child).Where(t => t.StartsWith(Catalog.ItemTypePrefix) || t.StartsWith(Catalog.FamilyPrefix)))
                    if (!types.Contains(t)) types.Add(t);
            }
        }
        catch { }
        // 游戏的商品类型（药品、食品、材料、武器……）排在具体种类前面，各自按名字排序
        list.AddRange(types.Select(t => (t, (t.StartsWith(Catalog.FamilyPrefix) ? Domain.L.T("selector.prefix_family") : Domain.L.T("selector.prefix_item_type")) + Catalog.TypeName(t))).OrderBy(x => x.Item2));
        list.AddRange(kinds.Select(k => (k, Catalog.TypeName(k))).OrderBy(x => x.Item2));
        list.AddRange(Catalog.Traits.Where(t => Catalog.IsContainerType(t.Id)).Select(t => (t.Id, t.Id == Catalog.Container ? t.Name : Domain.L.T("selector.prefix_container") + t.Name)));
        if (_d.Kind != Catalog.Self && list.All(x => x.id != _d.Kind)) list.Add((_d.Kind, Catalog.TypeName(_d.Kind)));
        return list;
    }

    public static void Show()
    {
        var d = _d;
        RulesUi.View = d.Owner; // 共用的筛选器只能用共用的定义；本流程的能用本流程 + 共用
        var others = RulesUi.VisibleSelectors.Where(x => x.Id != d.Id && (_orig == null || x.Id != _orig.Id)).ToList();
        var otherItems = others.Where(x => !x.IsNumber).ToList();
        var source = RulesUi.SourceItems(d);
        var b = Ui.Window(Id, _orig == null ? Domain.L.T("selector.title_new") : Domain.L.T("selector.title_edit", _orig.Name), 900, 800, keepScroll: true);

        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft)
            .AddLabel(Domain.L.T("ui.name")).AddInput(Domain.L.T("ui.name"), d.Name, Ui.AS(v => { d.Name = v; _nameTouched = true; })).Width(320)
            .AddLabel(Tone.Dim(ObjectDialog.ScopeText(D, d.Owner)));
        if (_orig != null && d.Owner != null) b.AddButton(Domain.L.T("selector.btn_promote"), Ui.A(() => ObjectDialog.Promote(_orig.Id)));
        b.End();

        b.BeginScroll(620);

        // ---- 1 范围 ----
        var places = new List<(string id, string name)> { (Places.Shop, Domain.L.T("scope.shop")) };
        places.AddRange(RulesUi.DeviceNames());
        places.AddRange(otherItems.Select(x => (x.Id, x.Name + Domain.L.T("schedule.selector_suffix"))));
        var modes = ScopeModes(d);
        int mode = d.Kind == Catalog.Self ? 1 : d.Slot == null ? 0 : modes.FindIndex(m => m.slot == d.Slot);
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 4);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("selector.section_scope")))
            .AddFlexibleSpace().AddLabel(Tone.Small(Tone.Dim(source.Count == 0 ? Domain.L.T("selector.scope_empty") : Domain.L.N("selector.scope_count", source.Count, source.Count >= 40 ? "+" : "")))).End();
        for (int k = 0; k < d.From.Count; k++)
        {
            int at = k;
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(k == 0 ? Domain.L.T("selector.scope_from") : Domain.L.T("selector.scope_and")).Width(Domain.L.Int("layout.selector.scope_label_width", 24))
                .AddDropdown(Ui.L(places.Select(x => x.name)), RulesUi.IndexOf(places, x => x.id == d.From[at]), Ui.AI(v => { d.From[at] = places[v].id; Rebuild(); })).Width(260);
            if (k == 0)
                b.AddDropdown(Ui.L(modes.Select(m => m.name)), Math.Max(0, mode), Ui.AI(v =>
                {
                    var m = modes[v];
                    if (m.self) { d.Kind = Catalog.Self; d.Slot = null; }
                    else { if (d.Kind == Catalog.Self) d.Kind = Catalog.Any; d.Slot = m.slot; }
                    Rebuild();
                })).Width(240);
            else b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("selector.scope_same_as_above"))));
            if (d.From.Count > 1) b.AddButton("×", Ui.A(() => { d.From.RemoveAt(at); Rebuild(); }));
            b.End();
        }
        b.AddButton(Domain.L.T("selector.btn_add_scope"), Ui.A(() => { d.From.Add(Places.Shop); Rebuild(); })).Tip(Domain.L.T("selector.tip_add_scope"));
        b.End();

        // ---- 2 物品类型 ----
        var kinds = KindOptions();
        var props = RulesUi.PropsFor(d.Kind == Catalog.Self ? Catalog.Any : d.Kind, source);
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 4);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("selector.section_kind")));
        if (d.Kind == Catalog.Self) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("selector.kind_self_tip"))));
        else
            b.AddDropdown(Ui.L(kinds.Select(x => x.name)), RulesUi.IndexOf(kinds, x => x.id == d.Kind), Ui.AI(k =>
            {
                d.Kind = kinds[k].id;
                var allowed = Catalog.PresetsFor(d.Kind, RulesUi.World).Select(p => p.Id).ToHashSet();
                d.Where.Items.RemoveAll(c => c.Preset != null && !allowed.Contains(c.Preset));
                Rebuild();
            })).Width(260);
        b.End();
        b.End();

        // ---- 3 条件 ----
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 4);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("selector.section_cond")));
        if (d.Where.Items.Count > 1)
            b.AddDropdown(Ui.L(new[] { Domain.L.T("selector.cond_all"), Domain.L.T("selector.cond_any") }), d.Where.IsAny ? 1 : 0, Ui.AI(k => { d.Where.Join = k == 1 ? "any" : "all"; Rebuild(); })).Width(130);
        b.End();
        // 常用条件：只列范围里实际有的那几类东西的（选“本身”时按那几样东西本身的类别）
        var presets = Catalog.PresetsFor(d.Kind == Catalog.Self && source.Count > 0 ? Catalog.Any : d.Kind, RulesUi.World).ToList();
        if ((d.Kind == Catalog.Any || d.Kind == Catalog.Self || string.IsNullOrEmpty(d.Kind)) && source.Count > 0)
        {
            var have = RulesUi.TraitsOf(source);
            presets = presets.Where(p => p.Applies == null || have.Contains(p.Applies) || d.Where.Items.Any(c => c.Preset == p.Id)).ToList();
        }
        if (presets.Count > 0)
        {
            // 选上的常用条件一行一条（× 去掉）；没选的列成按钮，点一下加上
            foreach (var p in presets.Where(p => d.Where.Items.Any(c => c.Preset == p.Id)))
            {
                var pid = p.Id;
                b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(p.Name).AddFlexibleSpace()
                    .AddButton("×", Ui.A(() => { d.Where.Items.RemoveAll(c => c.Preset == pid); Rebuild(); })).End();
            }
        }
        foreach (var c in d.Where.Items.Where(c => c.Preset == null).ToList())
        {
            if (c.Group != null) GroupRows(b, d.Where, c, props, others);
            else CondRow(b, d.Where, c, props, others, "");
        }
        var unused = presets.Where(p => d.Where.Items.All(c => c.Preset != p.Id)).ToList();
        if (unused.Count > 0)
        {
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Small(Tone.Dim(Domain.L.T("selector.common_presets")))).End();
            b.BeginGrid(3, 270, 30);
            foreach (var p in unused)
            {
                var pid = p.Id;
                b.AddButton("＋ " + p.Name, Ui.A(() => { d.Where.Items.Insert(0, Cond.Of(pid)); Rebuild(); }));
            }
            b.End();
        }
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft)
            .AddButton(Domain.L.T("selector.btn_add_cond"), Ui.A(() => { d.Where.Items.Add(Cond.Cmp(props.FirstOrDefault() ?? "weight", ">=", Expr.Of(0))); Rebuild(); }))
            .AddButton(_more ? Domain.L.T("selector.btn_collapse") : Domain.L.T("selector.btn_more"), Ui.A(() => { _more = !_more; Show(); })).Tip(Domain.L.T("selector.tip_more"));
        b.End();
        if (_more)
        {
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft)
                .AddButton(Domain.L.T("selector.btn_add_cond_group"), Ui.A(() => { d.Where.Items.Add(Cond.Sub("any", Cond.Cmp(props.FirstOrDefault() ?? "weight", ">=", Expr.Of(0)))); Rebuild(); }));
            if (!d.IsNumber)
            {
                var minus = new List<(string id, string name)> { (null, Domain.L.T("selector.minus_none")) };
                minus.AddRange(otherItems.Select(x => (x.Id, x.Name)));
                b.AddFlexibleSpace().AddLabel(Domain.L.T("selector.label_minus")).AddDropdown(Ui.L(minus.Select(x => x.name)), RulesUi.IndexOf(minus, x => x.id == d.Minus), Ui.AI(k => { d.Minus = minus[k].id; Rebuild(); })).Width(220)
                    .Tip(Domain.L.T("selector.tip_minus"));
            }
            b.End();
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft)
                .AddButton(RulesUi.ShowAllTags ? Domain.L.T("selector.btn_hide_tags") : Domain.L.T("selector.btn_show_tags"), Ui.A(() => { RulesUi.ShowAllTags = !RulesUi.ShowAllTags; Rebuild(); }))
                .Tip(Domain.L.T("selector.tip_tags"))
                .End();
        }
        b.End();

        // ---- 4 选取 ----
        Pick(b, d, props, others);
        b.End();

        // ---- 底部 ----
        var errs = Check.SelectorErrors(WithDraft(), d);
        foreach (var e in errs) b.AddLabel(Tone.Small(Tone.Color(e, Tone.Bad)));
        var users = _orig == null ? new List<(Schedule, WorkRule)>() : Check.UsedBy(D, _orig.Id);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft);
        if (_orig != null)
        {
            b.AddButton(Domain.L.T("ui.delete"), Ui.A(Delete));
            b.AddButton(Domain.L.T("ui.copy"), Ui.A(() =>
            {
                var c = Defs.Clone(_d);
                c.Id = Defs.NewId("s");
                // 在编辑器里复制：副本属于正在编辑的排班（共用筛选器想只改一处时用它）
                c.Owner = ScheduleEditor.CurrentId ?? _d.Owner;
                string name = Domain.L.T("selector.copy_name", _d.Name);
                for (int i = 2; Scope.NameConflict(D, true, name, c.Owner) != null; i++) name = Domain.L.T("selector.copy_name_seq", _d.Name, i);
                c.Name = name;
                _orig = null;
                _d = c;
                _nameTouched = true;
                Show();
            }));
        }
        b.AddLabel(Tone.Small(Tone.Dim(users.Count > 0 ? Domain.L.T("selector.used_in_rules", Domain.L.Join("list", users.Take(3).Select(u => $"{u.Item1.Name} · {u.Item2.Name}")), users.Count > 3 ? Domain.L.T("selector.more_suffix") : "") : "")))
            .AddFlexibleSpace()
            .AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(Id))).AddButton(Domain.L.T("ui.save"), Ui.A(Save)).End();
        b.Show();
    }

    /// <summary>
    /// 范围的写法：里面的东西 / 本身 / 某个槽位（范围里的机器上实际有的槽位；全店时是各机器的电池槽、滤芯槽）。
    /// </summary>
    private static List<(string name, bool self, string slot)> ScopeModes(SelectorDef d)
    {
        bool shop = d.From.Count > 0 && Places.IsShop(d.From[0]);
        var list = new List<(string name, bool self, string slot)> { (shop ? Domain.L.T("selector.scope_all_inside") : Domain.L.T("selector.scope_items_inside"), false, null), (Domain.L.T("selector.scope_self"), true, null) };
        var slots = new List<(string id, string name)> { ("battery", Domain.L.T("slot.battery")), ("filter", Domain.L.T("slot.filter")) };
        try
        {
            var ctx = new EvalCtx(RulesUi.World, D);
            foreach (var src in d.From)
                foreach (var box in Eval.Boxes(ctx, src).Take(10))
                    foreach (var sl in RulesUi.World.Slots(box))
                        if (slots.All(x => x.id != sl.id)) slots.Add(sl);
        }
        catch { }
        if (d.Slot != null && slots.All(x => x.id != d.Slot)) slots.Add((d.Slot, SlotNames.Name(d.Slot, D, d.From)));
        list.AddRange(slots.Select(x => (Domain.L.T(shop ? "selector.slot_format_shop" : "selector.slot_format", x.name), false, x.id)));
        return list;
    }

    /// <summary>
    /// 选取：全部（可指定排序）/ 前 N 件 / 排除前 N 件（留种）/ 统计数值（数量、合计、平均……，用于执行条件和条件中的数值）。
    /// 对应排序 + 数量：“前 N 件” = 前 N 个；“排除前 N 件” = 第 N + 1 个起。
    /// </summary>
    private static void Pick(CustomUIBuilder b, SelectorDef d, List<string> props, List<SelectorDef> others)
    {
        var sorts = props.Select(p => (id: p, name: RulesUi.PropName(p))).ToList();
        if (d.Sort?.Prop is { } sp && sorts.All(x => x.id != sp)) sorts.Add((sp, RulesUi.PropName(sp)));
        int mode = d.IsNumber ? 3 : d.Take == null ? 0 : d.Take.Mode == TakeMode.From ? 2 : 1;
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 4);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("selector.section_pick")))
            .AddDropdown(Ui.L(new[] { Domain.L.T("selector.pick_all"), Domain.L.T("selector.pick_first_n"), Domain.L.T("selector.pick_exclude_first_n"), Domain.L.T("selector.pick_number") }), mode, Ui.AI(k =>
            {
                d.Mode = k == 3 ? SelectorMode.Number : SelectorMode.Items;
                if (k == 3) { d.Agg ??= new AggSpec { Fn = AggFn.Count }; d.Take = null; }
                else if (k == 0) d.Take = null;
                else
                {
                    d.Sort ??= new SortSpec { Prop = sorts.FirstOrDefault().id ?? "weight", Desc = true };
                    int n = Count(d) ?? 1;
                    d.Take = new TakeSpec { Mode = k == 1 ? TakeMode.First : TakeMode.From, N = Expr.Of(k == 1 ? n : n + 1) };
                }
                Rebuild();
            })).Width(180);
        b.End();
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft);
        if (mode == 3)
        {
            // 统计数值：数量，或某属性的合计、平均、最大、最小
            d.Agg ??= new AggSpec { Fn = AggFn.Count };
            var fns = Vocab.AggFns;
            b.AddLabel(Domain.L.T("selector.label_stat")).AddDropdown(Ui.L(fns.Select(f => f.name)), RulesUi.IndexOf(fns, f => f.id == d.Agg.Fn), Ui.AI(k =>
            {
                d.Agg.Fn = fns[k].id;
                if (d.Agg.Fn != AggFn.Count && string.IsNullOrEmpty(d.Agg.Prop)) d.Agg.Prop = props.FirstOrDefault();
                Rebuild();
            })).Width(120);
            if (d.Agg.Fn != AggFn.Count)
            {
                var popts = props.Select(p => (p, RulesUi.PropName(p))).ToList();
                b.AddLabel(Domain.L.T("selector.label_prop")).AddDropdown(Ui.L(popts.Select(x => x.Item2)), RulesUi.IndexOf(popts, x => x.p == d.Agg.Prop), Ui.AI(k => { d.Agg.Prop = popts[k].p; Rebuild(); })).Width(200);
            }
            b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("selector.stat_tip"))));
        }
        else
        {
            // 排序：不指定时按店内的先后
            var opts = new List<(string id, string name)> { (null, Domain.L.T("selector.sort_unspecified")) };
            opts.AddRange(sorts);
            b.AddLabel(Domain.L.T("selector.label_sort")).AddDropdown(Ui.L(opts.Select(x => x.name)), RulesUi.IndexOf(opts, x => x.id == d.Sort?.Prop), Ui.AI(k =>
            {
                d.Sort = opts[k].id == null ? null : new SortSpec { Prop = opts[k].id, Desc = d.Sort?.Desc ?? true };
                Rebuild();
            })).Width(220).Tip(Domain.L.T("selector.tip_sort"));
            if (d.Sort != null) HighLow(b, d);
            if (mode != 0)
            {
                b.AddLabel(mode == 2 ? Domain.L.T("selector.take_exclude_first") : Domain.L.T("selector.take_first"));
                int? n = Count(d);
                if (n != null)
                    b.AddInput("1", n.Value.ToString(), Ui.AS(v =>
                    {
                        if (!int.TryParse(v, out var x) || x < 1) return;
                        d.Take.N = Expr.Of(d.Take.Mode == TakeMode.From ? x + 1 : x);
                    })).Width(60);
                b.AddLabel(Domain.L.T("selector.items_unit"));
            }
        }
        b.End();
        // N 不是一个数字时（例如用参数、统计数值算出来的），照原样编辑
        if (d.Take != null && Count(d) == null)
        {
            b.AddLabel(Tone.Small(Tone.Dim(d.Take.Mode == TakeMode.From ? Domain.L.T("selector.take_start_from") : Domain.L.T("selector.take_count"))));
            ExprEditor.Build(b, d.Take.N, new ExprEditor.Options { Stats = others.Where(x => x.IsNumber).Select(x => x.Id).ToList() }, Rebuild);
        }
        b.End();
    }

    private static void HighLow(CustomUIBuilder b, SelectorDef d) =>
        b.AddDropdown(Ui.L(new[] { Domain.L.T("selector.sort_desc"), Domain.L.T("selector.sort_asc") }), d.Sort.Desc ? 0 : 1, Ui.AI(k => { d.Sort.Desc = k == 0; Rebuild(); })).Width(120);

    /// <summary>件数是一个数字时返回它（“除了……以外”存的是从第 N + 1 件起）；是表达式时为空。</summary>
    private static int? Count(SelectorDef d)
    {
        if (d.Take?.N?.Terms is not { Count: 1 } t || t[0].Kind != TermKind.Num) return d.Take == null ? 1 : null;
        int n = (int)Math.Round(t[0].Num);
        return d.Take.Mode == TakeMode.From ? Math.Max(1, n - 1) : Math.Max(1, n);
    }

    /// <summary>把草稿放进一份临时数据里（检查和预览用）。</summary>
    private static RulesData WithDraft()
    {
        var list = D.Selectors.ToList();
        if (_orig != null) list[list.IndexOf(_orig)] = _d; else list.Add(_d);
        return new RulesData { Objects = D.Objects, Properties = D.Properties, Selectors = list, Schedules = D.Schedules };
    }

    private static void Toggle(List<string> list, string id, bool on)
    {
        list.Remove(id);
        if (on) list.Add(id);
    }

    private static void CondRow(CustomUIBuilder b, CondGroup parent, Cond c, List<string> props, List<SelectorDef> above, string indent) =>
        CondEditor.Row(b, c, props, above.Where(x => x.IsNumber || !Eval.YieldsBoxes(x)).Select(x => x.Id).ToList(), indent,
            () => _d.Kind, () => KindOptions().Where(x => x.id != Catalog.Any && x.id != Catalog.Self).ToList(), () => parent.Items.Remove(c), Rebuild);

    private static void GroupRows(CustomUIBuilder b, CondGroup parent, Cond g, List<string> props, List<SelectorDef> above)
    {
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Domain.L.T("selector.group_prefix"))
            .AddDropdown(Ui.L(new[] { Domain.L.T("selector.cond_all"), Domain.L.T("selector.cond_any") }), g.Group.IsAny ? 1 : 0, Ui.AI(k => { g.Group.Join = k == 1 ? "any" : "all"; Rebuild(); })).Width(130)
            .AddButton(Domain.L.T("selector.btn_add_cond"), Ui.A(() => { g.Group.Items.Add(Cond.Cmp(props.FirstOrDefault() ?? "weight", ">=", Expr.Of(0))); Rebuild(); }))
            .AddButton(Domain.L.T("selector.btn_del_group"), Ui.A(() => { parent.Items.Remove(g); Rebuild(); }))
            .End();
        foreach (var c in g.Group.Items.ToList())
        {
            if (c.Preset != null)
            {
                var cc = c;
                b.BeginRow(4).AddLabel("    " + (Catalog.Preset(c.Preset)?.Name ?? c.Preset)).AddButton("×", Ui.A(() => { g.Group.Items.Remove(cc); Rebuild(); })).End();
            }
            else if (c.Group == null) CondRow(b, g.Group, c, props, above, "    ");
        }
    }

    private static void Save()
    {
        var d = _d;
        if (string.IsNullOrWhiteSpace(d.Name)) { Toast.Show(Domain.L.T("object_dlg.name_empty")); return; }
        d.Name = d.Name.Trim();
        if (Scope.NameConflict(D, true, d.Name, d.Owner, d.Id) is { } conflict) { Toast.Show(conflict + "。"); return; }
        if (d.From.Count == 0) { Toast.Show(Domain.L.T("selector.save_no_scope")); return; }
        if (!d.IsNumber) d.Agg = null;
        else { d.Sort = null; d.Take = null; d.Minus = null; }
        if (_orig == null) D.Selectors.Add(d);
        else D.Selectors[D.Selectors.IndexOf(_orig)] = d;
        Scope.Normalize(D); // 新建的共用筛选器移到共用组末尾
        Ui.Close(Id);
        var cb = _onSaved;
        _onSaved = null;
        try { cb?.Invoke(d); } catch (Exception ex) { AutoMod.Domain.Log.Warn($"[UI] {ex.Message}"); }
        ScheduleEditor.Refresh();
        SchedulePage.Refresh();
    }

    private static void Delete()
    {
        var refs = Check.Referrers(D, _orig.Id);
        if (refs.Count > 0) { Toast.Show(Domain.L.T("selector.delete_still_used", Domain.L.Join("list", refs.Take(4)))); return; }
        D.Selectors.Remove(_orig);
        Ui.Close(Id);
        ScheduleEditor.Refresh();
        SchedulePage.Refresh();
    }
}
