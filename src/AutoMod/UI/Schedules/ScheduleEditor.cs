using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;
using AutoMod;
using AutoMod.App;
using AutoMod.UI.Guide;
using AutoMod.UI.Intercom;
using AutoMod.UI.Kit;
using AutoMod.UI.Simulator;
using AutoMod.UI.Store;
using AutoMod.Domain;

namespace AutoMod.UI.Schedules;

/// <summary>
/// 排班编辑器：两栏。左栏是设施、参数、筛选器（每项一行，定义在悬停提示里），高级（自定义属性、计数器）收在底部；
/// 右栏是任务列表，选中的任务就地展开前提和动作。规则说明在手册里，界面上不写。
/// </summary>
public static class ScheduleEditor
{
    public const string Id = "am_sched";
    private const float LeftW = 440, RightW = 860;

    private static string _schId, _ruleId;
    private static bool _onlyRefs;

    /// <summary>左栏：“本排班”组展开；“全局”组列出全部（否则只列本排班用到的）；“高级”组展开。</summary>
    private static bool _localOpen = true, _globalAll, _advancedOpen;

    /// <summary>编辑器开着时正在编辑的排班 Id。</summary>
    public static string CurrentId => IsOpen ? _schId : null;

    private static RulesData D => RulesUi.D;
    private static Schedule Sch => D.Schedules.FirstOrDefault(s => s.Id == _schId);
    private static WorkRule Rule => Sch?.Rules.FirstOrDefault(r => r.Id == _ruleId);

    public static bool IsOpen => Ui.IsOpen(Id);

    public static void Open(Schedule s, string ruleId = null)
    {
        _schId = s?.Id ?? _schId;
        var sch = Sch;
        if (sch == null) return;
        _ruleId = ruleId ?? (sch.Rules.Any(r => r.Id == _ruleId) ? _ruleId : sch.Rules.FirstOrDefault()?.Id);
        _onlyRefs = false;
        _localOpen = true;
        _globalAll = false;
        Ui.Close(MainWindow.Id);
        Show();
    }

    public static void Hide()
    {
        Ui.Close(Id);
        ListDrag.Clear("rules");
        ListDrag.Clear("sel_local");
        ListDrag.Clear("sel_global");
    }

    /// <summary>数据变化后重建（保持当前排班和规则）。</summary>
    public static void Refresh()
    {
        RulesUi.Changed();
        if (IsOpen) Show();
    }

    // ================= 窗口 =================

