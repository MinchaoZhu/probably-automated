using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Adapter;
using AutoMod.App;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;
using AutoMod.UI.Intercom;
using AutoMod.UI.Kit;
using AutoMod.UI.Simulator;
using AutoMod.UI.Store;

namespace AutoMod.UI.Schedules;

/// <summary>
/// 对讲机“排班”页：每个排班一张卡片。
/// 卡片上能直接编辑已有的参数、容器、规则；新建 / 删除规则、挑选器、对象在排班编辑器里做。
/// </summary>
public static class SchedulePage
{
    /// <summary>下拉菜单里标出当前选中的那一项。</summary>
    private static string Current => "  " + Tone.Small(Tone.Dim(Domain.L.T("schedule.current_tag")));

    private static readonly HashSet<string> Open = new();
    private static string _openRule;

    private static RulesData D => RulesUi.D;

    /// <summary>展开某个排班的卡片。</summary>
    public static void Expand(Schedule s)
    {
        Open.Clear();
        Open.Add(s.Id);
        _openRule = null;
    }

    /// <summary>对讲机开着且在排班页时重建（数据在别处改了之后）。</summary>
    public static void Refresh()
    {
        if (Ui.IsOpen(MainWindow.Id) && MainWindow.Tab == "schedules") MainWindow.Show("schedules");
    }

    private static void Rebuild()
    {
        RulesUi.Changed();
        MainWindow.Show("schedules");
    }

    public static void Build(CustomUIBuilder b)
    {
        b.BeginRow(8)
            .AddButton(Domain.L.T("schedule.btn_new"), Ui.A(() => NewScheduleWindow.Show(NewBlank, NewBatteryExample, NewProfessionExample, Import)))
            .End();
        if (D.Schedules.Count == 0)
        {
            b.AddLabel(Tone.Dim(Domain.L.T("schedule.empty")));
            return;
        }
        float ruleColW = Domain.L.Float("layout.schedule.col_rule_width", 90f);
        float devColW = Domain.L.Float("layout.schedule.col_device_width", 90f);
        Table.Header(b, 0, (Domain.L.T("schedule.col_rule_header"), ruleColW), (Domain.L.T("schedule.col_device_header"), devColW));
        for (int i = 0; i < D.Schedules.Count; i++) Card(b, D.Schedules[i], i);
    }

    // ================= 卡片 =================

    /// <summary>
    /// 一个流程，两行：第一行展开按钮、名字、状态；第二行任务数、设备名数和统计、编辑。只讲流程本身，不提学徒。
    /// 展开后是参数、设备名、任务和操作。
    /// </summary>
    private static void Card(CustomUIBuilder b, Schedule s, int index)
    {
        bool open = Open.Contains(s.Id);
        int errs = RulesUi.ScheduleErrors(s);
        var notes = errs > 0 ? new List<string>() : Session.ScheduleNotes(s);
        string status = errs > 0 ? Table.Status(Domain.L.N("schedule.status_errors", errs), Tone.Bad)
            : notes.Count > 0 ? Table.Status(notes.Count == 1 ? notes[0] : Domain.L.N("schedule.status_notes", notes.Count - 1, notes[0]), Tone.Warn)
            : Table.Status(Domain.L.T("schedule.status_ready"), Tone.Ok);
        Table.BeginItem(b, index, open);
        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
        b.AddLabel(Tone.Bold(s.Name)).Tip(s.Description)
            .AddFlexibleSpace()
            .AddLabel(status).Tip(notes.Count > 1 ? string.Join("\n", notes) : null)
            .End();
        Table.BeginSecondLine(b);
        float ruleColW = Domain.L.Float("layout.schedule.col_rule_width", 90f);
        float devColW = Domain.L.Float("layout.schedule.col_device_width", 90f);
        Table.Cell(b, Domain.L.N("schedule.count_tasks", s.Rules.Count), ruleColW);
        Table.Cell(b, Domain.L.N("schedule.count_devices", AutoMod.Domain.Rules.Card.DeviceNames(D, s).Count), devColW);
        b.AddFlexibleSpace();
        Table.More(b, open, () => { if (!Open.Remove(s.Id)) Open.Add(s.Id); _openRule = null; MainWindow.Show("schedules"); });
        b.AddButton(Domain.L.T("schedule.btn_stats"), Ui.A(() => FlowStats.Show(s))).Tip(Domain.L.T("schedule.tip_stats"))
            .AddButton(Domain.L.T("schedule.btn_edit"), Ui.A(() => ScheduleEditor.Open(s)))
            .End();
        if (open)
        {
            if (!string.IsNullOrEmpty(s.Description)) b.AddLabel(Tone.Small(Tone.Dim(s.Description)));
            Parameters(b, s);
            Containers(b, s);
            Rules(b, s);
            b.BeginRow(6)
                .AddButton(Domain.L.T("schedule.btn_simulator"), Ui.A(() => SimulatorWindow.Open(s)))
                .AddFlexibleSpace()
                .AddButton(Domain.L.T("schedule.btn_export"), Ui.A(() => Export(s))).Tip(Domain.L.T("schedule.tip_export"))
                .AddButton(Domain.L.T("ui.copy"), Ui.A(() => Copy(s)))
                .AddButton(Domain.L.T("ui.delete"), Ui.A(() => Delete(s)))
                .End();
        }
        Table.EndItem(b);
    }

