using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Persistence;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;
using AutoMod.Domain.Workforce;
using AutoMod;
using AutoMod.Adapter;
using AutoMod.App;
using AutoMod.UI.Intercom;
using AutoMod.UI.Kit;
using AutoMod.UI.Schedules;
using AutoMod.UI.Store;
using AutoMod.Domain;

namespace AutoMod.UI.Simulator;

/// <summary>
/// 策略模拟器：在店铺副本上运行排班。默认使用店铺当前状态和该排班的指派学徒；
/// 可添加物品、虚拟学徒；按班次、按规则显示执行结果，点击规则展开明细。
/// </summary>
public static class SimulatorWindow
{
    private const string Id = "am_rsim";

    private class ShiftResult
    {
        public int N, Day;
        public string Shift;
        /// <summary>每名学徒执行一次是一组。</summary>
        public List<GroupRunReport> Groups = new();
    }

    private static string _schId;
    private static SimWorld _world;
    private static List<Apprentice> _apps = new();
    private static string _start = Shift.Morning;
    private static int _runs = 1;
    private static readonly List<ShiftResult> Results = new();
    private static readonly HashSet<string> Expanded = new();
    private static string _note;

    private static RulesData D => Session.D;
    private static Schedule Sch => D.Schedules.FirstOrDefault(s => s.Id == _schId);

    public static void Open(Schedule s)
    {
        ScheduleEditor.Hide();
        Ui.Close(MainWindow.Id);
        _schId = (s ?? D.Schedules.FirstOrDefault())?.Id;
        Reset();
        Show();
    }

    private static Apprentice Clone(Apprentice a) =>
        AutoMod.Domain.JsonCodec.Clone(a);

    private static void Reset()
    {
        Results.Clear();
        Expanded.Clear();
        _note = null;
        try { _world = SimImport.FromGame(Automation.State.Rules, seed: 7); }
        catch (Exception ex)
        {
            _world = new SimWorld();
            _note = Domain.L.T("sim.copy_failed", ex.Message);
            Log.Warn($"[RuleSim] {ex}");
        }
        // 执行此流程的学徒副本：只保留这个流程的工作，AP 从当天开始算
        var runners = Automation.State.Apprentices.Where(a => a.Duties.Any(d => d.ScheduleId == _schId)).ToList();
        _apps = runners.Select(Clone).ToList();
        foreach (var a in _apps)
        {
            a.Training = null;
            a.ApUsed = 0;
            a.Duties = a.Duties.Where(d => d.ScheduleId == _schId).ToList();
        }
        _start = _apps.SelectMany(a => a.Duties).Any(d => d.Shift == Shift.Night) ? Shift.Night : Shift.Morning;
    }

    private static void Run()
    {
        var s = Sch;
        if (s == null) return;
        var interp = new Interpreter(_world, D);
        string shift = Results.Count == 0 ? _start : (Results[^1].Shift == Shift.Morning ? Shift.Night : Shift.Morning);
        for (int k = 0; k < _runs; k++)
        {
            if (shift == Shift.Night) foreach (var a in _apps) a.ApUsed = 0; // 新的一天从晚班开始（入睡时发薪、AP 回满）
            var group = _apps.Where(a => a.DutiesIn(shift).Any()).ToList();
            var r = new ShiftResult { N = Results.Count + 1, Shift = shift, Day = _world.Day };
            if (group.Count > 0) r.Groups = interp.RunShift(shift, group, applyXp: true).Groups;
            Results.Add(r);
            if (shift == Shift.Night) _world.Night();
            shift = shift == Shift.Morning ? Shift.Night : Shift.Morning;
        }
        Show();
    }

    private static string SimTip => Domain.L.T("sim.tip");

    // ================= 窗口 =================

