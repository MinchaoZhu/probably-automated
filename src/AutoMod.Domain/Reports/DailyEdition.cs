using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AutoMod.Domain.Reports;

/// <summary>
/// 工作日报的一期数据：合并昨夜晚班与今晨早班的报告、昨夜工资与罢工情况。
/// 纯数据结构，支持 JSON 往返，不依赖游戏。
/// </summary>
public class DailyEdition
{
    public int Day { get; set; }
    public ShiftReport Night { get; set; }
    public ShiftReport Morning { get; set; }
    public int Wage { get; set; }
    public int Strikers { get; set; }

    /// <summary>两班完成件数之和。</summary>
    [JsonIgnore]
    public int Done => (Night?.Done ?? 0) + (Morning?.Done ?? 0);

    /// <summary>
    /// 一名学徒一天的 AP 是晚班 + 次日早班共用一份（晚班用剩的留给早班）。
    /// 起点 = 晚班报告里的 Workers[名字][0]（没有则用早班的）；
    /// 剩余 = 早班报告里的 Workers[名字][1]（早班没有他则用晚班的）；
    /// 用掉 = 起点 − 剩余；总和得到 ApUsed，起点之和得到 ApTotal。
    /// </summary>
    private (int Used, int Total) CalculateAp()
    {
        var names = new HashSet<string>();
        if (Night?.Workers != null)
        {
            foreach (var k in Night.Workers.Keys) names.Add(k);
        }
        if (Morning?.Workers != null)
        {
            foreach (var k in Morning.Workers.Keys) names.Add(k);
        }

        int totalUsed = 0;
        int totalStart = 0;
        foreach (var name in names)
        {
            int start = 0;
            if (Night?.Workers != null && Night.Workers.TryGetValue(name, out var nw) && nw != null && nw.Length > 0)
                start = nw[0];
            else if (Morning?.Workers != null && Morning.Workers.TryGetValue(name, out var mw) && mw != null && mw.Length > 0)
                start = mw[0];

            int rem = 0;
            if (Morning?.Workers != null && Morning.Workers.TryGetValue(name, out var mw2) && mw2 != null && mw2.Length > 1)
                rem = mw2[1];
            else if (Night?.Workers != null && Night.Workers.TryGetValue(name, out var nw2) && nw2 != null && nw2.Length > 1)
                rem = nw2[1];

            int used = Math.Max(0, start - rem);
            totalUsed += used;
            totalStart += start;
        }
        return (totalUsed, totalStart);
    }

    [JsonIgnore]
    public int ApUsed => CalculateAp().Used;

    [JsonIgnore]
    public int ApTotal => CalculateAp().Total;

    /// <summary>按流程名汇总的列表。顺序：先晚班里出现的流程，再补早班里新出现的。</summary>
    [JsonIgnore]
    public List<DailyFlowSummary> Flows
    {
        get
        {
            var flowNames = new List<string>();
            void Collect(ShiftReport r)
            {
                if (r == null) return;
                if (r.Groups != null)
                {
                    foreach (var g in r.Groups)
                    {
                        if (!string.IsNullOrEmpty(g.Schedule) && !flowNames.Contains(g.Schedule))
                            flowNames.Add(g.Schedule);
                    }
                }
                if (r.Entries != null)
                {
                    foreach (var e in r.Entries)
                    {
                        string name = !string.IsNullOrEmpty(e.Ws) ? e.Ws : e.Flow;
                        if (!string.IsNullOrEmpty(name) && !flowNames.Contains(name))
                            flowNames.Add(name);
                    }
                }
            }
            Collect(Night);
            Collect(Morning);

            var list = new List<DailyFlowSummary>();
            foreach (var name in flowNames)
            {
                int nightDone = Night?.Entries?.Where(e => (e.Ws == name || (string.IsNullOrEmpty(e.Ws) && e.Flow == name)) && e.Ok).Sum(e => e.Units()) ?? 0;
                int morningDone = Morning?.Entries?.Where(e => (e.Ws == name || (string.IsNullOrEmpty(e.Ws) && e.Flow == name)) && e.Ok).Sum(e => e.Units()) ?? 0;

                double apNight = Night?.Groups?.Where(g => g.Schedule == name).Sum(g => g.ApUsed) ?? 0;
                double apMorning = Morning?.Groups?.Where(g => g.Schedule == name).Sum(g => g.ApUsed) ?? 0;

                int pNight = Night?.Entries?.Count(e => (e.Ws == name || (string.IsNullOrEmpty(e.Ws) && e.Flow == name)) && !e.Ok && !e.Deferred && e.Code != FailCode.Many) ?? 0;
                int pMorning = Morning?.Entries?.Count(e => (e.Ws == name || (string.IsNullOrEmpty(e.Ws) && e.Flow == name)) && !e.Ok && !e.Deferred && e.Code != FailCode.Many) ?? 0;

                list.Add(new DailyFlowSummary
                {
                    Name = name,
                    NightDone = nightDone,
                    MorningDone = morningDone,
                    ApUsed = apNight + apMorning,
                    Problems = pNight + pMorning,
                });
            }
            return list;
        }
    }