    /// <summary>参数：直接改数值（输入时只改数据，不重建窗口）。说明在名字的悬停提示里。</summary>
    private static void Parameters(CustomUIBuilder b, Schedule s)
    {
        var ps = AutoMod.Domain.Rules.Card.Parameters(D, s);
        if (ps.Count == 0) return;
        b.Section(Domain.L.T("schedule.section_params"));
        // 设备、列表参数一行一个，点值换一个设备名 / 筛选器
        foreach (var obj in ps.Where(o => o.IsRef))
        {
            var o = obj;
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(o.Name).Width(140).Tip(Hint(o))
                .AddButton(AutoMod.Domain.Rules.Text.ParamValue(D, o), Ui.A(() => PickParamValue(o))).End();
        }
        var nums = ps.Where(o => !o.IsRef).ToList();
        for (int k = 0; k < nums.Count; k += 2)
        {
            b.BeginRow(6);
            foreach (var obj in nums.Skip(k).Take(2))
            {
                var o = obj;
                b.AddLabel(o.Name).Tip(Hint(o));
                b.AddInput("0", o.Value.ToString("0.##"), Ui.AS(v => { if (double.TryParse(v, out var x)) { o.Value = x; RulesUi.Changed(); } })).Width(80)
                    .AddLabel(Tone.Dim(string.IsNullOrEmpty(o.Unit) ? " " : o.Unit)).Width(36);
            }
            b.End();
        }
    }

    /// <summary>卡片上换设备 / 列表参数的值。</summary>
    private static void PickParamValue(ObjectDef o)
    {
        var opts = o.Type == ParamType.Device ? RulesUi.DeviceNames() : Scope.Selectors(D, o.Owner).Where(x => !x.IsNumber).Select(x => (x.Id, x.Name)).ToList();
        Pickers.Choose(Domain.L.T("schedule.pick_param", o.Name), opts.Select(x =>
        {
            string id = x.Item1;
            return (x.Item2 + (id == o.Ref ? Current : ""), (Action)(() => { o.Ref = id; RulesUi.Changed(); Rebuild(); }));
        }));
    }

