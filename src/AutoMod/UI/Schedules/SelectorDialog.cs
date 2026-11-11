using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>
/// 筛选器对话框：一个筛选器和它的上下级在同一个窗口里编辑，同一时间展开一个，其余折叠成一行。
/// 展开的一项是 范围 → 物品类型 → 条件 → 选取（或统计数值）→ 当前结果。在草稿上编辑，保存时一起写回。
/// 筛选器之间可以按任意顺序引用，只是不能循环。
/// </summary>
public static class SelectorDialog
{
    private const string Id = "am_seldlg";
    public const string DialogId = Id;
    private const int ResultLines = 8;

    private static SelectorDrafts _s;
    private static string _title;

    /// <summary>下一次重建回到顶部（展开了别的项）。</summary>
    private static bool _toTop;

    /// <summary>保存后的回调（在任务里新建时，把新筛选器填进任务）。</summary>
    private static Action<SelectorDef> _onSaved;

    /// <summary>这次重建用的数据（规则数据 + 草稿）和求值上下文：同一个上级只算一次。</summary>
    private static RulesData _data;
    private static EvalCtx _ctx;

    private static RulesData D => RulesUi.D;

    /// <param name="owner">所属排班 Id；为空 = 全局。</param>
    /// <param name="onSaved">保存后调用（在任务里新建时用）。</param>
    /// <param name="kind">默认的物品类型；为空 = 全部物品。</param>
    public static void New(string owner = null, Action<SelectorDef> onSaved = null, string kind = null)
    {
        var def = new SelectorDef { Id = Defs.NewId("s"), From = { Places.Shop }, Kind = kind ?? Catalog.Any, Where = new CondGroup(), Owner = owner };
        _s = SelectorDrafts.New(D, RulesUi.World, def, ScheduleEditor.CurrentId ?? owner);
        _title = Domain.L.T("selector.title_new");
        _onSaved = onSaved;
        AutoName(_s.Active);
        Ui.Close(Id); // 新开的对话框从顶部开始；之后的重建保留滚动位置
        Show();
    }

    public static void Edit(SelectorDef s)
    {
        _s = SelectorDrafts.Edit(D, RulesUi.World, s, ScheduleEditor.CurrentId ?? s.Owner);
        _title = Domain.L.T("selector.title_edit", s.Name);
        _onSaved = null;
        Ui.Close(Id);
        Show();
    }

    private static void AutoName(SelectorDraft e)
    {
        if (e.NameTouched) return;
        var d = e.Def;
        var data = _s.Overlay();
        string from = Domain.L.Join("list", d.From.Select(f => Places.IsShop(f) ? Domain.L.T("filter.auto_name.shop") : Text.Name(data, f)));
        string conds = string.Join("", d.Where.Items.Where(c => c.Preset != null && c.Preset != "alive").Select(c => Catalog.Preset(c.Preset)?.Name ?? ""));
        string kind = d.Kind == Catalog.Self ? "" : d.Kind == Catalog.Any ? Domain.L.T("filter.auto_name.items") : Catalog.TypeName(d.Kind).Replace("任意", "");
        string target = Domain.L.T("filter.auto_name.target", conds, kind).Trim();
        if (string.IsNullOrEmpty(target)) target = Domain.L.T("filter.auto_name.items");

        if (d.Kind == Catalog.Self)
        {
            d.Name = Domain.L.T("filter.auto_name.self", from);
        }
        else if (d.IsNumber)
        {
            var agg = d.Agg ?? new AggSpec();
            if (agg.Fn == AggFn.Count)
            {
                d.Name = Domain.L.T("filter.auto_name.agg_count", from, target);
            }
            else
            {
                string fn = Vocab.AggName(agg.Fn).Replace("值", "");
                string prop = RulesUi.PropName(agg.Prop);
                d.Name = Domain.L.T("filter.auto_name.agg_prop", from, target, fn, prop);
            }
        }
        else if (d.From.Count > 0 && d.From.All(f => SelectorFamily.IsItemList(data, f, RulesUi.World)))
        {
            d.Name = Domain.L.T("filter.auto_name.sub", from);
        }
        else
        {
            d.Name = Domain.L.T("filter.auto_name.items_in", from, target);
        }

        if (string.IsNullOrEmpty(d.Name)) d.Name = Domain.L.T("filter.auto_name.default");
        if (d.From.Any(f => data.Selectors.Any(x => x.Id == f)))
            d.Name = Defs.UniqueName(data.Selectors.Where(x => x.Id != d.Id).Select(x => x.Name), d.Name);
    }