    public static void Show()
    {
        var s = Sch;
        if (s == null) { Hide(); return; }
        var rule = Rule;
        RulesUi.View = s.Id;
        _entry = 0;
        var b = Ui.Window(Id, Domain.L.T("editor.window_title", s.Name), LeftW + RightW + 60, 860, draggable: false, keepScroll: true);

        // ---- 顶栏 ----
        b.BeginRow(8, TextAnchor.MiddleLeft)
            .AddLabel(Domain.L.T("ui.name")).AddInput(Domain.L.T("editor.input_name"), s.Name, Ui.AS(v => { if (!string.IsNullOrWhiteSpace(v)) s.Name = v.Trim(); })).Width(260)
            .AddFlexibleSpace()
            .AddButton(Domain.L.T("schedule.btn_simulator"), Ui.A(() => SimulatorWindow.Open(s)))
            .AddButton(Domain.L.T("editor.btn_return"), Ui.A(() => { Hide(); MainWindow.Show("schedules"); }))
            .AddButton(Domain.L.T("ui.close"), Ui.A(Hide))
            .End();

        var closure = rule == null ? new HashSet<string>() : Check.Closure(D, Check.RuleRefs(rule));
        var usedHere = Check.Closure(D, s.Rules.SelectMany(Check.RuleRefs));
        bool Visible(string id) => !_onlyRefs || rule == null || closure.Contains(id);

        b.BeginRow(12);

        // ================= 左栏 =================
        b.BeginColumn(4);
        b.AddImage(ShapeSprites.White, LeftW, 1, "am_sp_left");
        b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("editor.sec_defs"))).AddFlexibleSpace()
            .AddButton(_onlyRefs ? Domain.L.T("editor.btn_show_all") : Domain.L.T("editor.btn_only_refs"), Ui.A(() => { _onlyRefs = !_onlyRefs; Show(); }))
            .Tip(_onlyRefs ? Domain.L.T("editor.tip_show_all") : Domain.L.T("editor.tip_only_refs"))
            .AddButton("?", Ui.A(() => Handbook.Open(HandbookPages.Definitions)))
            .End();
        b.BeginScroll(720);
        var localSel = new List<string>();
        var globalSel = new List<string>();

        var mineObjs = D.Objects.Where(o => o.Owner == s.Id).ToList();
        // 只用在一个任务里的筛选器在任务里编辑，左栏只列可复用的（被几处用到、或还没用到的）
        var allMine = D.Selectors.Where(x => x.Owner == s.Id).ToList();
        var mineSels = allMine.Where(x => Uses(s, x.Id) != 1).ToList();
        int inTasks = allMine.Count - mineSels.Count;
        b.BeginRow(6, TextAnchor.MiddleLeft).AddButton(Domain.L.T("editor.group_local"), Ui.A(() => { _localOpen = !_localOpen; Show(); })).Tip(_localOpen ? Domain.L.T("ui.collapse") : Domain.L.T("ui.expand"))
            .AddLabel(Tone.Dim($"{mineObjs.Count + allMine.Count}")).AddFlexibleSpace();
        GroupButtons(b, s.Id);
        b.End();
        if (_localOpen)
        {
            DefRows(b, mineObjs.Where(o => Visible(o.Id)), mineSels.Where(x => Visible(x.Id)), closure, usedHere, localSel, "am_sell_");
            if (mineObjs.Count + mineSels.Count == 0) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("editor.empty_none"))));
            if (inTasks > 0) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.N("editor.local_in_tasks", inTasks))));
        }

        var globObjs = D.Objects.Where(o => o.Owner == null).ToList();
        var globSels = D.Selectors.Where(x => x.Owner == null).ToList();
        int used = globObjs.Count(o => usedHere.Contains(o.Id)) + globSels.Count(x => usedHere.Contains(x.Id));
        b.AddSpace(6);
        b.BeginRow(6, TextAnchor.MiddleLeft).AddButton(Domain.L.T("editor.group_global"), Ui.A(() => { _globalAll = !_globalAll; Show(); }))
            .Tip(_globalAll ? Domain.L.T("editor.tip_collapse_global") : Domain.L.T("editor.tip_expand_global"))
            .AddLabel(Tone.Dim(Domain.L.N("editor.global_used", used, globObjs.Count + globSels.Count))).AddFlexibleSpace();
        GroupButtons(b, null);
        b.End();
        bool Shown(string id) => Visible(id) && (_globalAll || usedHere.Contains(id) || closure.Contains(id));
        DefRows(b, globObjs.Where(o => Shown(o.Id)), globSels.Where(x => Shown(x.Id)), closure, usedHere, globalSel, "am_selg_");

        // 设备名：这个排班用到的（店里有几个叫这个名字的东西；还没有的点开去店里起名）
        var names = AutoMod.Domain.Rules.Card.DeviceNames(D, s);
        b.AddSpace(6);
        b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.sec_devices")).AddFlexibleSpace()
            .AddButton(Domain.L.T("editor.btn_name_device"), Ui.A(() => { Hide(); DeviceNameDialog.Pick(null, Show); })).Tip(Domain.L.T("editor.tip_name_device")).End();
        if (names.Count == 0) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("editor.no_devices"))));
        {
            foreach (var n in names)
            {
                string name = n;
                var items = Session.World.Named(name).Cast<GameItem>().ToList();
                Entry(b, RulesUi.DeviceLabel(name),
                    items.Count > 0 ? Domain.L.Join("list", items.Select(i => Session.World.KindName(i))) : Domain.L.T("editor.device_none_named"),
                    null, closure.Contains(Places.Ref(name)), true, 0, () => DeviceMenu(s, name), null,
                    items.Count > 0 ? Domain.L.N("schedule.count_devices", items.Count) : Tone.Color(Domain.L.T("editor.device_none_short"), Tone.Warn));
            }
        }

        b.AddSpace(6);
        var noteRefs = rule == null ? new HashSet<string>() : NoteRefs(rule);
        b.BeginRow(6, TextAnchor.MiddleLeft).AddButton(Domain.L.T("editor.sec_advanced"), Ui.A(() => { _advancedOpen = !_advancedOpen; Show(); })).Tip(_advancedOpen ? Domain.L.T("ui.collapse") : Domain.L.T("ui.expand"))
            .AddLabel(Tone.Dim(Domain.L.T("editor.advanced_stats", D.Properties.Count, s.Notepad.Count))).End();
        if (_advancedOpen)
        {
            b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.label_custom_props")).AddFlexibleSpace().AddButton("＋", Ui.A(() => PropertyDialog.New())).End();
            foreach (var p in D.Properties.Where(p => Visible(p.Id)))
            {
                var prop = p;
                Entry(b, prop.Name, AutoMod.Domain.Rules.Text.Property(D, prop), null, closure.Contains(prop.Id), usedHere.Contains(prop.Id), Check.UsedBy(D, prop.Id).Count, () => PropertyDialog.Edit(prop), null);
            }
            b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.label_counters")).AddFlexibleSpace().AddButton("＋", Ui.A(() => NoteDialog(null))).End();
            foreach (var n in s.Notepad)
            {
                var note = n;
                Entry(b, note.Name, Domain.L.T("editor.note_desc", note.Init.ToString("0.##")), null, noteRefs.Contains(note.Id), true, 0, () => NoteDialog(note), null);
            }
        }
        b.End();
        b.End();

        // ================= 右栏：任务 =================
        b.BeginColumn(4);
        b.AddImage(ShapeSprites.White, RightW, 1, "am_sp_right");
        b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("editor.sec_rules"))).AddFlexibleSpace()
            .AddButton(Domain.L.T("editor.btn_new_rule"), Ui.A(() =>
            {
                var r = new WorkRule { Id = Defs.NewId("r"), Name = SchedulePage.Unique(s.Rules.Select(x => x.Name), Domain.L.T("editor.default_rule_name", s.Rules.Count + 1)) };
                s.Rules.Add(r);
                _ruleId = r.Id;
                Refresh();
            }))
            .AddButton("?", Ui.A(() => Handbook.Open(HandbookPages.Schedules)))
            .End();
        b.BeginScroll(720);
        for (int i = 0; i < s.Rules.Count; i++)
        {
            // 两行：第一行名字（拖动排序）、上一次的结果（停用的写“已停用”）；第二行灰色的整句话。展开后就地编辑。
            var r = s.Rules[i];
            bool sel = r.Id == _ruleId;
            var errs = r.Enabled ? RulesUi.RuleErrors(s, r) : new List<string>();
            Table.BeginItem(b, i, sel);
            b.BeginRow(8, TextAnchor.MiddleLeft)
                .AddButton($"{i + 1}. {r.Name}", Ui.A(() => { _ruleId = sel ? null : r.Id; Show(); }), "am_rule_" + i)
                .Tip(Domain.L.T("editor.tip_rule_reorder"));
            if (errs.Count > 0) b.AddLabel(Tone.Color("<b>!</b>", Tone.Bad)).Tip(Domain.L.T("editor.tip_cannot_run") + "\n" + string.Join("\n", errs));
            else if (r.Enabled && Session.RuleWarnings(r) is { Count: > 0 } warns) b.AddLabel(Tone.Color("<b>!</b>", Tone.Warn)).Tip(Domain.L.T("editor.tip_warn") + "\n" + string.Join("\n", warns));
            b.AddFlexibleSpace();
            var (last, tone) = r.Enabled ? LastResult(s, r) : (Domain.L.T("schedule.rule_disabled"), Tone.Muted);
            if (last != null) Table.Cell(b, last, 300, t => Tone.Color(t, tone)).Tip(Domain.L.T("editor.tip_last_result"));
            b.End();
            if (!sel)
            {
                b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel("").Width(36);
                Table.Cell(b, AutoMod.Domain.Rules.Text.Rule(D, s, r), RightW - 90, Tone.Dim);
                b.End();
            }
            else RuleInspector(b, s, r);
            Table.EndItem(b);
        }
        if (s.Rules.Count == 0) b.AddLabel(Tone.Dim(Domain.L.T("editor.no_rules")));
        b.End();
        b.End();

        b.End();
        var win = b.Show();
        AfterShow(win, s, localSel, globalSel);
    }

    /// <summary>筛选器在这个流程里被直接用到几处：任务（对象、执行条件……）和别的筛选器。</summary>
    private static int Uses(Schedule s, string id) =>
        s.Rules.Count(r => Check.RuleRefs(r).Contains(id)) + D.Selectors.Count(x => x.Id != id && Check.SelectorRefs(x).Contains(id));

    /// <summary>
    /// 任务上一次执行的结果：最近一次真实执行的报告（读档后没有时，用保存着的班次报告）。
    /// “放入 6 件”“「作物输出」当前为空”“目标已满……”。
    /// </summary>
    private static (string text, string tone) LastResult(Schedule s, WorkRule r)
    {
        var rr = Automation.LastRun?.Groups.FirstOrDefault(g => g.ScheduleId == s.Id)?.Rules.FirstOrDefault(x => x.RuleId == r.Id);
        if (rr != null)
        {
            if (rr.Issues.Count > 0) return (rr.Issues[0].Text, Tone.Warn);
            if (rr.Done.Count > 0) return (Domain.L.Join("list", rr.Done.Select(kv => $"{Catalog.Op(kv.Key).Name} " + Domain.L.N("schedule.count_items", kv.Value))), Tone.Ok);
            if (rr.Deferred > 0) return (Domain.L.N("editor.deferred_ap", rr.Deferred), Tone.Warn);
            if (rr.Pre == PreResult.Fail) return (Domain.L.T("editor.pre_fail", rr.Why), Tone.Muted);
            if (rr.Idle != null) return (rr.Idle, Tone.Muted);
            return (rr.Pre, Tone.Muted);
        }
        var rep = Automation.State.Reports.AsEnumerable().Reverse().FirstOrDefault(x => !x.DryRun && x.Groups.Any(g => g.Schedule == s.Name));
        if (rep == null) return (null, null);
        var mine = rep.Entries.Where(e => e.Ws == s.Name && e.Rule == r.Name).ToList();
        if (mine.Any(e => !e.Ok)) return (mine.First(e => !e.Ok).Reason, Tone.Warn);
        return mine.Count > 0 ? (Domain.L.N("editor.done_items", mine.Count), Tone.Ok) : (Domain.L.T("editor.unprocessed"), Tone.Muted);
    }

    /// <summary>
    /// 选一组东西（任务的对象、执行条件）：下拉框选已有的设备名、筛选器；旁边“编辑”改当前的筛选器，
    /// “＋ 新建”在任务里直接写一个新的（属于这个流程，只用在这里时不出现在左栏）。
    /// </summary>
    private static void ListPicker(CustomUIBuilder b, Schedule s, string current, Action<string> set, float width = 300)
    {
        var opts = WithNone(RulesUi.ItemSelectors, Domain.L.T("editor.picker_placeholder"));
        b.AddDropdown(Ui.L(opts.Select(o => o.name)), RulesUi.IndexOf(opts, o => o.id == current), Ui.AI(k => { set(opts[k].id); Refresh(); })).Width(width);
        if (D.Selectors.FirstOrDefault(x => x.Id == current) is { } cur)
            b.AddButton(Domain.L.T("editor.btn_edit"), Ui.A(() => SelectorDialog.Edit(cur))).Tip(AutoMod.Domain.Rules.Text.Selector(D, cur));
        b.AddButton(Domain.L.T("editor.btn_new"), Ui.A(() => SelectorDialog.New(s.Id, sel => { set(sel.Id); Refresh(); }))).Tip(Domain.L.T("editor.tip_new_filter"));
    }

    private static HashSet<string> NoteRefs(WorkRule r) =>
        r.Pre.Where(p => p.RefKind == PreRef.Note).Select(p => p.Ref)
            .Concat(r.Pre.SelectMany(p => p.Right?.Terms ?? new List<Term>()).Where(t => t.Kind == TermKind.Note).Select(t => t.Ref))
            .Concat(r.Actions.Select(a => a.Note))
            .Concat(r.Actions.SelectMany(a => a.Value?.Terms ?? new List<Term>()).Where(t => t.Kind == TermKind.Note).Select(t => t.Ref))
            .Where(x => x != null).ToHashSet();

    /// <summary>组标题后面的“＋”：在这个范围里新建（owner 为空 = 全局）。</summary>
    private static void GroupButtons(CustomUIBuilder b, string owner)
    {
        b.AddButton(Domain.L.T("editor.btn_add_param"), Ui.A(() => ObjectDialog.New(owner)))
            .AddButton(Domain.L.T("editor.btn_add_filter"), Ui.A(() => SelectorDialog.New(owner)));
    }

    /// <summary>一组定义：参数在前，筛选器在后（筛选器的行可拖动，行 id = prefix + 序号）。</summary>
    private static void DefRows(CustomUIBuilder b, IEnumerable<ObjectDef> objs, IEnumerable<SelectorDef> sels, HashSet<string> closure, HashSet<string> usedHere, List<string> selIds, string prefix)
    {
        foreach (var o in objs)
        {
            var obj = o;
            string def = AutoMod.Domain.Rules.Text.Object(obj) + (string.IsNullOrEmpty(obj.Hint) ? "" : "\n" + obj.Hint);
            Entry(b, obj.Name, def, null, closure.Contains(obj.Id), usedHere.Contains(obj.Id), Check.UsedBy(D, obj.Id).Count,
                () => ObjectDialog.Edit(obj), null, $"{obj.Value:0.##}{obj.Unit}");
        }
        foreach (var x in sels)
        {
            var sel = x;
            var errs = Check.SelectorErrors(D, sel);
            Entry(b, sel.Name, AutoMod.Domain.Rules.Text.Selector(D, sel), errs.FirstOrDefault(), closure.Contains(sel.Id), usedHere.Contains(sel.Id), Check.UsedBy(D, sel.Id).Count,
                () => SelectorDialog.Edit(sel), prefix + selIds.Count);
            selIds.Add(sel.Id);
        }
    }

    /// <summary>
    /// <summary>点设备名：去店里再起一个叫这个名字的，或者展开成筛选器（要排序、取几个、多个条件时）。</summary>
    private static void DeviceMenu(Schedule s, string name)
    {
        Pickers.Choose(RulesUi.DeviceLabel(name), new (string, Action)[]
        {
            (Domain.L.T("editor.menu_device_reuse"), () => { Hide(); DeviceNameDialog.Pick(name, Show); }),
            (Domain.L.T("editor.menu_device_to_filter"), () =>
            {
                var sel = ExampleSetup.ExpandName(s, name);
                Refresh();
                SelectorDialog.Edit(sel);
            }),
        }, Domain.L.T("editor.tip_device_to_filter", name));
    }

    /// <summary>左栏条目的序号（底色交替用），每次重建窗口时清零。</summary>
    private static int _entry;

    /// <summary>
    /// 左栏一项，两行：第一行名字按钮、右边一点说明；第二行灰色的定义（太长截断，悬停看全文）。
    /// 当前任务用到的高亮；这个流程没用到的名字变灰；有错误的标红 !。
    /// </summary>
    private static void Entry(CustomUIBuilder b, string name, string def, string err, bool highlight, bool usedHere, int refs, Action edit, string rowId, string right = null)
    {
        Table.BeginItem(b, _entry++, highlight);
        b.BeginRow(6, TextAnchor.MiddleLeft);
        string label = usedHere || highlight ? name : Tone.Dim(name);
        b.AddButton(label, Ui.A(edit), rowId).Tip(def + (refs > 0 ? Domain.L.N("editor.entry_refs", refs) : Domain.L.T("editor.entry_no_refs")));
        if (err != null) b.AddLabel(Tone.Color("<b>!</b>", Tone.Bad)).Tip(err);
        b.AddFlexibleSpace();
        if (right != null) b.AddLabel(Tone.Small(Tone.Dim(right)));
        b.End();
        string first = (def ?? "").Split('\n')[0];
        if (first != "")
        {
            b.BeginRow(6, TextAnchor.MiddleLeft);
            Table.Cell(b, first, LeftW - 90, Tone.Dim, tip: def != first ? def : null);
            b.End();
        }
        Table.EndItem(b);
    }

    private static void AfterShow(CustomUIWindow win, Schedule s, List<string> localSel, List<string> globalSel)
    {
        try
        {
            foreach (var sp in new[] { "am_sp_left", "am_sp_right" })
                if (win.Get(sp)?.image is { } img) img.color = new Color(0, 0, 0, 0);
            var ruleRows = new List<RectTransform>();
            for (int i = 0; i < s.Rules.Count; i++)
                if (win.Get("am_rule_" + i)?.root is { } go) ruleRows.Add(go.GetComponent<RectTransform>());
            ListDrag.Register("rules", ruleRows, (from, to) =>
            {
                var r = s.Rules[from];
                s.Rules.RemoveAt(from);
                s.Rules.Insert(Math.Min(to, s.Rules.Count), r);
                Refresh();
            });
            // 挑选器只在组内拖动（全局组整体在本排班组上方）
            foreach (var (key, prefix, ids) in new[] { ("sel_local", "am_sell_", localSel), ("sel_global", "am_selg_", globalSel) })
            {
                var rows = new List<RectTransform>();
                for (int i = 0; i < ids.Count; i++)
                    if (win.Get(prefix + i)?.root is { } go) rows.Add(go.GetComponent<RectTransform>());
                ListDrag.Register(key, rows, (from, to) => MoveSelector(ids[from], ids[to]));
            }
        }
        catch (Exception ex) { Log.Warn($"[ScheduleEditor] {ex.Message}"); }
    }

    /// <summary>挑选器拖动：会造成反向引用的位置不允许放。</summary>
    private static void MoveSelector(string id, string targetId)
    {
        var list = D.Selectors.ToList();
        var x = list.First(s => s.Id == id);
        int to = list.FindIndex(s => s.Id == targetId);
        list.Remove(x);
        list.Insert(Math.Min(to, list.Count), x);
        if (Check.FirstBackRef(D, list) is { } back)
        {
            Show(); // 先重建编辑器，提示窗口才会在最上层
            Toast.Show(Domain.L.T("editor.selector_backref", back.a.Name, back.b.Name));
            return;
        }
        D.Selectors.Clear();
        D.Selectors.AddRange(list);
        Refresh();
    }

    // ================= 记事本 =================

    private static void NoteDialog(NoteDef note)
    {
        var s = Sch;
        string name = note?.Name ?? Domain.L.T("editor.note_default_name");
        string init = (note?.Init ?? 0).ToString("0.##");
        var b = Ui.Window("am_note", note == null ? Domain.L.T("editor.note_title_new") : Domain.L.T("editor.note_title"), 380, 190)
            .BeginRow(6).AddLabel(Domain.L.T("ui.name")).Width(60).AddInput(Domain.L.T("ui.name"), name, Ui.AS(v => name = v)).End()
            .BeginRow(6).AddLabel(Domain.L.T("editor.note_init_val")).Width(60).AddInput("0", init, Ui.AS(v => init = v)).End()
            .AddFlexibleSpace()
            .BeginRow(6);
        if (note != null)
            b.AddButton(Domain.L.T("ui.delete"), Ui.A(() =>
            {
                bool used = s.Rules.Any(r => NoteRefs(r).Contains(note.Id));
                if (used) { Toast.Show(Domain.L.T("editor.note_still_used")); return; }
                s.Notepad.Remove(note);
                Ui.Close("am_note");
                Refresh();
            }));
        b.AddFlexibleSpace().AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close("am_note")))
            .AddButton(Domain.L.T("ui.save"), Ui.A(() =>
            {
                if (string.IsNullOrWhiteSpace(name)) { Toast.Show(Domain.L.T("object_dlg.name_empty")); return; }
                double.TryParse(init, out var v);
                if (note == null) s.Notepad.Add(new NoteDef { Id = Defs.NewId("m"), Name = name.Trim(), Init = v });
                else { note.Name = name.Trim(); note.Init = v; }
                Ui.Close("am_note");
                Refresh();
            }))
            .End().Show();
    }

    // ================= 右栏 =================

    private static void RuleInspector(CustomUIBuilder b, Schedule s, WorkRule r)
    {
        int idx = s.Rules.IndexOf(r);
        b.BeginRow(6, TextAnchor.MiddleLeft)
            .AddLabel(Domain.L.T("ui.name")).Width(65).AddInput(Domain.L.T("editor.input_rule_name"), r.Name, Ui.AS(v => { if (!string.IsNullOrWhiteSpace(v)) r.Name = v.Trim(); })).Width(260)
            .AddFlexibleSpace()
            .AddButton(r.Enabled ? Domain.L.T("editor.btn_disable") : Domain.L.T("editor.btn_enable"), Ui.A(() => { r.Enabled = !r.Enabled; Refresh(); }))
                .Tip(r.Enabled ? Domain.L.T("editor.tip_disable") : Domain.L.T("editor.tip_enable"))
            .AddButton(Domain.L.T("ui.move_up"), Ui.A(() => { if (idx > 0) { s.Rules.RemoveAt(idx); s.Rules.Insert(idx - 1, r); Refresh(); } }))
            .AddButton(Domain.L.T("ui.move_down"), Ui.A(() => { if (idx < s.Rules.Count - 1) { s.Rules.RemoveAt(idx); s.Rules.Insert(idx + 1, r); Refresh(); } }))
            .AddButton(Domain.L.T("ui.copy"), Ui.A(() =>
            {
                var c = Defs.Clone(r);
                c.Id = Defs.NewId("r");
                c.Name = SchedulePage.Unique(s.Rules.Select(x => x.Name), Domain.L.T("fmt.copy_suffix", r.Name));
                s.Rules.Insert(idx + 1, c);
                _ruleId = c.Id;
                Refresh();
            }))
            .AddButton(Domain.L.T("ui.delete"), Ui.A(() => Pickers.Confirm(Domain.L.T("editor.confirm_delete_rule", r.Name), Array.Empty<string>(),
                (Domain.L.T("ui.delete"), () => { s.Rules.Remove(r); _ruleId = s.Rules.ElementAtOrDefault(Math.Max(0, idx - 1))?.Id; Refresh(); }), (Domain.L.T("ui.cancel"), () => { }))))
            .End();
        var errs = RulesUi.RuleErrors(s, r);
        foreach (var e in errs) b.AddLabel(Tone.Small(Tone.Color(e, Tone.Bad)));
        foreach (var w in Session.RuleWarnings(r)) b.AddLabel(Tone.Small(Tone.Color(Domain.L.T("editor.warn_prefix", w), Tone.Warn)));

        // ---- 执行条件 ----
        When(b, s, r);
        // ---- 动作 ----
        b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("editor.sec_actions"))).AddFlexibleSpace()
            .AddButton(Domain.L.T("editor.btn_add_action"), Ui.A(() => { r.Actions.Add(new ActionDef { Verb = ActVerb.Move }); Refresh(); }))
            .AddButton("?", Ui.A(() => Handbook.Open(HandbookPages.Ap)))
            .End();
        if (r.Actions.Count == 0) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("editor.no_actions"))));
        for (int i = 0; i < r.Actions.Count; i++) ActionRows(b, s, r, i);
    }

    /// <summary>执行条件用高级写法（几个条件、计数器、系统变量）的任务。</summary>
    private static readonly HashSet<string> _advancedWhen = new();

    /// <summary>执行条件是不是一句简单的话：没有，或一个“当〔一组东西〕有 / 没有 / 少于 / 多于 N 件时”。</summary>
    private static bool SimpleWhen(WorkRule r) =>
        r.Pre.Count == 0 || r.Pre.Count == 1 && r.Pre[0].RefKind == PreRef.Selector && ListTests.Contains(r.Pre[0].Test)
            && (Defs.Selector(D, r.Pre[0].Ref) == null || Defs.Selector(D, r.Pre[0].Ref) is { IsNumber: false })
            && (r.Pre[0].Test is PreTest.NotEmpty or PreTest.Empty || r.Pre[0].Right?.Terms is { Count: 1 } t && t[0].Kind == TermKind.Num);

    /// <summary>
    /// 执行条件：每班执行 / 〔……〕不为空时 / 为空时 / 少于 N 件时 / 多于 N 件时。
    /// 仅决定是否执行此任务；只处理部分物品时在动作对象的筛选器里加条件。几个条件、计数器在“高级条件…”里。
    /// </summary>
    private static void When(CustomUIBuilder b, Schedule s, WorkRule r)
    {
        b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("editor.sec_when")))
            .Tip(Domain.L.T("editor.tip_when"));
        bool advanced = _advancedWhen.Contains(r.Id) || !SimpleWhen(r);
        if (advanced)
        {
            if (r.Pre.Count > 1)
                b.AddDropdown(Ui.L(new[] { Domain.L.T("schedule.cond_all"), Domain.L.T("schedule.cond_any") }), r.Join == "any" ? 1 : 0, Ui.AI(i => { r.Join = i == 1 ? "any" : "all"; Refresh(); })).Width(140);
            b.AddFlexibleSpace().AddButton(Domain.L.T("editor.btn_add_cond"), Ui.A(() =>
            {
                r.Pre.Add(new PreCond { RefKind = PreRef.Selector, Ref = null, Test = PreTest.NotEmpty, Right = Expr.Of(0) });
                Refresh();
            }));
            if (SimpleWhen(r)) b.AddButton(Domain.L.T("editor.btn_simple_when"), Ui.A(() => { _advancedWhen.Remove(r.Id); Refresh(); }));
            b.End();
            if (r.Pre.Count == 0) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("schedule.cond_always"))));
            for (int i = 0; i < r.Pre.Count; i++) PreRow(b, s, r, i);
            return;
        }
        var modes = new (string name, string test)[] { (Domain.L.T("schedule.cond_always"), null), (Domain.L.T("schedule.cond_not_empty"), PreTest.NotEmpty), (Domain.L.T("schedule.cond_empty"), PreTest.Empty),
            (Domain.L.T("editor.when_lt_n"), PreTest.CountLt), (Domain.L.T("editor.when_gt_n"), PreTest.CountGt) };
        var p = r.Pre.FirstOrDefault();
        int cur = p == null ? 0 : Array.FindIndex(modes, m => m.test == p.Test);
        b.AddDropdown(Ui.L(modes.Select(m => m.name)), Math.Max(0, cur), Ui.AI(k =>
        {
            if (modes[k].test == null) { r.Pre.Clear(); Refresh(); return; }
            var keep = r.Pre.FirstOrDefault();
            r.Pre.Clear();
            r.Pre.Add(new PreCond { RefKind = PreRef.Selector, Ref = keep?.Ref, Test = modes[k].test, Where = keep?.Where,
                Right = modes[k].test is PreTest.CountLt or PreTest.CountGt ? Expr.Of(keep?.Right?.Terms.FirstOrDefault()?.Num ?? 1) : Expr.Of(0) });
            Refresh();
        })).Width(Domain.L.Int("layout.editor.when_dropdown_width", 180));
        b.AddFlexibleSpace().AddButton(Domain.L.T("schedule.cond_advanced"), Ui.A(() => { _advancedWhen.Add(r.Id); Refresh(); })).Tip(Domain.L.T("editor.tip_advanced_when")).End();
        if (p == null) return;
        b.BeginRow(6, TextAnchor.MiddleLeft).AddLabel("    ");
        ListPicker(b, s, p.Ref, v => p.Ref = v);
        if (p.Test is PreTest.CountLt or PreTest.CountGt)
        {
            double n = p.Right?.Terms.FirstOrDefault()?.Num ?? 1;
            b.AddInput("1", n.ToString("0.##"), Ui.AS(v => { if (double.TryParse(v, out var x)) p.Right = Expr.Of(x); })).Width(60).AddLabel(Domain.L.T("selector.items_unit"));
        }
        if (p.Where == null) b.AddButton(Domain.L.T("editor.btn_add_cond"), Ui.A(() => AddPreCond(p))).Tip(Domain.L.T("editor.tip_add_pre_cond"));
        b.End();
        if (p.Where != null)
        {
            if (p.Where.Preset != null)
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.preset_satisfied", Catalog.Preset(p.Where.Preset)?.Name ?? p.Where.Preset))
                    .AddButton("×", Ui.A(() => { p.Where = null; Refresh(); })).End();
            else
                CondEditor.Row(b, p.Where, PreProps(p), RulesUi.VisibleSelectors.Where(x => x.IsNumber || !Eval.YieldsBoxes(x)).Select(x => x.Id).ToList(), Domain.L.T("editor.satisfy_prefix"),
                    () => Catalog.Any, KindChoices, () => p.Where = null, Refresh);
        }
        b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("editor.tip_when_sub"))));
    }

    private static readonly string[] ListTests = { PreTest.NotEmpty, PreTest.Empty, PreTest.CountGt, PreTest.CountLt };
    private static string[] ListTestNames => new[] { Domain.L.T("editor.test_not_empty"), Domain.L.T("editor.test_empty"), Domain.L.T("editor.test_count_gt"), Domain.L.T("editor.test_count_lt") };
    private static readonly string[] NumTests = { ">", ">=", "<", "<=", "=" };
    private static readonly string[] NumTestNames = { ">", "≥", "<", "≤", "=" };

    private static void PreRow(CustomUIBuilder b, Schedule s, WorkRule r, int i)
    {
        var p = r.Pre[i];
        var refs = new List<(string kind, string id, string name)>();
        refs.AddRange(RulesUi.DeviceNames().Select(x => (PreRef.Selector, x.id, x.name)));
        refs.AddRange(RulesUi.VisibleSelectors.Select(x => (PreRef.Selector, x.Id, x.Name + (x.IsNumber ? Domain.L.T("editor.num_tag") : ""))));
        refs.AddRange(s.Notepad.Select(n => (PreRef.Note, n.Id, Domain.L.T("editor.counter_tag", n.Name))));
        refs.AddRange(Vocab.SysVars.Select(v => (PreRef.Sys, v.id, v.name)));
        refs.Insert(0, (PreRef.Selector, null, Domain.L.T("editor.picker_placeholder")));
        bool isList = p.RefKind == PreRef.Selector && Defs.Selector(D, p.Ref) is { IsNumber: false };
        var tests = isList ? ListTests : NumTests;
        var names = isList ? ListTestNames : NumTestNames;
        b.BeginRow(4, TextAnchor.MiddleLeft)
            .AddDropdown(Ui.L(refs.Select(x => x.name)), RulesUi.IndexOf(refs, x => x.kind == p.RefKind && x.id == p.Ref), Ui.AI(k =>
            {
                p.RefKind = refs[k].kind;
                p.Ref = refs[k].id;
                bool list = p.RefKind == PreRef.Selector && Defs.Selector(D, p.Ref) is { IsNumber: false };
                if (!list) p.Where = null;
                if (list && !ListTests.Contains(p.Test)) p.Test = PreTest.NotEmpty;
                if (!list && !NumTests.Contains(p.Test)) p.Test = ">=";
                Refresh();
            })).Width(240)
            .AddDropdown(Ui.L(names), Math.Max(0, Array.IndexOf(tests, p.Test)), Ui.AI(k => { p.Test = tests[k]; Refresh(); })).Width(140);
        // 比较的值接在同一行；“＋ 条件”和删除按钮在这一行最后
        void Tail(CustomUIBuilder row)
        {
            if (isList && p.Where == null) row.AddButton(Domain.L.T("editor.btn_add_cond"), Ui.A(() => AddPreCond(p))).Tip(Domain.L.T("editor.tip_add_pre_cond"));
            row.AddButton("×", Ui.A(() => { r.Pre.RemoveAt(i); Refresh(); }));
        }
        bool needsRight = !(isList && p.Test is PreTest.NotEmpty or PreTest.Empty);
        if (needsRight)
            ExprEditor.Build(b, p.Right ??= Expr.Of(0), new ExprEditor.Options { Stats = RulesUi.VisibleSelectors.Select(x => x.Id).ToList(), Notes = s, Sys = true, Inline = true, Tail = Tail }, Refresh);
        else
        {
            b.AddFlexibleSpace();
            Tail(b);
            b.End();
        }
        if (isList && p.Where != null)
        {
            if (p.Where.Preset != null)
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.preset_satisfied", Catalog.Preset(p.Where.Preset)?.Name ?? p.Where.Preset))
                    .AddButton("×", Ui.A(() => { p.Where = null; Refresh(); })).End();
            else
                CondEditor.Row(b, p.Where, PreProps(p), RulesUi.VisibleSelectors.Where(x => x.IsNumber || !Eval.YieldsBoxes(x)).Select(x => x.Id).ToList(), Domain.L.T("editor.satisfy_prefix"),
                    () => Catalog.Any, KindChoices, () => p.Where = null, Refresh);
        }
    }

    /// <summary>前提列表此刻有的东西（取属性、常用条件用），最多 40 件。</summary>
    private static List<object> PreItems(PreCond p)
    {
        try { return Eval.List(new EvalCtx(RulesUi.World, D), p.Ref).Take(40).ToList(); }
        catch { return new List<object>(); }
    }

    private static List<string> PreProps(PreCond p)
    {
        var sel = Defs.Selector(D, p.Ref);
        string kind = sel == null || sel.Kind == Catalog.Self ? Catalog.Any : sel.Kind;
        return RulesUi.PropsFor(kind, PreItems(p));
    }

    private static List<(string id, string name)> KindChoices() =>
        Catalog.Traits.Where(t => !Catalog.IsContainerType(t.Id) && t.Id != Catalog.Any).Select(t => (t.Id, t.Name)).ToList();

    /// <summary>给列表前提加一个条件：常用条件（只列列表里此刻有的那几类东西的），或按属性比较。</summary>
    private static void AddPreCond(PreCond p)
    {
        var items = PreItems(p);
        var have = RulesUi.TraitsOf(items);
        var opts = new List<(string, Action)>();
        foreach (var x in Catalog.PresetsFor(Catalog.Any, RulesUi.World).Where(x => items.Count == 0 || x.Applies == null || have.Contains(x.Applies)))
        {
            string id = x.Id;
            opts.Add((x.Name, () => { p.Where = Cond.Of(id); Refresh(); }));
        }
        opts.Add((Domain.L.T("editor.cmp_by_prop"), () => { p.Where = Cond.Cmp(PreProps(p).FirstOrDefault() ?? "weight", ">", Expr.Of(0)); Refresh(); }));
        Pickers.Choose(Domain.L.T("editor.add_cond_title"), opts, Domain.L.T("editor.add_cond_tip"));
    }

    private static List<(string id, string name)> WithNone(List<(string id, string name)> list, string none)
    {
        var l = new List<(string id, string name)> { (null, none) };
        l.AddRange(list);
        return l;
    }

    private static void Pick(CustomUIBuilder b, List<(string id, string name)> opts, string current, Action<string> set)
    {
        b.AddDropdown(Ui.L(opts.Select(o => o.name)), RulesUi.IndexOf(opts, o => o.id == current), Ui.AI(k => { set(opts[k].id); Refresh(); }));
    }

    /// <summary>
    /// 一个动作：第一行是动作和对象，第二行是另一个集合（放入的目标、交换的另一组、使用的工具）或按钮、次数。
    /// 对哪些、什么顺序、几件都在筛选器里设置，动作本身没有别的选项。
    /// </summary>
    private static void ActionRows(CustomUIBuilder b, Schedule s, WorkRule r, int i)
    {
        var a = r.Actions[i];
        var verbs = Catalog.Ops;
        var items = WithNone(RulesUi.ItemSelectors, Domain.L.T("editor.pick_target_placeholder"));

        b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Tone.Dim($"{i + 1}."))
            .AddDropdown(Ui.L(verbs.Select(v => v.Name)), RulesUi.IndexOf(verbs, v => v.Id == a.Verb), Ui.AI(k =>
            {
                var keep = a.List;
                var na = AutoMod.Domain.Rules.Card.SwitchOp(D, a, verbs[k].Id);
                if (verbs[k].Id == ActVerb.Note) { na.List = null; na.Value = Expr.Ref(TermKind.Stat, keep); na.Note = s.Notepad.FirstOrDefault()?.Id; }
                r.Actions[i] = na;
                Refresh();
            }));
        // 第一行和句子的开头一致：“使用 [工具]”“放入 [对象]”“交换 [对象]”……
        if (a.Verb == ActVerb.Use) Pick(b, WithNone(RulesUi.ToolSources(), Domain.L.T("editor.pick_tool_placeholder")), a.Tool, v => a.Tool = v);
        else if (a.Verb != ActVerb.Note) ListPicker(b, s, a.List, v => a.List = v);
        b.AddButton(Domain.L.T("ui.move_up"), Ui.A(() => { if (i > 0) { r.Actions.RemoveAt(i); r.Actions.Insert(i - 1, a); Refresh(); } }))
            .AddButton("×", Ui.A(() => { r.Actions.RemoveAt(i); Refresh(); }))
            .End();

        switch (a.Verb)
        {
            case ActVerb.Move:
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.action_target"))
                    .WithTooltip(Domain.L.T("editor.tip_action_target"));
                Pick(b, WithNone(RulesUi.Destinations(), Domain.L.T("editor.pick_dest_placeholder")), a.To, v => a.To = v);
                b.End();
                break;
            case ActVerb.Swap:
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.action_swap_with"))
                    .WithTooltip(Domain.L.T("editor.tip_action_swap"));
                ListPicker(b, s, a.To, v => a.To = v);
                b.End();
                break;
            case ActVerb.Use:
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.action_apply_to"))
                    .WithTooltip(Domain.L.T("editor.tip_action_apply"));
                ListPicker(b, s, a.List, v => a.List = v, 260);
                Times(b, a);
                b.End();
                break;
            case ActVerb.Press:
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.action_button"));
                Pick(b, WithNone(RulesUi.ButtonsOf(a.List, a.Button), Domain.L.T("editor.pick_btn_placeholder")), a.Button, v => a.Button = v);
                Times(b, a);
                b.End();
                break;
            case ActVerb.Activate:
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel("   ");
                Times(b, a);
                b.End();
                break;
            case ActVerb.Mark:
            case ActVerb.Unmark:
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel(Domain.L.T("editor.action_mark")).AddInput(Domain.L.T("editor.input_mark_name"), a.Text ?? "", Ui.AS(v => a.Text = v.Trim())).End();
                break;
            case ActVerb.Note:
                b.BeginRow(4).AddLabel(Domain.L.T("editor.action_value")).End();
                ExprEditor.Build(b, a.Value ??= Expr.Of(0), new ExprEditor.Options { Stats = RulesUi.VisibleSelectors.Select(x => x.Id).ToList(), Notes = s, Sys = true, PerRow = 1 }, Refresh);
                b.BeginRow(4, TextAnchor.MiddleLeft).AddLabel("    ")
                    .AddDropdown(Ui.L(new[] { Domain.L.T("editor.note_add"), Domain.L.T("editor.note_set") }), a.NoteMode == "set" ? 1 : 0, Ui.AI(k => { a.NoteMode = k == 1 ? "set" : "add"; Refresh(); }));
                Pick(b, WithNone(s.Notepad.Select(n => (n.Id, n.Name)).ToList(), Domain.L.T("editor.pick_counter_placeholder")), a.Note, v => a.Note = v);
                b.End();
                break;
        }
    }

    /// <summary>行内的“每件 [n] 次”。</summary>
    private static void Times(CustomUIBuilder b, ActionDef a)
    {
        b.AddLabel(Domain.L.T("editor.times_each"))
            .AddInput("1", a.Times.ToString(), Ui.AS(v => { if (int.TryParse(v, out var x) && x >= 1) a.Times = x; })).Width(50);
        string u = Domain.L.T("editor.times_unit");
        if (!string.IsNullOrEmpty(u)) b.AddLabel(u);
    }
}
