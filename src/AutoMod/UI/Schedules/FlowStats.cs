using System.Collections.Generic;
using System.Linq;
using AutoMod.App;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>
/// 流程的统计：把保存着的班次报告里这个流程的部分加起来（报告按流程名记）：执行了几个班、完成件数、AP，
/// 以及每个任务的完成件数、AP 和最常见的问题。
/// </summary>
public static class FlowStats
{
    private const string Id = "am_flowstats";

    public static void Show(Schedule s)
    {
        var reports = Automation.State.Reports.Where(r => !r.DryRun && r.Groups.Any(g => g.Schedule == s.Name)).ToList();
        var b = Ui.Window(Id, Domain.L.T("schedule.stats_title", s.Name), 640, 520);
        if (reports.Count == 0)
        {
            b.AddLabel(Tone.Dim(Domain.L.T("schedule.stats_empty")));
            b.Show();
            return;
        }
        var groups = reports.SelectMany(r => r.Groups.Where(g => g.Schedule == s.Name)).ToList(); // 几名学徒执行同一流程时每人一组
        var entries = reports.SelectMany(r => r.Entries.Where(e => e.Ws == s.Name)).ToList();
        int done = entries.Where(e => e.Ok).Sum(e => e.Units());
        double used = groups.Sum(g => g.ApUsed);
        string from = Domain.L.T("schedule.day_shift", reports.First().Day, Shift.DisplayName(reports.First().Shift));
        string to = Domain.L.T("schedule.day_shift", reports.Last().Day, Shift.DisplayName(reports.Last().Shift));
        b.AddLabel(Tone.Dim(Domain.L.T("schedule.stats_range", reports.Count, from, to)));
        b.AddLabel(Domain.L.N("schedule.stats_summary", done, (used / reports.Count).ToString("0"), entries.Count(e => !e.Ok && e.Code != FailCode.Many)));
        b.AddSpace(4);

        b.BeginScroll(360);
        Table.Header(b, 0, (Domain.L.T("schedule.col_rule"), 150), (Domain.L.T("schedule.col_done"), 70), (Domain.L.T("schedule.col_ap"), 70), (Domain.L.T("schedule.col_top_issue"), 300));
        var names = s.Rules.Select(r => r.Name).Concat(entries.Select(e => e.Rule)).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        for (int i = 0; i < names.Count; i++)
        {
            var mine = entries.Where(e => e.Rule == names[i]).ToList();
            var issues = mine.Where(e => !e.Ok && e.Code != FailCode.Many).GroupBy(e => System.Text.RegularExpressions.Regex.Replace(e.Reason ?? "", @"\d+", "N"))
                .OrderByDescending(g => g.Count()).ToList();
            string top = issues.Count == 0 ? "—" : Domain.L.N("schedule.stats_issue_count", issues[0].Count(), issues[0].First().Reason);
            Table.BeginItem(b, i, false);
            b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
            Table.Cell(b, names[i], 150, Tone.Bold, 16);
            Table.Cell(b, Domain.L.N("schedule.stats_done_items", mine.Where(e => e.Ok).Sum(e => e.Units())), 70);
            Table.Cell(b, $"{mine.Where(e => e.Ok).Sum(e => e.Cost):0.#}", 70);
            Table.Cell(b, top, 300, issues.Count == 0 ? Tone.Dim : t => Tone.Color(t, Tone.Warn),
                tip: issues.Count > 1 ? string.Join("\n", issues.Skip(1).Take(5).Select(g => Domain.L.N("schedule.stats_issue_count", g.Count(), g.First().Reason))) : null);
            b.AddFlexibleSpace().End();
            Table.EndItem(b);
        }
        b.End();
        b.Show();
    }
}