    /// <summary>
    /// 去重后的问题列表（条目：流程名、任务名、原因文字、出现几次）。
    /// 取两班里 !Ok && !Deferred && Code != FailCode.Many 的 ReportEntry，
    /// 按（流程、任务、原因文字去掉数字后）合并计数；按出现次数从多到少排。
    /// </summary>
    [JsonIgnore]
    public List<DailyProblem> Problems
    {
        get
        {
            var allEntries = new List<ReportEntry>();
            if (Night?.Entries != null) allEntries.AddRange(Night.Entries);
            if (Morning?.Entries != null) allEntries.AddRange(Morning.Entries);

            var problemEntries = allEntries.Where(e => !e.Ok && !e.Deferred && e.Code != FailCode.Many);
            var grouped = new List<DailyProblem>();
            var groupKeys = new Dictionary<(string Flow, string Rule, string CleanReason), DailyProblem>();

            foreach (var e in problemEntries)
            {
                string flow = !string.IsNullOrEmpty(e.Ws) ? e.Ws : (e.Flow ?? "");
                string rule = e.Rule ?? "";
                string reason = !string.IsNullOrWhiteSpace(e.Reason) ? e.Reason : (!string.IsNullOrWhiteSpace(e.Text) ? e.Text : FailCode.DisplayName(e.Code));
                string cleanReason = Regex.Replace(reason, @"\d+", "").Trim();

                var key = (Flow: flow, Rule: rule, CleanReason: cleanReason);
                int count = Math.Max(1, e.Count);

                if (groupKeys.TryGetValue(key, out var existing))
                {
                    existing.Count += count;
                }
                else
                {
                    var p = new DailyProblem
                    {
                        Flow = flow,
                        Rule = rule,
                        Reason = reason,
                        Count = count,
                    };
                    groupKeys[key] = p;
                    grouped.Add(p);
                }
            }

            return grouped.OrderByDescending(p => p.Count).ToList();
        }
    }

    /// <summary>总问题数（所有问题出现次数之和）。</summary>
    [JsonIgnore]
    public int ProblemCount => Problems.Sum(p => p.Count);

    /// <summary>两班因 AP 不足留到下次的件数之和。</summary>
    [JsonIgnore]
    public int Deferred => (Night?.Deferred ?? 0) + (Morning?.Deferred ?? 0);

    /// <summary>两班里的 ReportGroup.Growth 去重后的行。</summary>
    [JsonIgnore]
    public List<string> Growth
    {
        get
        {
            var list = new List<string>();
            void AddGrowth(ShiftReport r)
            {
                if (r?.Groups == null) return;
                foreach (var g in r.Groups)
                {
                    if (g.Growth == null) continue;
                    foreach (var line in g.Growth)
                    {
                        if (!string.IsNullOrEmpty(line) && !list.Contains(line))
                            list.Add(line);
                    }
                }
            }
            AddGrowth(Night);
            AddGrowth(Morning);
            return list;
        }
    }
}

/// <summary>工作日报里的单项问题汇总。</summary>
public class DailyProblem
{
    public string Flow { get; set; } = "";
    public string Rule { get; set; } = "";
    public string Reason { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>工作日报里的流程汇总条目。</summary>
public class DailyFlowSummary
{
    public string Name { get; set; } = "";
    public int NightDone { get; set; }
    public int MorningDone { get; set; }
    public double ApUsed { get; set; }
    public int Problems { get; set; }
}