    private static void Rebuild()
    {
        foreach (var e in _s.Entries) AutoName(e);
        Show();
    }

    private static void Open(SelectorDraft e)
    {
        _s.Expand(e);
        _toTop = true;
        Rebuild();
    }

    /// <summary>
    /// 可选的类型：全部物品、容器本身、物品类别、种类（常见的 + 范围里实际出现的）、
    /// 范围里出现过的商品类型、容器类别。
    /// </summary>
    private static List<(string id, string name)> KindOptions(SelectorDef d)
    {
        var list = new List<(string id, string name)> { (Catalog.Any, Domain.L.T("kind.any")) };
        list.AddRange(Catalog.Traits.Where(t => !Catalog.IsContainerType(t.Id)).Select(t => (t.Id, t.Name)));
        var kinds = new List<string>();
        var types = new List<string>();
        try
        {
            foreach (var child in d.From.SelectMany(src => Eval.Expand(_ctx, d, src)))
            {
                var k = RulesUi.World.Kind(child);
                if (!kinds.Contains(k)) { kinds.Add(k); RulesUi.NoteKindGroup(k, child); }
                foreach (var t in RulesUi.World.Traits(child).Where(t => t.StartsWith(Catalog.ItemTypePrefix) || t.StartsWith(Catalog.FamilyPrefix)))
                    if (!types.Contains(t)) types.Add(t);
            }
        }
        catch { }
        // 游戏的商品类型（药品、食品、材料、武器……）排在具体种类前面，各自按名字排序
        list.AddRange(types.Select(t => (t, (t.StartsWith(Catalog.FamilyPrefix) ? Domain.L.T("selector.prefix_family") : Domain.L.T("selector.prefix_item_type")) + Catalog.TypeName(t))).OrderBy(x => x.Item2));
        list.AddRange(kinds.Select(k => (k, Catalog.TypeName(k))).OrderBy(x => x.Item2));
        list.AddRange(Catalog.Traits.Where(t => Catalog.IsContainerType(t.Id)).Select(t => (t.Id, t.Id == Catalog.Container ? t.Name : Domain.L.T("selector.prefix_container", t.Name))));
        if (d.Kind != Catalog.Self && list.All(x => x.id != d.Kind)) list.Add((d.Kind, Catalog.TypeName(d.Kind)));
        return list;
    }

    public static void Show()
    {
        var s = _s;
        bool single = s.Entries.Count == 1 && s.Active != null;
        _data = s.Overlay();
        _ctx = new EvalCtx(RulesUi.World, _data);
        var b = Ui.Window(Id, _title, 900, 800, keepScroll: !_toTop);
        _toTop = false;

        if (single) NameRow(b, s.Active, false);
        b.BeginScroll(single ? 620 : 664);
        foreach (var e in s.Entries)
        {
            if (e == s.Active) Expanded(b, e, single);
            else Folded(b, e);
        }
        b.End();
        Footer(b, s, single);
        b.Show();
        Ui.ScrollGutter(Id, 16);
    }

    // ---------- 一行：折叠的项 ----------