    private static string Hint(ObjectDef o)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(o.Hint)) lines.Add(o.Hint);
        if (AutoMod.Domain.Rules.Card.SharedBy(D, o.Id) > 1) lines.Add(Domain.L.T("schedule.param_shared"));
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>
    /// 设备名：排班里指定的地方（“放到【水培仓库】”）。店里有几个东西叫这个名字；还没有的标出来，点“起名…”去店里选一个。
    /// 不起名任务也照常做（示例里没有指定的地方就放任意有空位的储藏区）。
    /// </summary>
    private static void Containers(CustomUIBuilder b, Schedule s)
    {
        var names = AutoMod.Domain.Rules.Card.DeviceNames(D, s);
        if (names.Count == 0) return;
        b.Section(Domain.L.T("schedule.section_devices"));
        foreach (var n in names)
        {
            string name = n;
            var items = Session.World.Named(name).Cast<GameItem>().ToList();
            b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
            if (items.Count > 0) b.Icon(Icons.Of(items[0]), 24); else b.AddSpace(24);
            b.AddLabel(RulesUi.DeviceLabel(name)).Width(140)
                .AddLabel(items.Count > 0 ? Tone.Small(AutoMod.Domain.Rules.Text.Members(items.Select(i => Session.World.KindName(i)).ToList())) : Tone.Small(Tone.Dim(Domain.L.T("schedule.device_none"))))
                .Tip(items.Count > 0 ? null : Domain.L.T("schedule.device_tip"))
                .AddFlexibleSpace()
                .AddButton(items.Count > 0 ? Domain.L.T("schedule.btn_add_device") : Domain.L.T("schedule.btn_name_device"), Ui.A(() => DeviceNameDialog.Pick(name, Rebuild)))
                .End();
        }
    }

    // ================= 规则 =================

    private static void Rules(CustomUIBuilder b, Schedule s)
    {
        if (s.Rules.Count == 0) return;
        b.Section(Domain.L.T("schedule.section_rules"));
        var ctx = new EvalCtx(RulesUi.World, D);
        for (int i = 0; i < s.Rules.Count; i++)
        {
            var r = s.Rules[i];
            bool open = _openRule == r.Id;
            var errs = r.Enabled ? RulesUi.RuleErrors(s, r) : new List<string>();
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft)
                .AddButton($"{i + 1}. {r.Name}{(r.Enabled ? "" : Domain.L.T("schedule.rule_disabled"))}", Ui.A(() => { _openRule = open ? null : r.Id; MainWindow.Show("schedules"); }));
            Table.Cell(b, AutoMod.Domain.Rules.Text.Rule(D, s, r), 380, Tone.Dim);
            if (errs.Count > 0) b.AddLabel(Tone.Color("<b>!</b>", Tone.Bad)).Tip(Domain.L.T("schedule.err_cannot_run") + "\n" + string.Join("\n", errs));
            b.End();
            if (open) RuleDetail(b, s, r, i);
        }
    }

    private static void RuleDetail(CustomUIBuilder b, Schedule s, WorkRule r, int i)
    {
        b.BeginPanel(-1, -1, UnityEngine.TextAnchor.UpperLeft, 8, 2);
        foreach (var line in AutoMod.Domain.Rules.Card.Sentence(D, s, r))
        {
            b.BeginRow(2, UnityEngine.TextAnchor.MiddleLeft);
            foreach (var seg in line)
            {
                var sg = seg;
                if (sg.Kind is SegKind.Text) b.AddLabel(sg.Text);
                else if (sg.Kind is SegKind.Complex) b.AddLabel(Tone.Dim(sg.Text)).Tip(Domain.L.T("schedule.tip_edit_in_editor"));
                else b.AddButton(sg.Empty ? Tone.Color(sg.Text, Tone.Bad) : sg.Text, Ui.A(() => Token(s, r, sg)));
            }
            b.End();
        }
        b.BeginRow(6)
            .AddButton(Domain.L.T("schedule.btn_move_up"), Ui.A(() => { if (AutoMod.Domain.Rules.Card.Move(s, r, -1)) Rebuild(); }))
            .AddButton(Domain.L.T("schedule.btn_move_down"), Ui.A(() => { if (AutoMod.Domain.Rules.Card.Move(s, r, 1)) Rebuild(); }))
            .AddFlexibleSpace()
            .AddButton(Domain.L.T("schedule.btn_open_in_editor"), Ui.A(() => ScheduleEditor.Open(s, r.Id)))
            .End();
        b.End();
    }

    /// <summary>点句子里的词：换成别的已有项（新建 / 删除在编辑器里）。</summary>
    private static void Token(Schedule s, WorkRule r, Seg sg)
    {
        RulesUi.View = s.Id;
        var a = sg.Action >= 0 ? r.Actions[sg.Action] : null;
        var opts = new List<(string, Action)>();
        switch (sg.Kind)
        {
            case SegKind.List:
            {
                string cur = a != null ? AutoMod.Domain.Rules.Card.Get(a, sg.Param) : r.Pre.FirstOrDefault()?.Ref;
                foreach (var (pid, plabel) in RulesUi.RefParams())
                {
                    string id = pid;
                    opts.Add((plabel + (id == cur ? Current : ""), () =>
                    {
                        if (a != null) AutoMod.Domain.Rules.Card.Set(a, sg.Param, id); else r.Pre[0].Ref = id;
                        Rebuild();
                    }));
                }
                foreach (var (nid, label) in RulesUi.DeviceNames())
                {
                    string id = nid;
                    opts.Add((label + "  " + Tone.Small(Tone.Dim(Domain.L.T("schedule.token_named_item"))) + (id == cur ? Current : ""), () =>
                    {
                        if (a != null) AutoMod.Domain.Rules.Card.Set(a, sg.Param, id); else r.Pre[0].Ref = id;
                        Rebuild();
                    }));
                }
                foreach (var x in RulesUi.VisibleSelectors.Where(x => !x.IsNumber))
                {
                    var sel = x;
                    opts.Add(($"{sel.Name}  " + Tone.Small(Tone.Dim(AutoMod.Domain.Rules.Text.SelectorShort(D, sel))) + (sel.Id == cur ? Current : ""), () =>
                    {
                        if (a != null) AutoMod.Domain.Rules.Card.Set(a, sg.Param, sel.Id); else r.Pre[0].Ref = sel.Id;
                        Rebuild();
                    }));
                }
                var current = D.Selectors.FirstOrDefault(x => x.Id == cur);
                if (current != null)
                    opts.Add((Domain.L.T("schedule.btn_edit_selector", current.Name), () => SelectorDialog.Edit(current)));
                Pickers.Choose(Domain.L.T("schedule.pick_device_or_selector"), opts);
                break;
            }
            case SegKind.Box:
            {
                string cur = AutoMod.Domain.Rules.Card.Get(a, sg.Param);
                foreach (var (id, name) in sg.Param == "tool" ? RulesUi.ToolSources() : RulesUi.Destinations())
                {
                    string target = id;
                    opts.Add((name + (id == cur ? Current : ""), () => { AutoMod.Domain.Rules.Card.Set(a, sg.Param, target); Rebuild(); }));
                }
                Pickers.Choose(Domain.L.T("schedule.pick_param", AutoMod.Domain.Rules.Card.ParamName(sg.Param)), opts);
                break;
            }
            case SegKind.Type when sg.Param == "toslot":
            {
                foreach (var (id, name) in RulesUi.SlotsOf(a.To, a.ToSlot).Prepend((null, Domain.L.T("editor.slot_auto"))))
                {
                    string target = id;
                    opts.Add((name + (id == a.ToSlot ? Current : ""), () => { a.ToSlot = target; Rebuild(); }));
                }
                Pickers.Choose(Domain.L.T("schedule.pick_param", AutoMod.Domain.Rules.Card.ParamName(sg.Param)), opts);
                break;
            }
            case SegKind.Type when sg.Param == "flow":
            {
                foreach (var f in D.Schedules.Where(x => x.Id != s.Id))
                {
                    string id = f.Id;
                    opts.Add((f.Name + (id == a.Flow ? Current : ""), () => { AutoMod.Domain.Rules.Card.Set(a, "flow", id); Rebuild(); }));
                }
                Pickers.Choose(Domain.L.T("schedule.pick_flow"), opts, opts.Count == 0 ? Tone.Dim(Domain.L.T("schedule.no_other_flows")) : null);
                break;
            }
            case SegKind.Type when sg.Param == "button":
            {
                foreach (var (id, name) in RulesUi.ButtonsOf(a.List, a.Button))
                {
                    string label = id;
                    opts.Add((name + (id == a.Button ? Current : ""), () => { AutoMod.Domain.Rules.Card.Set(a, "button", label); Rebuild(); }));
                }
                Pickers.Choose(Domain.L.T("schedule.pick_button"), opts, opts.Count == 0 ? Tone.Dim(Domain.L.T("schedule.no_buttons")) : null);
                break;
            }
            case SegKind.Op:
                foreach (var op in Catalog.Ops.Where(o => o.Mechanism != Mechanism.Note))
                {
                    var verb = op.Id;
                    opts.Add((op.Name + (verb == a.Verb ? Current : ""), () =>
                    {
                        r.Actions[sg.Action] = AutoMod.Domain.Rules.Card.SwitchOp(D, a, verb, RulesUi.World);
                        Rebuild();
                    }));
                }
                Pickers.Choose(Domain.L.T("schedule.pick_action"), opts);
                break;
            case SegKind.Cond:
                opts.Add((Domain.L.T("schedule.cond_always"), () => { AutoMod.Domain.Rules.Card.SetPre(r, null); Rebuild(); }));
                opts.Add((Domain.L.T("schedule.cond_not_empty"), () => { AutoMod.Domain.Rules.Card.SetPre(r, PreTest.NotEmpty); Rebuild(); }));
                opts.Add((Domain.L.T("schedule.cond_empty"), () => { AutoMod.Domain.Rules.Card.SetPre(r, PreTest.Empty); Rebuild(); }));
                opts.Add((Domain.L.T("schedule.cond_count_gt"), () => Pickers.Number(Domain.L.T("schedule.count_gt_prompt"), 0, n => { AutoMod.Domain.Rules.Card.SetPre(r, PreTest.CountGt, n); Rebuild(); })));
                opts.Add((Domain.L.T("schedule.cond_count_lt"), () => Pickers.Number(Domain.L.T("schedule.count_lt_prompt"), 1, n => { AutoMod.Domain.Rules.Card.SetPre(r, PreTest.CountLt, n); Rebuild(); })));
                opts.Add((Domain.L.T("schedule.cond_advanced"), () => ScheduleEditor.Open(s, r.Id)));
                Pickers.Choose(Domain.L.T("schedule.pick_condition"), opts);
                break;
        }
    }

    // ================= 排班 =================

    private static void NewBlank()
    {
        var s = new Schedule { Id = Defs.NewId("sch"), Created = Defs.Now(), Name = Unique(D.Schedules.Select(x => x.Name), Domain.L.T("schedule.default_name_seq", D.Schedules.Count + 1)) };
        D.Schedules.Add(s);
        Expand(s);
        ScheduleEditor.Open(s);
    }

    /// <summary>示例排班：建好后展开卡片，提示玩家把满电区、待充区换成店里的容器。</summary>
    private static void NewBatteryExample()
    {
        var s = ExampleSetup.BatteryRotation();
        Expand(s);
        Rebuild();
    }

    private static void NewProfessionExample(string id)
    {
        var s = ExampleSetup.Profession(id);
        Expand(s);
        Rebuild();
    }

    private static void Export(Schedule s)
    {
        Clipboard.Text = ExampleSetup.Export(s);
        Toast.Show(Domain.L.T("schedule.copied_to_clipboard", s.Name));
    }

    /// <summary>从剪贴板导入：店里还没有的设备名在卡片上标出来。</summary>
    private static void Import()
    {
        try
        {
            var s = ExampleSetup.Import(Clipboard.Text);
            Expand(s);
            Rebuild();
        }
        catch (TransferException ex) { Toast.Show(ex.Message + "。"); }
    }

    private static void Copy(Schedule s)
    {
        // 本排班定义一起复制
        Scope.CopySchedule(D, s, Unique(D.Schedules.Select(x => x.Name), Domain.L.T("schedule.copy_name", s.Name)));
        Rebuild();
    }

    private static void Delete(Schedule s)
    {
        // 被别的流程调用时不能删：先改掉那些调用（否则那些任务没法执行）
        var callers = Calls.Callers(D, s).Where(x => x.Id != s.Id).ToList();
        if (callers.Count > 0)
        {
            Pickers.Choose(Domain.L.T("schedule.del_confirm_title", s.Name), new (string, Action)[] { (Domain.L.T("ui.ok"), () => { }) },
                Domain.L.T("schedule.del_called_note", Domain.L.Join("list", callers.Select(x => x.Name))));
            return;
        }
        var runners = Automation.State.Apprentices.Where(a => a.Duties.Any(d => d.ScheduleId == s.Id)).ToList();
        var lines = new List<string>();
        int own = Scope.OwnCount(D, s);
        if (own > 0) lines.Add(Domain.L.T("schedule.del_own_note", own));
        if (runners.Count > 0) lines.Add(Domain.L.T("schedule.del_runners_note", Domain.L.Join("list", runners.Select(a => a.Name))));
        Pickers.Confirm(Domain.L.T("schedule.del_confirm_title", s.Name), lines.ToArray(),
            (Domain.L.T("ui.delete"), () =>
            {
                Scope.DeleteSchedule(D, s);
                foreach (var a in runners) a.Duties.RemoveAll(d => d.ScheduleId == s.Id);
                Rebuild();
            }), (Domain.L.T("ui.cancel"), () => { }));
    }

    public static string Unique(IEnumerable<string> existing, string name)
    {
        var set = new HashSet<string>(existing);
        if (!set.Contains(name)) return name;
        for (int i = 2; ; i++)
        {
            string cand = name + Domain.L.T("text.paren", i);
            if (!set.Contains(cand)) return cand;
        }
    }
}
