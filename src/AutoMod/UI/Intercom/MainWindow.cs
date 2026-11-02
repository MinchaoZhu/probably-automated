using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Workforce;
using AutoMod.App;
using AutoMod.UI.Kit;
using AutoMod.UI.Schedules;
using AutoMod.UI.Store;

namespace AutoMod.UI.Intercom;

/// <summary>对讲机窗口：学徒 / 排班 / 报告。只放状态和操作，规则写在手册里。</summary>
public static class MainWindow
{
    public const string Id = "am_main";
    private static string _tab = "apprentices";

    /// <summary>当前标签页：apprentices / schedules / reports。</summary>
    public static string Tab => _tab;

    public static void Toggle()
    {
        if (Ui.IsOpen(Id)) Ui.Close(Id); else Show();
    }

    public static void Show(string tab = null)
    {
        bool sameTab = tab == null || tab == _tab;
        if (tab != null) _tab = tab;
        var st = Automation.State;
        int dayAp = st.Apprentices.Where(a => a.Paid).Sum(a => a.DailyAp);

        var b = Ui.Window(Id, Domain.L.T("intercom.title"), 720, 620, keepScroll: sameTab);
        b.BeginRow(4);
        b.Tabs(_tab, ("apprentices", Domain.L.T("intercom.tab_apprentices"), () => Show("apprentices")), ("schedules", Domain.L.T("intercom.tab_schedules"), () => Show("schedules")), ("reports", Domain.L.T("intercom.tab_reports"), () => Show("reports")));
        b.AddFlexibleSpace().AddButton(Domain.L.T("intercom.handbook"), Ui.A(() => AutoMod.UI.Guide.Handbook.Open()));
        b.End();
        b.AddLabel(Tone.Dim(Domain.L.T("intercom.header_stats", st.Apprentices.Count, st.Slots, dayAp, Tone.Money(App.Payroll.Tonight()))));
        b.AddSeparator();

        b.BeginScroll(470);
        switch (_tab)
        {
            case "schedules": SchedulePage.Build(b); break;
            case "reports": BuildReports(b); break;
            default: BuildApprentices(b); break;
        }
        b.End();
        if (_tab == "apprentices")
        {
            b.BeginRow(8).AddButton(Domain.L.T("intercom.call_agency"), Ui.A(AgencyWindow.Show)).End();
        }
        b.Show();
    }

    // ---------- 学徒 ----------

    /// <summary>学徒页里展开的学徒（显示熟练度、培训、解雇）。</summary>
    private static string _openApp;

    private static void BuildApprentices(CustomUIBuilder b)
    {
        var st = Automation.State;
        if (st.Apprentices.Count == 0)
        {
            b.AddLabel(Tone.Dim(Domain.L.T("intercom.no_apprentices")));
            return;
        }
        Table.Header(b, 0, (Domain.L.T("intercom.col_level"), 45), (Domain.L.T("intercom.col_ap"), 40), (Domain.L.T("intercom.col_wage"), 65), (Domain.L.T("intercom.col_schedules"), 270));
        // 学徒列表的顺序：排在前面的先执行，资金不足时先发工资
        for (int i = 0; i < st.Apprentices.Count; i++) ApprenticeRow(b, st.Apprentices[i], i);
    }