    public static void Show()
    {
        var s = Sch;
        var b = Ui.Window(Id, Domain.L.T("sim.title"), 1440, 860, draggable: false);
        var schedules = Defs.PickOrder(D); // 新建的排在最前
        var runs = new[] { (1, Domain.L.T("sim.run_1_shift")), (2, Domain.L.T("sim.run_1_day")), (6, Domain.L.T("sim.run_3_days")), (14, Domain.L.T("sim.run_7_days")) };
        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
        if (schedules.Count > 0)
            b.AddLabel(Domain.L.T("sim.schedule")).AddDropdown(Ui.L(schedules.Select(x => Table.Fit(x.Name, 170, 13, out _))), Math.Max(0, schedules.IndexOf(s)), Ui.AI(k => { _schId = schedules[k].Id; Reset(); Show(); })).Width(200);
        b.AddLabel(Domain.L.T("sim.start_shift")).AddDropdown(Ui.L(new[] { Domain.L.T("sim.start_morning"), Domain.L.T("sim.start_night") }), _start == Shift.Night ? 1 : 0, Ui.AI(k => { _start = k == 1 ? Shift.Night : Shift.Morning; Show(); })).Width(Domain.L.Int("layout.sim.start_width", 90))
            .AddLabel(Domain.L.T("sim.duration")).AddDropdown(Ui.L(runs.Select(r => r.Item2)), Ui.IndexOf(runs, r => r.Item1 == _runs), Ui.AI(k => { _runs = runs[k].Item1; Show(); })).Width(Domain.L.Int("layout.sim.duration_width", 110))
            .AddButton(Domain.L.T("sim.btn_start"), Ui.A(Run))
            .AddButton(Domain.L.T("ui.reset"), Ui.A(() => { Reset(); Show(); }))
            .AddLabel(Tone.Small(Tone.Dim(Results.Count > 0 ? Domain.L.N("sim.ran_shifts_notice", Results.Count) : "")))
            .AddFlexibleSpace()
            .AddButton("?", Ui.A(() => { })).Tip(SimTip)
            .AddButton(Domain.L.T("sim.btn_edit_schedule"), Ui.A(() => { Ui.Close(Id); if (s != null) ScheduleEditor.Open(s); }))
            .AddButton(Domain.L.T("ui.close"), Ui.A(() => Ui.Close(Id)))
            .End();
        if (_note != null) b.AddLabel(Tone.Color(_note, Tone.Bad));
        if (s == null) { b.AddLabel(Tone.Dim(Domain.L.T("sim.no_schedules"))); b.Show(); return; }

        b.BeginRow(10);
        // ---- 左：模拟店铺、学徒 ----
        b.BeginColumn(4);
        b.AddImage(ShapeSprites.White, 520, 1, "am_sp_sim");
        b.AddLabel(Tone.Bold(Domain.L.T("sim.shop_copy")));
        b.BeginScroll(480);
        // 这个排班会动的设备：用到的设备名、选出机器 / 储藏区的筛选器（最多 30 个）
        var used = Check.Closure(D, s.Rules.SelectMany(Check.RuleRefs));
        var ctx = new EvalCtx(_world, D);
        var groups = new List<(string title, List<SimItem> boxes)>();
        var shown = new HashSet<SimItem>();
        foreach (var id in used.Where(x => Places.IsName(x) || D.Selectors.FirstOrDefault(y => y.Id == x) is { } sel && Eval.YieldsBoxes(sel, _world)))
        {
            var boxes = Eval.Boxes(ctx, id).Cast<SimItem>().Where(shown.Add).Take(30 - shown.Count + 1).ToList();
            groups.Add((Places.IsName(id) ? Domain.L.T("text.bracket", Places.NameOf(id)) : AutoMod.Domain.Rules.Text.Name(D, id), boxes));
            if (shown.Count >= 30) break;
        }
        foreach (var (title, boxes) in groups)
        {
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(title)).AddFlexibleSpace();
            if (boxes.Count > 0) b.AddButton(Domain.L.T("sim.btn_add_items"), Ui.A(() => AddItems(title, boxes[0])));
            b.End();
            if (boxes.Count == 0) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("sim.none_in_shop"))));
            foreach (var box in boxes.Take(6)) b.AddLabel(Tone.Small(Domain.L.T("text.pair", box.Name, Summary(box))));
            if (boxes.Count > 6) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("sim.more_items_count", boxes.Count - 6))));
        }
        b.End();
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Bold(Domain.L.T("sim.apprentices"))).AddFlexibleSpace().AddButton(Domain.L.T("sim.btn_add_vapp"), Ui.A(AddApprentice)).End();
        if (_apps.Count == 0) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("sim.no_apps_tip"))));
        foreach (var a in _apps.ToList())
        {
            var app = a;
            int x = Proficiency.Score(app.Proficiency, Proficiency.ProfileOf(D, Sch, Session.World));
            string shifts = Domain.L.Join("list", app.Duties.Select(d => Shift.DisplayName(d.Shift)).Distinct());
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Small(Domain.L.T("sim.app_line", app.Name, shifts, app.Level, app.MaxAp, x))).AddFlexibleSpace()
                .AddButton("×", Ui.A(() => { _apps.Remove(app); Show(); })).End();
        }
        b.End();

        // ---- 右：结果 ----
        b.BeginColumn(4);
        b.AddImage(ShapeSprites.White, 860, 1, "am_sp_sim2");
        b.BeginScroll(700);
        if (Results.Count == 0) b.AddLabel(Tone.Dim(Domain.L.T("sim.welcome_tip")));
        for (int i = Results.Count - 1; i >= 0; i--) ResultBlock(b, s, Results[i]);
        b.End();
        b.End();
        b.End();
        var win = b.Show();
        try
        {
            foreach (var sp in new[] { "am_sp_sim", "am_sp_sim2" })
                if (win.Get(sp)?.image is { } img) img.color = new UnityEngine.Color(0, 0, 0, 0);
        }
        catch { }
    }

    private static void ResultBlock(CustomUIBuilder b, Schedule s, ShiftResult r)
    {
        string head = Tone.Bold(Domain.L.T("sim.day_shift_head", r.Day, Shift.DisplayName(r.Shift)));
        if (r.Groups.Count == 0) { b.AddLabel(head); b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("sim.no_workers_this_shift")))); b.AddSpace(6); return; }
        for (int gi = 0; gi < r.Groups.Count; gi++) GroupBlock(b, s, r, gi, head + Tone.Bold(" · " + Domain.L.Join("list", r.Groups[gi].Workers)));
    }

    private static void GroupBlock(CustomUIBuilder b, Schedule s, ShiftResult r, int gi, string head)
    {
        var g = r.Groups[gi];
        string memo = Domain.L.Join("comma", s.Notepad.Where(n => g.Memo.ContainsKey(n.Id)).Select(n => $"{n.Name} = {g.Memo[n.Id]:0.##}"));
        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft).AddLabel(head)
            .AddLabel(Tone.Small(Tone.Dim(Domain.L.T("sim.group_stats", $"{g.ApUsed:0}", $"{g.ApTotal:0}", $"{g.Mult:0.00}") + (memo != "" ? " · " + Domain.L.T("sim.memo_tag", memo) : ""))))
            .Tip(Domain.L.T("sim.tip_prof", Domain.L.Join("list", g.Proficiency.Select(kv => $"{kv.Key} {kv.Value}"))));
        if (g.Deferred > 0) b.AddLabel(Tone.Small(Tone.Color(Domain.L.T("sim.deferred_items_warn", g.Deferred), Tone.Warn)));
        b.End();
        Table.Header(b, 0, (Domain.L.T("table.rule"), 220), (Domain.L.T("table.result"), 480), (Domain.L.T("table.ap"), 0));
        for (int k = 0; k < g.Rules.Count; k++)
        {
            var rr = g.Rules[k];
            string key = $"{r.N}:{gi}:{k}";
            string done = rr.Done.Count > 0 ? Domain.L.Join("list", rr.Done.Select(kv => Domain.L.N("sim.op_done_count", kv.Value, Catalog.Op(kv.Key).Name)))
                : rr.Pre != PreResult.Pass ? AutoMod.Domain.Rules.Text.Outcome(rr)
                : rr.Issues.Count > 0 || rr.Deferred > 0 ? "" // 原因写在下面的问题行里
                : rr.Idle ?? Domain.L.T("sim.no_pending_items");
            bool open = Expanded.Contains(key);
            Table.BeginItem(b, k, open);
            b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft)
                .AddButton($"{k + 1}. {rr.Name}", Ui.A(() => { if (!Expanded.Remove(key)) Expanded.Add(key); Show(); })).Width(220);
            Table.Cell(b, done, 480, rr.Done.Count > 0 ? null : Tone.Dim);
            b.AddFlexibleSpace()
                .AddLabel(Tone.Small(Tone.Dim(rr.Ap > 0 ? $"{rr.Ap:0} AP" : "")))
                .End();
            foreach (var i in rr.Issues)
            {
                string c = i.Code is FailCode.NoTool or "broken" ? "#FF8A6B" : "#F0C060";
                b.AddLabel($"<size=12>    <color={c}>{i.Text}</color></size>");
            }
            if (rr.Deferred > 0) b.AddLabel(Tone.Small(Tone.Color(Domain.L.N("sim.rule_deferred_warn", rr.Deferred), Tone.Warn)));
            if (open)
                foreach (var line in rr.Details.Take(60)) b.AddLabel($"<size=12>        {line}</size>");
            Table.EndItem(b);
        }
        if (g.LevelUps.Count > 0) b.AddLabel($"<size=13>{Domain.L.T("sim.lvl_up", Domain.L.Join("list", g.LevelUps))}</size>");
        b.AddLabel(" ");
    }

    private static string Pct(double f) => Math.Abs(f - 1) < 1e-6 ? Domain.L.T("sim.pct_unchanged") : $"{(f < 1 ? "−" : "+")}{Math.Round(Math.Abs(1 - f) * 100, 1)}%";

    /// <summary>容器内容摘要：鼠的雌雄与死亡数、电池电量、子容器、工具、产物、液体、电池槽。</summary>
    public static string Summary(SimItem box)
    {
        var parts = new List<string>();
        var rats = box.Children.Where(c => c.Kind == "rat").ToList();
        if (rats.Count > 0)
        {
            int dead = rats.Count(r => r.N("dead") > 0);
            string deadPart = dead > 0 ? " · " + Domain.L.T("sim.rat_dead_count", dead) : "";
            parts.Add(Domain.L.T("sim.rats_summary", rats.Count(r => r.N("dead") == 0 && r.Str.GetValueOrDefault("sex") == "MALE"), rats.Count(r => r.N("dead") == 0 && r.Str.GetValueOrDefault("sex") == "FEMALE")) + deadPart);
        }
        var bats = box.Children.Where(c => !c.InBatterySlot && SimWorld.InGroup(c, "battery")).ToList();
        if (bats.Count > 0) parts.Add(Domain.L.T("sim.battery_summary", string.Join(" ", bats.Select(x => $"{x.N("energy") * 100 / Math.Max(1, x.N("energyMax", 100)):0}%"))));
        var sub = box.Children.Where(SimWorld.IsBox).ToList();
        if (sub.Count > 0)
        {
            string inner = string.Join(" ", sub.Select(x => x.Children.Count == 0 ? Domain.L.T("sim.empty") : string.Join("", x.Children.Where(c => SimWorld.InGroup(c, "battery")).Select(c => $"{c.N("energy") * 100 / Math.Max(1, c.N("energyMax", 100)):0}%"))));
            parts.Add(Domain.L.T("sim.sub_summary", sub[0].Name, sub.Count, inner));
        }
        var others = box.Children.Where(c => c.Kind != "rat" && !SimWorld.InGroup(c, "battery") && !SimWorld.IsBox(c)).GroupBy(c => c.Name);
        foreach (var g in others) parts.Add(g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        if (box.Num.ContainsKey("volumeMl")) parts.Add(Domain.L.T("sim.water_summary", $"{box.N("volumeMl"):0}", $"{box.N("purity"):0}"));
        var slot = box.Children.FirstOrDefault(c => c.InBatterySlot);
        if (box.HasBatterySlot) parts.Add(Domain.L.T("sim.slot_summary", slot == null ? Domain.L.T("sim.empty") : $"{slot.N("energy") * 100 / Math.Max(1, slot.N("energyMax", 100)):0}%"));
        string cap = box.Capacity > 0 ? " <color=#A8988F>" + Domain.L.T("text.paren", $"{box.Used}/{box.Capacity}") + "</color>" : "";
        return (parts.Count == 0 ? Domain.L.T("sim.empty") : string.Join(" · ", parts)) + cap;
    }

    private const string AddId = "am_simadd";
    private static string _addFilter = "";
    private static int _addCount = 1;
    private static bool _addToSlot;

    /// <summary>
    /// 向模拟店铺的容器添加物品：种类来自游戏的物品目录（店里有的在前）。店里有的照一件实物复制（状态和店里的一样），
    /// 没有的照原型复制。允许超出容量，用于测试容器已满的情况。
    /// </summary>
    private static void AddItems(string title, SimItem box)
    {
        const int max = 80;
        var kinds = SimImport.Kinds(_addFilter);
        var b = Ui.Window(AddId, Domain.L.T("sim.add_to_title", title), 620, 560);
        b.BeginRow(4)
            .AddLabel(Domain.L.T("ui.search")).AddInput(Domain.L.T("sim.search_placeholder"), _addFilter, Ui.AS(v => { _addFilter = v.Trim(); AddItems(title, box); }))
            .AddLabel(Domain.L.T("sim.count_label")).AddInput("1", _addCount.ToString(), Ui.AS(v => { if (int.TryParse(v, out var n) && n >= 1 && n <= 50) _addCount = n; }));
        if (box.HasBatterySlot)
            b.AddLabel(Domain.L.T("sim.place_into")).AddDropdown(Ui.L(new[] { Domain.L.T("sim.place_in_container"), Domain.L.T("sim.place_in_battery_slot") }), _addToSlot ? 1 : 0, Ui.AI(k => _addToSlot = k == 1)).Width(110);
        b.End();

        b.BeginScroll(380);
        foreach (var (kind, name, inShop) in kinds.Take(max))
        {
            string k = kind;
            b.AddButton($"{name}  " + Tone.Small(Tone.Dim((inShop > 0 ? Domain.L.T("sim.in_shop_count", inShop) : Domain.L.T("sim.none_in_shop")) + " · " + kind)), Ui.A(() =>
            {
                bool slot = _addToSlot && box.HasBatterySlot;
                if (slot) foreach (var c in box.Children.Where(c => c.InBatterySlot).ToList()) _world.Remove(c);
                for (int i = 0; i < (slot ? 1 : _addCount); i++)
                {
                    var x = SimImport.NewOfKind(_world, k);
                    if (x == null) { _note = Domain.L.T("sim.cannot_create_item", name); break; }
                    _world.Attach(x, box, slot);
                }
                Ui.Close(AddId);
                Show();
            }));
        }
        if (kinds.Count == 0) b.AddLabel(Domain.L.T("sim.no_matching_items"));
        if (kinds.Count > max) b.AddLabel($"<size=12>{Domain.L.T("sim.more_kinds_search", kinds.Count - max)}</size>");
        b.End();
        b.BeginRow(4)
            .AddButton(Domain.L.T("ui.clear"), Ui.A(() => { foreach (var c in box.Children.ToList()) _world.Remove(c); Ui.Close(AddId); Show(); }))
            .AddFlexibleSpace()
            .AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(AddId)))
            .End().Show();
    }

    private static void AddApprentice()
    {
        string name = Domain.L.T("sim.vapp_default_name"), ap = "25", lvl = "1", prof = "100";
        int shift = _start == Shift.Night ? 1 : 0;
        var b = Ui.Window("am_vapp", Domain.L.T("sim.vapp_title"), 420, 300)
            .BeginRow().AddLabel(Domain.L.T("ui.name")).AddInput(Domain.L.T("ui.name"), name, Ui.AS(v => name = v)).End()
            .BeginRow().AddLabel(Domain.L.T("sim.daily_ap")).AddInput("25", ap, Ui.AS(v => ap = v)).End()
            .BeginRow().AddLabel(Domain.L.T("sim.level")).AddInput("1", lvl, Ui.AS(v => lvl = v)).End()
            .BeginRow().AddLabel(Domain.L.T("sim.prof_label")).AddInput("100", prof, Ui.AS(v => prof = v)).End()
            .BeginRow().AddLabel(Domain.L.T("sim.shift_label")).AddDropdown(Ui.L(new[] { Domain.L.T("shift.morning"), Domain.L.T("shift.night") }), shift, Ui.AI(k => shift = k)).End()
            .BeginRow()
            .AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close("am_vapp")))
            .AddButton(Domain.L.T("ui.add"), Ui.A(() =>
            {
                var a = new Apprentice { Name = name, MaxAp = int.TryParse(ap, out var x) ? x : 25, Duties = { new Duty { ScheduleId = _schId, Shift = shift == 1 ? Shift.Night : Shift.Morning } } };
                a.Level = int.TryParse(lvl, out var l) ? Math.Clamp(l, 1, SkillMath.MaxLevel) : 1;
                a.Proficiency = Proficiency.ForScore(Proficiency.ProfileOf(D, Sch, Session.World), int.TryParse(prof, out var p) ? Math.Clamp(p, 0, 100) : 100);
                _apps.Add(a);
                Ui.Close("am_vapp");
                Show();
            }))
            .End();
        b.Show();
    }
}
