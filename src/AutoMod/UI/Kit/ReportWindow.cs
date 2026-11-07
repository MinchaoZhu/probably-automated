using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Workforce;

namespace AutoMod.UI.Kit;

/// <summary>报告的一行：窗口和工作日报报纸共用同一份内容，各自按种类上色、排版。</summary>
public sealed class ReportLine
{
    public enum Kinds { Overview, Note, Section, Growth, Empty, RuleDone, RuleNone, Detail, Change, Warn, Bad, Idle, Extra }

    public Kinds Kind;
    public string Text;
    /// <summary>Section：右侧的学徒和 AP；RuleDone：件数和 AP。</summary>
    public string Aux;
}

/// <summary>
/// 班次报告窗口：标题是哪天哪班；一行概览；按排班分组，每个任务一行结果，下面是做了什么（同样的几件合并）。
/// 报告内容由 Lines 生成（工作日报报纸用同一份）；早班的报告不再自动弹出，随工作日报发放，也可以在对讲机的报告页点“详情”打开。
/// </summary>
public static class ReportWindow
{
    private const string Id = "am_report";

    public static string Title(ShiftReport r) => Domain.L.T("report.window.title", r.Day, Shift.DisplayName(r.Shift)) + (r.DryRun ? Domain.L.T("report.window.dry_run") : "");

    /// <summary>概览：完成件数、AP、留到下次的件数。</summary>
    public static string Overview(ShiftReport r)
    {
        var parts = new List<string> { Domain.L.N("report.window.done", r.Done) };
        if (r.Groups.Count > 0) parts.Add($"{r.Groups.Sum(g => g.ApUsed):0} / {r.ApAvailable:0} AP");
        if (r.Deferred > 0) parts.Add(Tone.Color(Domain.L.N("report.window.deferred", r.Deferred), Tone.Warn)); // 下一班从第 1 个任务重新开始
        int problems = r.Entries.Count(e => !e.Ok && !e.Deferred && e.Code != FailCode.Many);
        if (problems > 0) parts.Add(Tone.Color(Domain.L.N("report.window.problems", problems), Tone.Bad));
        return string.Join(" · ", parts);
    }

    /// <summary>报告正文（不含概览）：按排班分组，每个任务一块。</summary>
    public static List<ReportLine> Lines(ShiftReport r)
    {
        var lines = new List<ReportLine>();
        void Add(ReportLine.Kinds kind, string text, string aux = null) => lines.Add(new ReportLine { Kind = kind, Text = text, Aux = aux });
        if (!string.IsNullOrEmpty(r.Note) && r.Entries.Count == 0) Add(ReportLine.Kinds.Note, r.Note);
        var groups = r.Entries.GroupBy(e => e.Ws).ToList();
        foreach (var g in r.Groups)
        {
            Add(ReportLine.Kinds.Section, g.Schedule, $"{g.Workers} · {g.ApUsed:0} / {g.ApTotal:0} AP");
            foreach (var line in g.Growth) Add(ReportLine.Kinds.Growth, line);
            var entries = groups.FirstOrDefault(x => x.Key == g.Schedule)?.ToList() ?? new List<ReportEntry>();
            if (entries.Count == 0 && g.Idle.Count == 0) { Add(ReportLine.Kinds.Empty, Domain.L.T("report.window.empty")); continue; }
            foreach (var rule in entries.GroupBy(e => e.Rule))
                RuleBlock(lines, rule.Key, rule.ToList());
            // 没做事的任务写原因：“加营养片：「缺营养的水瓶」当前为空”
            foreach (var line in g.Idle) Add(ReportLine.Kinds.Idle, line);
        }
        // 不属于排班的条目（培训等）
        foreach (var e in r.Entries.Where(e => r.Groups.All(g => g.Schedule != e.Ws)))
            Add(ReportLine.Kinds.Extra, e.Ok ? Domain.L.T("text.pair", e.Item, e.Text) : e.Reason);
        return lines;
    }