    /// <summary>
    /// 一名学徒，两行：第一行名字、状态；第二行等级、每天的 AP、日薪、执行的流程。
    /// 展开后逐项设置流程和时间（可以有多项，串行执行，共用当天的 AP），以及熟练度说明、休息、培训、解雇。
    /// </summary>
    private static void ApprenticeRow(CustomUIBuilder b, Apprentice app, int index)
    {
        var st = Automation.State;
        bool open = _openApp == app.Name;
        var duties = app.Duties.Where(d => st.Rules.Schedules.Any(x => x.Id == d.ScheduleId)).ToList();
        string status = app.Striking ? Table.Status(Domain.L.T("intercom.status_strike"), Tone.Bad)
            : !app.Paid ? Table.Status(Domain.L.T("intercom.status_rest"), Tone.Muted)
            : duties.Count > 0 ? Table.Status(Domain.L.T("intercom.status_work"), Tone.Ok) : Table.Status(Domain.L.T("intercom.status_unassigned_paid"), Tone.Warn);
        Table.BeginItem(b, index, open);

        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
        b.AddLabel(Tone.Bold(app.Name));
        if (app.Training != null) b.AddLabel(Tone.Small(Tone.Color(Domain.L.T("intercom.training_pending"), Tone.Warn))).Tip(Domain.L.T("intercom.training_pending_tip", Agency.TrainingAp));
        b.AddFlexibleSpace().AddLabel(status).End();

        Table.BeginSecondLine(b);
        Table.Cell(b, $"Lv{app.Level}", 45, tip: app.Level < SkillMath.MaxLevel ? Domain.L.T("intercom.xp_tip", app.Xp, SkillMath.XpToNext(app.Level)) : null);
        Table.Cell(b, $"{app.DailyAp}", 40, tip: Domain.L.T("intercom.ap_tip"));
        Table.Cell(b, $"{app.Wage}", 65);
        if (duties.Count == 0)
        {
            // 还没有流程：直接选一个（早班），之后在“详情”里加更多
            var schedules = AutoMod.Domain.Rules.Defs.PickOrder(st.Rules);
            var names = new List<string> { Domain.L.T("intercom.select_schedule") };
            names.AddRange(schedules.Select(x => x.Name));
            b.AddDropdown(Ui.L(names), 0, Ui.AI(k =>
            {
                if (k == 0) return;
                app.Duties.Add(new Duty { ScheduleId = schedules[k - 1].Id, Shift = Shift.Morning });
                Session.Changed();
                Show();
            })).Width(270);
        }
        else Table.Cell(b, Domain.L.Join("list", duties.Select(d => $"{st.Rules.Schedules.First(x => x.Id == d.ScheduleId).Name}" + Domain.L.T("text.paren", ShortShift(d.Shift)))), 270,
            tip: Domain.L.T("intercom.duty_tip"));
        b.AddFlexibleSpace()
            .AddButton("↑", Ui.A(() => Move(app, -1))).Width(30).Tip(Domain.L.T("intercom.shift_order_tip"))
            .AddButton("↓", Ui.A(() => Move(app, +1))).Width(30);
        Table.More(b, open, () => { _openApp = open ? null : app.Name; Show(); });
        b.End();

        if (open)
        {
            b.AddSpace(2);
            DutyRows(b, app);
            b.AddSpace(2);
            Table.BeginSecondLine(b);
            b.BeginColumn(2);
            if (app.Level < SkillMath.MaxLevel) b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("intercom.xp_stat", app.Xp, SkillMath.XpToNext(app.Level)))));
            b.End();
            b.AddFlexibleSpace()
                .AddButton(app.Paid ? Domain.L.T("intercom.btn_rest") : Domain.L.T("intercom.btn_work"), Ui.A(() => { app.Paid = !app.Paid; Show(); }))
                .Tip(app.Paid ? Domain.L.T("intercom.tip_rest") : Domain.L.T("intercom.tip_work"))
                .AddButton(Domain.L.T("intercom.btn_train"), Ui.A(() => Training.Choose(app)))
                .AddButton(Domain.L.T("intercom.btn_fire"), Ui.A(() => ConfirmFire(app)))
                .End();
        }
        Table.EndItem(b);
    }

    private static string ShortShift(string shift) => shift == Shift.Night
        ? Domain.L.T("shift.short.night")
        : Domain.L.T("shift.short.morning");

    /// <summary>展开后的流程列表：每项选流程和时间，↑↓ 调整先后，× 去掉；熟练度按各自的流程算。</summary>
    private static void DutyRows(CustomUIBuilder b, Apprentice app)
    {
        var st = Automation.State;
        var schedules = AutoMod.Domain.Rules.Defs.PickOrder(st.Rules);
        var names = schedules.Select(x => Table.Fit(x.Name, 170, 13, out _)).ToList();
        for (int i = 0; i < app.Duties.Count; i++)
        {
            var d = app.Duties[i];
            int at = i;
            var p = Session.ProficiencyOf(app, d.ScheduleId);
            int curSched = Math.Max(0, schedules.FindIndex(x => x.Id == d.ScheduleId));
            Table.BeginSecondLine(b);
            b.AddLabel(Tone.Small($"{i + 1}.")).Width(24)
                .AddDropdown(Ui.L(names), curSched, Ui.AI(k => { d.ScheduleId = schedules[k].Id; Session.Changed(); Show(); })).Width(200);
            if (curSched < schedules.Count) b.Tip(schedules[curSched].Name);
            b.AddDropdown(Ui.L(new[] { ShortShift(Shift.Morning), ShortShift(Shift.Night) }), d.Shift == Shift.Night ? 1 : 0, Ui.AI(k => { d.Shift = k == 1 ? Shift.Night : Shift.Morning; Show(); })).Width(Domain.L.Int("layout.intercom.shift_width", 80))
                .Tip(Domain.L.T("intercom.duty_shift_tip"));
            Table.Cell(b, p == null ? Domain.L.T("intercom.prof_empty") : Domain.L.T("intercom.prof_score", p.Value.score), Domain.L.Int("layout.intercom.prof_width", 90),
                tip: p == null ? null : Domain.L.T("intercom.prof_tip", Proficiency.Floor(app.Level), p.Value.effective, p.Value.mult.ToString("0.00")));
            b.AddFlexibleSpace()
                .AddButton("↑", Ui.A(() => MoveDuty(app, at, -1))).Width(30)
                .AddButton("↓", Ui.A(() => MoveDuty(app, at, +1))).Width(30)
                .AddButton("×", Ui.A(() => { app.Duties.RemoveAt(at); Session.Changed(); Show(); })).Width(30)
                .End();
        }
        Table.BeginSecondLine(b);
        b.AddButton(Domain.L.T("intercom.btn_add_duty"), Ui.A(() =>
        {
            if (schedules.Count == 0) { Toast.Show(Domain.L.T("intercom.no_schedules_toast")); return; }
            app.Duties.Add(app.NewDuty(schedules.Select(x => x.Id).ToList()));
            Session.Changed();
            Show();
        })).Tip(Domain.L.T("intercom.tip_add_duty"));
        b.End();
    }

    private static void MoveDuty(Apprentice app, int from, int dir)
    {
        int to = from + dir;
        if (to < 0 || to >= app.Duties.Count) return;
        var d = app.Duties[from];
        app.Duties.RemoveAt(from);
        app.Duties.Insert(to, d);
        Show();
    }

    /// <summary>在学徒列表里上移 / 下移一位（执行和发工资的先后）。</summary>
    private static void Move(Apprentice app, int dir)
    {
        var list = Automation.State.Apprentices;
        int from = list.IndexOf(app), to = from + dir;
        if (from < 0 || to < 0 || to >= list.Count) return;
        list.RemoveAt(from);
        list.Insert(to, app);
        Show();
    }

    private static void ConfirmFire(Apprentice app) =>
        Pickers.Confirm(Domain.L.T("intercom.confirm_fire_title", app.Name), app.Training != null ? new[] { Domain.L.T("intercom.confirm_fire_note") } : Array.Empty<string>(),
            (Domain.L.T("intercom.btn_fire"), () => { Automation.State.Apprentices.Remove(app); Show(); }),
            (Domain.L.T("ui.cancel"), () => { }));

    // ---------- 报告 ----------

    private static void BuildReports(CustomUIBuilder b)
    {
        var st = Automation.State;
        int today = StoreStation.GetDayCounter();
        b.BeginRow(8).AddLabel(Domain.L.T("intercom.wages_last_7_days", Tone.Money(st.WagesSince(today - 6)))).End();
        int np = st.NeedsPlayer.Count;
        if (np > 0)
            b.BeginRow(8)
                .AddLabel(Tone.Small(Tone.Color(Domain.L.T("intercom.needs_player_warning", np), Tone.Warn)))
                .Tip(string.Join("\n", st.NeedsPlayer.Select(k => k.Replace("|", " → "))))
                .AddFlexibleSpace()
                .AddButton(Domain.L.T("intercom.btn_retry_player"), Ui.A(() => { Automation.ClearNeedsPlayer(); Show("reports"); }))
                .End();
        if (st.Reports.Count == 0) { b.AddLabel(Tone.Dim(Domain.L.T("intercom.no_reports"))); return; }
        float doneColW = Domain.L.Float("layout.intercom.col_done_width", 80f);
        float statusW = Domain.L.Float("layout.intercom.status_width", 160f);
        Table.Header(b, 0, (Domain.L.T("intercom.col_done"), doneColW), (Domain.L.T("intercom.col_ap"), 120), (Domain.L.T("intercom.col_report_wage"), 130), (Domain.L.T("intercom.col_apprentice"), 170));
        int idx = 0;
        foreach (var r in st.Reports.AsEnumerable().Reverse().Take(8))
        {
            var rep = r;
            var wage = r.Shift == Shift.Night ? st.Wages.FirstOrDefault(w => w.Day == r.Day) : null;
            // 新出现的问题排在前面；同一个问题每个同类班次都出现的，注明连续几天
            var issues = r.Entries.Where(e => !e.Ok).Select(e => (e, days: Streak(st.Reports, r, e))).OrderBy(t => t.days).ToList();
            int bad = issues.Count(t => !t.e.Deferred && t.e.Code != AutoMod.Domain.Reports.FailCode.Many);
            string status = bad > 0 ? Table.Status(Domain.L.T("intercom.status_issues", bad), Tone.Bad)
                : r.Deferred > 0 ? Table.Status(Domain.L.T("intercom.status_deferred", r.Deferred), Tone.Warn)
                : Table.Status(Domain.L.T("intercom.status_done"), Tone.Ok);
            Table.BeginItem(b, idx, false);
            b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
            b.AddLabel(Tone.Bold(ReportWindow.Title(r))).AddFlexibleSpace().AddLabel(status).Width(statusW);
            if (issues.Count > 0)
                b.Tip(string.Join("\n", issues.Take(6).Select(t => Domain.L.T("text.pair", t.e.Rule, t.e.Reason) + (t.days > 1 ? Domain.L.T("intercom.streak_days", t.days) : ""))));
            b.End();
            Table.BeginSecondLine(b);
            Table.Cell(b, Domain.L.N("intercom.done_items", r.Done), doneColW);
            Table.Cell(b, r.Groups.Count == 0 ? "—" : $"{r.Groups.Sum(g => g.ApUsed):0} / {r.ApAvailable:0}", 120);
            Table.Cell(b, wage == null ? "—" : $"{wage.Amount}" + (wage.Strikers > 0 ? Domain.L.T("intercom.strikers", wage.Strikers) : ""), 130);
            Table.Cell(b, r.Groups.Count == 0 ? Domain.L.T("intercom.none") : Domain.L.Join("list", r.Groups.Select(g => g.Workers).Where(w => w != "").Distinct()), 170);
            b.AddFlexibleSpace().AddButton(Domain.L.T("intercom.btn_details"), Ui.A(() => ReportWindow.Show(rep))).Width(Domain.L.Int("layout.btn_more_width", 95)).End();
            Table.EndItem(b);
            idx++;
        }
    }

    /// <summary>这个问题连续出现在几个同类班次里（含本班）：比较任务名和原因（数字不算）。</summary>
    private static int Streak(List<AutoMod.Domain.Reports.ShiftReport> all, AutoMod.Domain.Reports.ShiftReport r, AutoMod.Domain.Reports.ReportEntry e)
    {
        static string Key(AutoMod.Domain.Reports.ReportEntry x) => x.Rule + "|" + System.Text.RegularExpressions.Regex.Replace(x.Reason ?? "", @"\d+", "N");
        string key = Key(e);
        int days = 1;
        for (int i = all.IndexOf(r) - 1; i >= 0; i--)
        {
            if (all[i].Shift != r.Shift) continue;
            if (!all[i].Entries.Any(x => !x.Ok && Key(x) == key)) break;
            days++;
        }
        return days;
    }
}
