using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;

namespace AutoMod.Domain.Reports;

/// <summary>失败原因分类（体检、状态灯、汇总用）。文字说明照旧写在 Reason 里。</summary>
public static class FailCode
{
    public const string NoSource = "no_source";     // 来源里没有可用的东西
    public const string NoTool = "no_tool";         // 缺工具 / 工具用不上
    public const string DestFull = "dest_full";     // 去向放不下
    public const string Many = "many";            // 一次处理很多件（提示，不算没做成）
    public const string NotAllowed = "not_allowed"; // 游戏不允许
    public const string NoEffect = "no_effect";     // 游戏允许作用，但作用后什么都没变
    public const string Simulated = "simulated";    // 预演：效果未知的操作已对这件做过一次，不再重复
    public const string NeedsPlayer = "needs_player"; // 作用时游戏要打开界面让玩家看或填（检视、改名、分析……），学徒做不了
    public const string NoAp = "no_ap";             // 点数不够（AP 耗尽，未处理）
    public const string NoMembers = "no_members";   // 工位没有容器
    public const string Error = "error";

    public static string DisplayName(string c) => L.T("failcode." + (string.IsNullOrEmpty(c) ? "other" : c));
}

public class ReportEntry
{
    /// <summary>失败原因分类（FailCode），成功时为空。</summary>
    public string Code { get; set; }

    /// <summary>来自哪个流程（为空 = 手写规则）。</summary>
    public string Flow { get; set; }