    private static void Folded(CustomUIBuilder b, SelectorDraft e)
    {
        var errs = Check.SelectorErrors(_data, e.Def);
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 4);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(e.Def.Name)).AddFlexibleSpace()
            .AddLabel(Tone.Small(Tone.Dim(RulesUi.CountText(_ctx, e.Def.Id))))
            .AddButton(Domain.L.T("ui.expand"), Ui.A(() => Open(e))).End();
        b.AddLabel(Tone.Small(Tone.Dim(Text.SelectorShort(_data, e.Def))));
        if (errs.Count > 0) b.AddLabel(Tone.Small(Tone.Color(errs[0], Tone.Bad)));
        b.End();
    }

    // ---------- 名称行 ----------

    private static void NameRow(CustomUIBuilder b, SelectorDraft e, bool inList)
    {
        var d = e.Def;
        bool sub = inList && d.From.Any(f => _s.Entries.Any(x => x.Def.Id == f));
        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
        if (sub) b.AddLabel(Tone.Bold(Domain.L.T("selector.label_sub")));
        b.AddLabel(Domain.L.T("ui.name")).AddInput(Domain.L.T("ui.name"), d.Name, Ui.AS(v => { d.Name = v; e.NameTouched = true; })).Width(inList ? 260 : 320)
            .AddLabel(Tone.Dim(ObjectDialog.ScopeText(D, d.Owner)));
        if (e.Orig != null && d.Owner != null) b.AddButton(Domain.L.T("selector.btn_promote"), Ui.A(() =>
        {
            if (_s.AnyDirty) { Toast.Show(Domain.L.T("selector.promote_unsaved")); return; }
            ObjectDialog.Promote(e.Orig.Id);
        }));
        if (inList) b.AddFlexibleSpace().AddButton(Domain.L.T("ui.collapse"), Ui.A(() => { _s.Collapse(); Rebuild(); }));
        b.End();
    }

    // ---------- 展开的一项 ----------

    private static void Expanded(CustomUIBuilder b, SelectorDraft e, bool single)
    {
        var d = e.Def;
        RulesUi.View = d.Owner; // 共用的筛选器只能用共用的定义；本流程的能用本流程 + 共用
        var others = Scope.Selectors(_data, d.Owner).Where(x => x.Id != d.Id).ToList();
        var otherItems = others.Where(x => !x.IsNumber).ToList();
        var source = RulesUi.SourceItems(d, _data);

        if (!single)
        {
            b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 6, 6);
            NameRow(b, e, true);
        }

        ScopePanel(b, d, otherItems, source);

        // ---- 2 物品类型 ----
        var kinds = KindOptions(d);
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
            })).Searchable(i => RulesUi.KindGroup(kinds[i].id)).Width(260);
        b.End();
        b.End();

        CondPanel(b, e, props, others, otherItems, source);

        // ---- 4 选取 ----
        Pick(b, d, props, others);

        ResultPanel(b, e);

        if (!single) b.End();
    }

    // ---------- 1 范围 ----------

    private static void ScopePanel(CustomUIBuilder b, SelectorDef d, List<SelectorDef> otherItems, List<object> source)
    {
        var places = new List<(string id, string name)> { (Places.Shop, Domain.L.T("scope.shop")) };
        places.AddRange(Fixtures.Boxes);
        places.AddRange(RulesUi.RefParams());
        places.AddRange(RulesUi.DeviceNames());
        places.AddRange(otherItems.Select(x => (x.Id, x.Name + Domain.L.T("schedule.selector_suffix"))));
        bool ItemList(string f) => SelectorFamily.IsItemList(_data, f, RulesUi.World);
        // 范围方式只对容器来源起作用：放在第一个容器来源那一行，物品列表来源显示固定文字
        int modeRow = d.From.FindIndex(f => !ItemList(f));
        var modes = ScopeModes(d, modeRow);
        int mode = d.Kind == Catalog.Self ? 1 : d.Slot == null ? 0 : modes.FindIndex(m => m.slot == d.Slot);
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 4);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("selector.section_scope")))
            .AddFlexibleSpace().AddLabel(Tone.Small(Tone.Dim(source.Count == 0 ? Domain.L.T("selector.scope_empty") : Domain.L.N("selector.scope_count", source.Count, source.Count >= 40 ? "+" : "")))).End();
        for (int k = 0; k < d.From.Count; k++)
        {
            int at = k;
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(k == 0 ? Domain.L.T("selector.scope_from") : Domain.L.T("selector.scope_and")).Width(Domain.L.Int("layout.selector.scope_label_width", 24))
                .AddDropdown(Ui.L(places.Select(x => x.name)), RulesUi.IndexOf(places, x => x.id == d.From[at]), Ui.AI(v =>
                {
                    d.From[at] = places[v].id;
                    if (d.From.All(ItemList)) SelectorDrafts.ClearScopeMode(d);
                    Rebuild();
                })).Searchable().Width(260);
            RulesUi.AttachPreview(b, places.Select(x => x.id).ToList(), () => _s.Overlay());
            if (ItemList(d.From[k]))
                b.AddLabel(Tone.Dim(Domain.L.T("selector.scope_result_items")));
            else if (k == modeRow)
                b.AddDropdown(Ui.L(modes.Select(m => m.name)), Math.Max(0, mode), Ui.AI(v =>
                {
                    var m = modes[v];
                    if (m.self) { d.Kind = Catalog.Self; d.Slot = null; }
                    else { if (d.Kind == Catalog.Self) d.Kind = Catalog.Any; d.Slot = m.slot; }
                    Rebuild();
                })).Searchable().Width(240);
            else b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("selector.scope_same_as_above"))));
            if (d.From.Count > 1) b.AddButton("×", Ui.A(() => { d.From.RemoveAt(at); Rebuild(); }));
            b.End();
        }
        b.AddButton(Domain.L.T("selector.btn_add_scope"), Ui.A(() => { d.From.Add(Places.Shop); Rebuild(); })).Tip(Domain.L.T("selector.tip_add_scope"));
        b.End();
    }

    // ---------- 3 条件 ----------

    private static void CondPanel(CustomUIBuilder b, SelectorDraft e, List<string> props, List<SelectorDef> others, List<SelectorDef> otherItems, List<object> source)
    {
        var d = e.Def;
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
            if (c.Group != null) GroupRows(b, d, d.Where, c, props, others);
            else CondRow(b, d, d.Where, c, props, others, "");
        }
        var unused = presets.Where(p => d.Where.Items.All(c => c.Preset != p.Id)).ToList();
        if (unused.Count > 0)
        {
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Small(Tone.Dim(Domain.L.T("selector.common_presets")))).End();
            b.BeginGrid(3, 250, 30);
            foreach (var p in unused)
            {
                var pid = p.Id;
                b.AddButton("＋ " + p.Name, Ui.A(() => { d.Where.Items.Insert(0, Cond.Of(pid)); Rebuild(); }));
            }
            b.End();
        }
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft)
            .AddButton(Domain.L.T("selector.btn_add_cond"), Ui.A(() => { d.Where.Items.Add(Cond.Cmp(props.FirstOrDefault() ?? "weight", ">=", Expr.Of(0))); Rebuild(); }))
            .AddButton(e.More ? Domain.L.T("selector.btn_collapse") : Domain.L.T("selector.btn_more"), Ui.A(() => { e.More = !e.More; Show(); })).Tip(Domain.L.T("selector.tip_more"));
        b.End();
        if (e.More)
        {
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft)
                .AddButton(Domain.L.T("selector.btn_add_cond_group"), Ui.A(() => { d.Where.Items.Add(Cond.Sub("any", Cond.Cmp(props.FirstOrDefault() ?? "weight", ">=", Expr.Of(0)))); Rebuild(); }));
            if (!d.IsNumber)
            {
                var minus = new List<(string id, string name)> { (null, Domain.L.T("selector.minus_none")) };
                minus.AddRange(otherItems.Select(x => (x.Id, x.Name)));
                b.AddFlexibleSpace().AddLabel(Domain.L.T("selector.label_minus")).AddDropdown(Ui.L(minus.Select(x => x.name)), RulesUi.IndexOf(minus, x => x.id == d.Minus), Ui.AI(k => { d.Minus = minus[k].id; Rebuild(); })).Searchable().Width(220)
                    .Tip(Domain.L.T("selector.tip_minus"));
                RulesUi.AttachPreview(b, minus.Select(x => x.id).ToList(), () => _s.Overlay());
            }
            b.End();
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft)
                .AddButton(RulesUi.ShowAllTags ? Domain.L.T("selector.btn_hide_tags") : Domain.L.T("selector.btn_show_tags"), Ui.A(() => { RulesUi.ShowAllTags = !RulesUi.ShowAllTags; Rebuild(); }))
                .Tip(Domain.L.T("selector.tip_tags"))
                .End();
        }
        b.End();
    }

    // ---------- 当前结果 ----------

    private static void ResultPanel(CustomUIBuilder b, SelectorDraft e)
    {
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 4);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("selector.section_result"))).AddFlexibleSpace();
        if (!e.Def.IsNumber)
            b.AddButton(Domain.L.T("selector.btn_add_sub"), Ui.A(() =>
            {
                _s.AddChild(e, ScheduleEditor.CurrentId);
                _toTop = true;
                Rebuild();
            }));
        b.End();
        b.AddLabel(Tone.Small(RulesUi.PreviewText(_ctx, e.Def.Id, ResultLines) ?? ""));
        var label = b.Last;
        // 数值、名称的输入不重建窗口：内容变了就只更新这段文字
        ChangeWatcher.Attach(label?.root, () => Defs.Json(e.Def), () =>
        {
            var ctx = new EvalCtx(RulesUi.World, _s.Overlay());
            label.SetText(Tone.Small(RulesUi.PreviewText(ctx, e.Def.Id, ResultLines) ?? ""));
        });
        b.End();
    }

    // ---------- 底部 ----------

    private static void Footer(CustomUIBuilder b, SelectorDrafts s, bool single)
    {
        var e = s.Active;
        if (e != null)
            foreach (var err in Check.SelectorErrors(_data, e.Def)) b.AddLabel(Tone.Small(Tone.Color(err, Tone.Bad)));
        var users = e?.Orig == null ? new List<(Schedule, WorkRule)>() : Check.UsedBy(_data, e.Def.Id);
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft);
        if (e != null && (!e.IsNew || !single))
        {
            b.AddButton(Domain.L.T("ui.delete"), Ui.A(() => Delete(e)));
            b.AddButton(Domain.L.T("ui.copy"), Ui.A(() =>
            {
                // 在编辑器里复制：副本属于正在编辑的排班（共用筛选器想只改一处时用它）
                s.Copy(e, ScheduleEditor.CurrentId);
                _toTop = true;
                Show();
            }));
        }
        b.AddLabel(Tone.Small(Tone.Dim(users.Count > 0 ? Domain.L.T("selector.used_in_rules", Domain.L.Join("list", users.Take(3).Select(u => $"{u.Item1.Name} · {u.Item2.Name}")), users.Count > 3 ? Domain.L.T("selector.more_suffix") : "") : "")))
            .AddFlexibleSpace()
            .AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(Id))).AddButton(Domain.L.T("ui.save"), Ui.A(Save)).End();
    }

    /// <summary>
    /// 范围的写法：里面的东西 / 本身 / 某个槽位。来源里有东西时只列实际存在的槽位；全店、来源暂时为空时列电池槽、滤芯槽，
    /// 便于提前配置。已选中的槽位始终保留。
    /// </summary>
    private static List<(string name, bool self, string slot)> ScopeModes(SelectorDef d, int row)
    {
        string src = row >= 0 ? d.From[row] : null;
        bool shop = src != null && Places.IsShop(src);
        var list = new List<(string name, bool self, string slot)> { (shop ? Domain.L.T("selector.scope_all_inside") : Domain.L.T("selector.scope_items_inside"), false, null), (Domain.L.T("selector.scope_self"), true, null) };
        var slots = new List<(string id, string name)>();
        var w = RulesUi.World;
        (string id, string name) battery = ("battery", Domain.L.T("slot.battery")), filter = ("filter", Domain.L.T("slot.filter"));
        try
        {
            var boxes = d.From.Where(f => !SelectorFamily.IsItemList(_data, f, w) && !Places.IsShop(f)).SelectMany(f => Eval.Boxes(_ctx, f)).Take(40).ToList();
            if (d.From.Any(Places.IsShop) || boxes.Count == 0)
            {
                slots.Add(battery);
                slots.Add(filter);
            }
            else
            {
                if (boxes.Any(w.HasBatterySlot)) slots.Add(battery);
                if (boxes.Any(x => Catalog.HasTrait(w, x, "filterHolder") || w.Contents(x, "filter").Count > 0)) slots.Add(filter);
            }
            foreach (var box in boxes)
                foreach (var sl in w.Slots(box))
                    if (slots.All(x => x.id != sl.id)) slots.Add(sl);
            // 全店：机器的格子太多不列，储藏区的标签页列出来（同名的算一个，全店里都按这个标签页取）
            if (shop)
                foreach (var box in w.Shop().Where(x => Catalog.Matches(w, x, Catalog.Container)))
                    foreach (var sl in w.Slots(box).Where(x => SlotNames.IsTab(x.id)))
                        if (slots.All(x => x.id != sl.id)) slots.Add(sl);
        }
        catch { }
        if (d.Slot != null && slots.All(x => x.id != d.Slot)) slots.Add((d.Slot, SlotNames.Name(d.Slot, _data, d.From)));
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
            // 排序：不指定时沿用范围里的先后
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
            ExprEditor.Build(b, d.Take.N, new ExprEditor.Options { Stats = others.Where(x => x.IsNumber).Select(x => x.Id).ToList(), Data = _data }, Rebuild);
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

    private static void CondRow(CustomUIBuilder b, SelectorDef d, CondGroup parent, Cond c, List<string> props, List<SelectorDef> above, string indent) =>
        CondEditor.Row(b, c, props, above.Where(x => x.IsNumber || !Eval.YieldsBoxes(x)).Select(x => x.Id).ToList(), indent,
            () => d.Kind, () => KindOptions(d).Where(x => x.id != Catalog.Any && x.id != Catalog.Self).ToList(), () => parent.Items.Remove(c), Rebuild, _data);

    private static void GroupRows(CustomUIBuilder b, SelectorDef d, CondGroup parent, Cond g, List<string> props, List<SelectorDef> above)
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
            else if (c.Group == null) CondRow(b, d, g.Group, c, props, above, "    ");
        }
    }

    private static void Save()
    {
        if (_s.Commit() is { } why)
        {
            Toast.Show(why);
            _toTop = true;
            Rebuild();
            return;
        }
        var target = _s.TaskTarget;
        Ui.Close(Id);
        var cb = _onSaved;
        _onSaved = null;
        try { if (target != null) cb?.Invoke(target); } catch (Exception ex) { AutoMod.Domain.Log.Warn($"[UI] {ex.Message}"); }
        ScheduleEditor.Refresh();
        SchedulePage.Refresh();
    }

    private static void Delete(SelectorDraft e)
    {
        int at = _s.Entries.IndexOf(e);
        if (_s.Remove(e) is { } why) { Toast.Show(why); return; }
        if (_s.Entries.Count > 0)
        {
            Open(_s.Entries[Math.Max(0, at - 1)]);
            return;
        }
        // 对话框里已经没有别的项：和原来一样立即生效
        _s.Commit();
        Ui.Close(Id);
        ScheduleEditor.Refresh();
        SchedulePage.Refresh();
    }
}