    public static void Show(ShiftReport r)
    {
        var b = Ui.Window(Id, Domain.L.T("report.window.title_full", Title(r)), 600, 560);
        b.AddLabel(Overview(r));
        b.BeginScroll(420);
        foreach (var l in Lines(r))
        {
            switch (l.Kind)
            {
                case ReportLine.Kinds.Note: b.AddLabel(Tone.Dim(l.Text)); break;
                case ReportLine.Kinds.Section: b.Section(l.Text, l.Aux); break;
                case ReportLine.Kinds.Growth: b.AddLabel(Tone.Small(Tone.Color(l.Text, Tone.Ok))); break;
                case ReportLine.Kinds.Empty: b.AddLabel(Tone.Dim(l.Text)); break;
                case ReportLine.Kinds.RuleDone: b.AddLabel(Tone.Color("✓", Tone.Ok) + $" {l.Text}　" + Tone.Dim(l.Aux)); break;
                case ReportLine.Kinds.RuleNone: b.AddLabel(Tone.Dim("— " + l.Text)); break;
                case ReportLine.Kinds.Detail: b.AddLabel(Tone.Small("    " + l.Text)); break;
                case ReportLine.Kinds.Change: b.AddLabel(Tone.Small(Tone.Dim("        " + l.Text))); break;
                case ReportLine.Kinds.Warn: b.AddLabel(Tone.Small(Tone.Color("    " + l.Text, Tone.Warn))); break;
                case ReportLine.Kinds.Bad: b.AddLabel(Tone.Small(Tone.Color("    " + l.Text, Tone.Bad))); break;
                case ReportLine.Kinds.Idle: b.AddLabel(Tone.Small(Tone.Dim(l.Text))); break;
                default: b.AddLabel(Tone.Small(l.Text)); break;
            }
        }
        b.End();
        b.BeginRow(8).AddFlexibleSpace().AddButton(Domain.L.T("ui.close"), Ui.A(() => Ui.Close(Id))).End();
        b.Show();
    }

    /// <summary>物品的说法去掉编号和它后面的状态，再去掉开头的动作名（任务名已经说了）：“使用 桶装水#12（0ml）” → “桶装水”。</summary>
    private static string Plain(string item)
    {
        var s = ShiftReport.Plain(item);
        int sp = s.IndexOf(' ');
        return sp > 0 && sp <= 4 ? s[(sp + 1)..] : s;
    }

    private static void RuleBlock(List<ReportLine> lines, string rule, List<ReportEntry> entries)
    {
        void Add(ReportLine.Kinds kind, string text, string aux = null) => lines.Add(new ReportLine { Kind = kind, Text = text, Aux = aux });
        var done = entries.Where(e => e.Ok).ToList();
        double ap = done.Sum(e => e.Cost);
        int n = done.Sum(e => e.Units());
        if (n > 0) Add(ReportLine.Kinds.RuleDone, rule, Domain.L.N("report.window.group_stats", n, ap.ToString("0.#")));
        else Add(ReportLine.Kinds.RuleNone, rule);
        // 同样的事合并：去掉物品编号后文字相同的算一种
        foreach (var same in done.GroupBy(e => Plain(e.Item) + "｜" + ShiftReport.NoUid(e.Text)))
        {
            var e = same.First();
            string what = Plain(e.Item);
            string text = e.Text ?? "";
            int colon = text.IndexOfAny(new[] { '：', ':' });
            string action = colon >= 0 ? text[..colon] : text, change = colon >= 0 ? text[(colon + 1)..] : "";
            int units = same.Sum(x => x.Units());
            string times = units > 1 ? $" ×{units}" : "";
            action = Regex.Replace(ShiftReport.NoUid(action), "（[^）]*）", "");
            Add(ReportLine.Kinds.Detail, action.Contains(what) ? $"{action}{times}" : $"{what}{times}　{action}");
            if (change != "") Add(ReportLine.Kinds.Change, change.Replace("，", " · ").Replace(", ", " · "));
        }
        foreach (var e in entries.Where(e => !e.Ok))
            Add(e.Deferred ? ReportLine.Kinds.Warn : ReportLine.Kinds.Bad, e.Reason);
    }
}