    public string Ws { get; set; } = "";
    public string Rule { get; set; } = "";
    public string Item { get; set; } = "";
    public bool Ok { get; set; }
    public string Text { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>AP（保留 1 位小数）。</summary>
    public double Cost { get; set; }

    /// <summary>汇总条目代表的件数（“AP 耗尽，N 件未处理”；同样的事做了 N 件合成一条）；0 = 1 件。</summary>
    public int Count { get; set; }

    /// <summary>这条代表几件。</summary>
    public int Units() => Math.Max(1, Count);

    /// <summary>执行的学徒。</summary>
    public string Worker { get; set; } = "";

    /// <summary>因为点数不够而留到下个班次（不算失败）。</summary>
    public bool Deferred { get; set; }

    public int Xp { get; set; }
    public int LevelUps { get; set; }
}

public class ShiftReport
{
    public string Shift { get; set; } = "";
    public int Day { get; set; }
    public bool DryRun { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public int Rounds { get; set; }
    public List<ReportEntry> Entries { get; set; } = new();

    /// <summary>当班学徒：名字 → (班次开始 AP, 剩余 AP)。</summary>
    public Dictionary<string, int[]> Workers { get; set; } = new();

    /// <summary>每个执行组（同一排班的学徒）：已用 / 共有 AP。</summary>
    public List<ReportGroup> Groups { get; set; } = new();

    /// <summary>说明（例：没有当班学徒）。</summary>
    public string Note { get; set; } = "";

    /// <summary>由排班解释器生成的一行摘要；不为空时 Summary() 直接用它。</summary>
    public string SummaryOverride { get; set; }

    public int Done => Entries.Where(e => e.Ok).Sum(e => e.Units());
    public int Failed => Entries.Count(e => !e.Ok && !e.Deferred);
    /// <summary>AP 耗尽、留到下次的件数。</summary>
    public int Deferred => Entries.Where(e => e.Deferred && e.Code != FailCode.NotAllowed).Sum(e => Math.Max(1, e.Count));

    /// <summary>游戏暂时不允许、等条件满足再做的（汇总条目）。</summary>
    public bool HasWaiting => Entries.Any(e => e.Deferred && e.Code == FailCode.NotAllowed);
    /// <summary>本班可用的 AP：每名学徒按他第一组开始时剩下的算（一人执行几个流程时不重复计）。</summary>
    public double ApAvailable => Groups.GroupBy(g => g.Workers).Sum(x => x.First().ApTotal);

    public double TotalCost => Groups.Count > 0 ? Groups.Sum(g => g.ApUsed) : Entries.Where(e => e.Ok).Sum(e => e.Cost);

    /// <summary>一行摘要，写进游戏的夜间日志。</summary>
    public string Summary()
    {
        if (!string.IsNullOrEmpty(SummaryOverride)) return SummaryOverride;
        string who = Workers.Count > 0 ? L.Join("list", Workers.Keys) : L.T("report.apprentice");
        if (!string.IsNullOrEmpty(Note) && Entries.Count == 0) return L.T("report.shift_note", AutoMod.Domain.Workforce.Shift.DisplayName(Shift), Note);
        if (Entries.Count == 0) return L.T("report.shift_empty", who, AutoMod.Domain.Workforce.Shift.DisplayName(Shift));
        var byWs = Entries.Where(e => e.Ok).GroupBy(e => e.Ws).Select(g => $"{g.Key} {L.N("report.count_items", g.Sum(e => e.Units()))}");
        string s = L.T("report.shift_done", who, AutoMod.Domain.Workforce.Shift.DisplayName(Shift), Done, L.Join("list", byWs));
        if (Deferred > 0) s += L.N("report.shift_deferred", Deferred);
        if (Failed > 0) s += L.N("report.shift_failed", Failed);
        if (HasWaiting) s += L.T("report.shift_waiting");
        var ups = Entries.Where(e => e.LevelUps > 0).Select(e => e.Worker).Distinct().ToList();
        if (ups.Count > 0) s += L.T("report.shift_levelup", L.Join("list", ups));
        return s + L.T("text.period");
    }

    public string Detail()
    {
        var sb = new StringBuilder();
        string ap = Groups.Count > 0 ? "  " + Text.Ap(Groups.Sum(g => g.ApUsed), ApAvailable) : "";
        string mode = DryRun ? L.T("report.detail.dry_run") : L.T("report.detail.run");
        string day = L.T("report.detail.day", Day);
        string doneStr = L.N("report.detail.done", Done);
        string defStr = L.N("report.detail.deferred", Deferred);
        string failStr = L.N("report.detail.failed", Failed);
        sb.AppendLine($"== {mode} {AutoMod.Domain.Workforce.Shift.DisplayName(Shift)} {day} {Time:HH:mm:ss}  {doneStr}  {defStr}  {failStr}{ap}");
        if (!string.IsNullOrEmpty(Note)) sb.AppendLine("  " + Note);
        foreach (var g in Groups) sb.AppendLine(L.T("report.detail.group_line", g.Workers, g.Schedule, Text.Ap(g.ApUsed, g.ApTotal)));
        foreach (var g in Entries.GroupBy(e => e.Ws))
        {
            sb.AppendLine($"[{g.Key}]");
            foreach (var e in g)
            {
                string who = string.IsNullOrEmpty(e.Worker) ? "" : $"{e.Worker} ";
                string levelup = e.LevelUps > 0 ? L.T("report.detail.levelup") : "";
                string itemColon = e.Item == "" ? "" : L.T("text.pair_after", e.Item);
                string times = e.Count > 1 ? $" ×{e.Count}" : "";
                sb.AppendLine(e.Ok ? $"  ✓ {who}{itemColon}{e.Text}{times}  ({e.Rule}, {e.Cost:0.#} AP{levelup})"
                    : e.Deferred ? $"  … {itemColon}{e.Reason}  ({e.Rule})"
                    : $"  ✗ {itemColon}{e.Reason}  ({e.Rule})");
            }
        }
        return sb.ToString();
    }

    /// <summary>排班解释器的运行报告 → 班次报告（报告页、夜间日志用）。</summary>
    public static ShiftReport From(RunReport run, bool dryRun, IEnumerable<Apprentice> workers)
    {
        var r = new ShiftReport { Shift = run.Shift, Day = run.Day, DryRun = dryRun, Rounds = 1 };
        r.Entries.AddRange(run.Extra);
        foreach (var g in run.Groups)
        {
            r.Groups.Add(new ReportGroup { Schedule = g.ScheduleName, Workers = L.Join("list", g.Workers), ApUsed = g.ApUsed, ApTotal = g.ApTotal,
                Growth = g.Learned.Concat(g.LevelUps.Select(u => L.T("report.growth.prefix", u))).ToList(),
                Idle = g.Rules.Where(x => x.Done.Count == 0 && x.Issues.Count == 0 && x.Deferred == 0 && x.Called == null)
                    .Select(x => (x.Name, why: x.Pre == PreResult.Pass ? x.Idle : x.Pre == PreResult.Fail ? x.Why : null))
                    .Where(t => t.why != null).Select(t => L.T("fmt.colon", t.Name, t.why)).ToList() });
            foreach (var rule in g.Rules)
            {
                // 同样的事合成一条：去掉物品编号和括号里的状态后，物品和结果文字都相同的（“放入 营养果：移至 食品仓库 ×591”）
                var same = new Dictionary<string, ReportEntry>();
                foreach (var ev in rule.Items.Where(e => e.Outcome == Outcomes.Done))
                {
                    string item = $"{Catalog.Op(ev.Op).Name} {ev.Item}";
                    string key = Plain(item) + "｜" + NoUid(ev.Text);
                    if (same.TryGetValue(key, out var first))
                    {
                        first.Count = first.Units() + 1;
                        first.Cost += ev.Ap;
                        first.Item = Plain(item);
                        first.Text = NoUid(ev.Text);
                        continue;
                    }
                    var entry = new ReportEntry { Ok = true, Ws = g.ScheduleName, Rule = rule.Name, Worker = L.Join("list", g.Workers), Item = item, Text = ev.Text, Cost = ev.Ap };
                    same[key] = entry;
                    r.Entries.Add(entry);
                }
                foreach (var i in rule.Issues)
                    r.Entries.Add(new ReportEntry { Ok = false, Ws = g.ScheduleName, Rule = rule.Name, Item = i.Count > 1 ? L.N("report.count_items", i.Count) : "", Code = i.Code, Reason = i.Text,
                        Deferred = i.Code == FailCode.NotAllowed });
                if (rule.Deferred > 0)
                    r.Entries.Add(new ReportEntry { Ok = false, Deferred = true, Code = FailCode.NoAp, Ws = g.ScheduleName, Rule = rule.Name, Item = "", Count = rule.Deferred,
                        Reason = rule.Pre == PreResult.NotRun ? L.N("report.reason.notrun", rule.Deferred, rule.Why) : L.N("report.reason.no_ap", rule.Deferred) });
            }
        }
        foreach (var w in workers) r.Workers[w.Name] = new[] { w.DailyAp, w.ApLeft };
        r.Note = string.Join(" ", run.Notes);
        r.SummaryOverride = L.T("report.summary_override", AutoMod.Domain.Workforce.Shift.DisplayName(run.Shift), Text.RunSummary(run));
        return r;
    }

    private static readonly Regex Uid = new(@"#\d+", RegexOptions.Compiled);

    /// <summary>编号和紧跟着的状态括号（“#12（0ml，水质 0%）”）；名字里的括号在编号前面（“生肉（小）#1680”），不算。</summary>
    private static readonly Regex UidState = new(@"#\d+（[^）]*）", RegexOptions.Compiled);

    /// <summary>去掉物品编号（“营养果#1804” → “营养果”）。</summary>
    public static string NoUid(string s) => Uid.Replace(s ?? "", "");

    /// <summary>去掉物品编号和它后面的状态（“使用 水桶#12（0ml，水质 0%）” → “使用 水桶”，“生肉（小）#1680” → “生肉（小）”）。</summary>
    public static string Plain(string s) => NoUid(UidState.Replace(s ?? "", "")).Trim();
}

/// <summary>班次报告里的一个执行组。</summary>
public class ReportGroup
{
    public string Schedule { get; set; } = "";
    public string Workers { get; set; } = "";
    public double ApUsed { get; set; }
    public double ApTotal { get; set; }

    /// <summary>熟练度、等级的变化（“林豆子 熟练度 67 → 68”）。</summary>
    public List<string> Growth { get; set; } = new();

    /// <summary>这一班没做事的任务和原因（“加营养片：「缺营养的水瓶」当前为空”）。</summary>
    public List<string> Idle { get; set; } = new();
}
