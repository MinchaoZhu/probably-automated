using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;
using Newtonsoft.Json;

/// <summary>
/// 0.8 中文快照比对：
/// 中文界面全部文字的输出和存下的 0.8 快照逐字比对，确保多语言改造没有改动中文。
/// </summary>
internal static class SnapshotTest
{
    private static string SnapshotPath => Path.Combine(FindRoot(), "tests", "AutoMod.Domain.Tests", "snapshot_zh.json");

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ProbablyStolenMod.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }

    public static Dictionary<string, string> CaptureCurrent()
    {
        AutoMod.Domain.L.Use("zh", LocTests.LoadTable("zh"), LocTests.LoadTable("en"));
        var data = new Dictionary<string, string>();

        void Put(string key, string val) => data[key] = val ?? "";

        // 1. 全部示例排班
        var d = new RulesData();
        Examples.BatteryRotation(d);
        foreach (var spec in ProfessionExamples.All) ProfessionExamples.Build(d, spec.Id);

        for (int si = 0; si < d.Schedules.Count; si++)
        {
            var s = d.Schedules[si];
            string sp = $"sched.{si}.{s.Name}";
            Put($"{sp}.name", s.Name);
            foreach (var o in d.Objects.Where(o => o.Owner == s.Id))
            {
                Put($"{sp}.obj.{o.Name}.name", o.Name);
                Put($"{sp}.obj.{o.Name}.text", Text.Object(o));
            }
            foreach (var p in d.Properties)
            {
                Put($"prop.{p.Name}.name", p.Name);
                Put($"prop.{p.Name}.text", Text.Property(d, p));
            }
            foreach (var x in d.Selectors.Where(x => x.Owner == s.Id))
            {
                Put($"{sp}.sel.{x.Name}.name", x.Name);
                Put($"{sp}.sel.{x.Name}.full", Text.Selector(d, x));
                Put($"{sp}.sel.{x.Name}.short", Text.SelectorShort(d, x));
            }
            foreach (var r in s.Rules)
            {
                Put($"{sp}.rule.{r.Name}.rule", Text.Rule(d, s, r));
                for (int i = 0; i < r.Pre.Count; i++)
                {
                    Put($"{sp}.rule.{r.Name}.pre.{i}", Text.Pre(d, s, r.Pre[i]));
                    Put($"{sp}.rule.{r.Name}.prenot.{i}", Text.PreNot(d, s, r.Pre[i]));
                }
                for (int i = 0; i < r.Actions.Count; i++)
                {
                    Put($"{sp}.rule.{r.Name}.act.{i}", Text.Action(d, s, r.Actions[i]));
                }
                var cardLines = Card.Sentence(d, s, r).Select(line => string.Concat(line.Select(seg => seg.Text)));
                Put($"{sp}.rule.{r.Name}.card", string.Join(" / ", cardLines));
            }
        }

        // 2. Vocab 与 Catalog
        foreach (var v in Vocab.SysVars) Put($"vocab.sys.{v.id}", Vocab.SysName(v.id));
        foreach (var a in Vocab.AggFns) Put($"vocab.agg.{a.id}", Vocab.AggName(a.id));
        foreach (var op in Vocab.CmpOps) Put($"vocab.cmp.{op}", Vocab.OpText(op));
        foreach (var op in Vocab.ArithOps) Put($"vocab.arith.{op}", Vocab.OpText(op));

        foreach (var t in Catalog.Traits) Put($"catalog.trait.{t.Id}", t.Name);
        foreach (var p in Catalog.Props) Put($"catalog.prop.{p.Id}", p.Name);
        foreach (var ps in Catalog.Presets) Put($"catalog.preset.{ps.Id}", ps.Name);
        foreach (var op in Catalog.Ops) Put($"catalog.op.{op.Id}", op.Name);

        // 3. Text 常用方法
        Put("text.ap.1", Text.Ap(11, 25));
        Put("text.ap.2", Text.Ap(0, 40));
        Put("text.members.short", Text.Members(new[] { "箱子1", "箱子2" }, 2));
        Put("text.members.long", Text.Members(new[] { "高级涡轮增压器#1", "高级涡轮增压器#2", "清洁机器人#1", "水分收集器#1", "水分收集器#2", "风扇" }, 2));

        Put("text.runsummary.0", Text.RunSummary(FakeRun(0, 0, 0, Array.Empty<string>())));
        Put("text.runsummary.1", Text.RunSummary(FakeRun(1, 0, 0, new[] { "小灰" })));
        Put("text.runsummary.many", Text.RunSummary(FakeRun(31, 6, 2, new[] { "小灰", "豆子" })));

        Put("text.outcome.pass", Text.Outcome(new RuleRunReport { Pre = PreResult.Pass }));
        Put("text.outcome.fail", Text.Outcome(new RuleRunReport { Pre = PreResult.Fail, Why = "没有死鼠" }));
        Put("text.outcome.notrun", Text.Outcome(new RuleRunReport { Pre = PreResult.NotRun, Why = "AP 不足（前面的任务已用 11 / 共 11 AP）" }));
        Put("text.outcome.disabled", Text.Outcome(new RuleRunReport { Pre = PreResult.Disabled }));
        Put("text.outcome.broken", Text.Outcome(new RuleRunReport { Pre = PreResult.Broken }));

        Put("text.merge.1", Text.MergeSteps(new[] { "水质 3 → 2", "水质 2 → 1" }));
        Put("text.merge.2", Text.MergeSteps(new[] { "售水机：现金 +10", "售水机：现金 +20" }));

        // 4. ShiftReport 摘要
        var rep0 = new ShiftReport { Shift = Shift.Morning, Day = 1 };
        Put("shiftreport.empty", rep0.Summary());

        var rep1 = new ShiftReport
        {
            Shift = Shift.Morning,
            Day = 2,
            Workers = { ["小灰"] = new[] { 25, 10 } },
            Entries =
            {
                new ReportEntry { Ws = "自动换电", Rule = "换电池", Ok = true, Item = "电池", Cost = 2 },
                new ReportEntry { Ws = "自动换电", Rule = "充电池", Ok = false, Item = "电池", Reason = "放不下" },
            }
        };
        Put("shiftreport.sample", rep1.Summary());

        // 5. 手册文本（从源码提取 7 页中文字符串；若已多语言化则按表取出）
        string handbookSrc = Path.Combine(FindRoot(), "src", "AutoMod", "UI", "Guide", "HandbookPages.cs");
        if (File.Exists(handbookSrc))
        {
            var matches = Regex.Matches(File.ReadAllText(handbookSrc), @"""([^""\\]|\\.)*""");
            int idx = 0;
            foreach (Match m in matches)
            {
                string val = Regex.Unescape(m.Value.Trim('"'));
                if (val.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                {
                    Put($"handbook.raw.{idx++}", val);
                }
            }
            if (idx == 0)
            {
                // 手册已多语言化（通过 Domain.L.T 取词）
                string[] fixedKeys =
                {
                    "handbook.title",
                    "handbook.diagram.recharger",
                    "handbook.diagram.recharger_note",
                    "handbook.diagram.machines",
                    "handbook.diagram.machines_note",
                    "handbook.diagram.storage",
                    "handbook.diagram.storage_note",
                    "handbook.diagram.arrow_full",
                    "handbook.diagram.arrow_empty",
                    "handbook.diagram.arrow_charge",
                    "handbook.p1.banner_quickstart",
                };
                foreach (var k in fixedKeys) Put($"handbook.raw.{idx++}", AutoMod.Domain.L.T(k));

                // p1 快速上手步骤拆行
                string steps = AutoMod.Domain.L.T("handbook.p1.steps_quickstart");
                var stepLines = steps.Split('\n');
                for (int i = 0; i < stepLines.Length; i++)
                    Put($"handbook.raw.{idx++}", stepLines[i] + (i < stepLines.Length - 1 ? "\n" : ""));

                string[] restKeys =
                {
                    "handbook.p1.tip_payroll",
                    "handbook.p1.banner_apprentices",
                    "handbook.p1.text_ap",
                    "handbook.p1.title_multi_sched",
                    "handbook.p1.text_multi_sched",
                    "handbook.p2.banner",
                    "handbook.p2.title_prof",
                    "handbook.p2.text_prof",
                    "handbook.p2.title_switch",
                    "handbook.p2.text_switch",
                    "handbook.p2.title_level",
                    "handbook.p2.text_level",
                    "handbook.p2.tip",
                    "handbook.p3.banner",
                    "handbook.p3.title_scope",
                    "handbook.p3.text_scope",
                    "handbook.p3.title_filter",
                    "handbook.p3.text_filter",
                    "handbook.p3.tip_name",
                    "handbook.p3.title_range",
                    "handbook.p3.text_range",
                    "handbook.p3.steps_pick",
                    "handbook.p3.title_param",
                    "handbook.p3.text_param",
                    "handbook.p3.steps_conds",
                    "handbook.p4.banner",
                    "handbook.p4.title_pre",
                    "handbook.p4.text_pre",
                    "handbook.p4.title_move",
                    "handbook.p4.text_move",
                    "handbook.p4.title_swap",
                    "handbook.p4.text_swap",
                    "handbook.p4.title_use",
                    "handbook.p4.text_use",
                    "handbook.p4.title_press",
                    "handbook.p4.text_press",
                    "handbook.p4.tip",
                    "handbook.p5.banner_swap",
                    "handbook.p5.steps_swap",
                    "handbook.p5.banner_order",
                    "handbook.p5.title_seq",
                    "handbook.p5.text_seq",
                    "handbook.p6.banner",
                    "handbook.p6.title_battery",
                    "handbook.p6.text_battery",
                    "handbook.p6.title_organize",
                    "handbook.p6.text_organize",
                    "handbook.p6.title_hydro",
                    "handbook.p6.text_hydro",
                    "handbook.p6.title_ras",
                    "handbook.p6.text_ras",
                    "handbook.p6.title_rat",
                    "handbook.p6.text_rat",
                    "handbook.p6.title_print",
                    "handbook.p6.text_print",
                    "handbook.p6.title_vending",
                    "handbook.p6.text_vending",
                    "handbook.p6.tip",
                    "handbook.p7.banner",
                    "handbook.p7.title_sim",
                    "handbook.p7.text_sim",
                    "handbook.p7.title_report",
                    "handbook.p7.text_report",
                    "handbook.p7.tip",
                };
                foreach (var k in restKeys) Put($"handbook.raw.{idx++}", AutoMod.Domain.L.T(k));
            }
        }

        return data;
    }

    private static RunReport FakeRun(int done, int deferred, int issues, string[] ups)
    {
        var rr = new RuleRunReport { Done = { ["move"] = done } };
        for (int i = 0; i < issues; i++) rr.Issues.Add(new RunIssue { Code = "noSource", Text = "x" });
        var g = new GroupRunReport { Deferred = deferred, Rules = { rr } };
        g.LevelUps.AddRange(ups);
        return new RunReport { Groups = { g } };
    }

    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("多语言·0.8 中文快照逐字一致", () =>
        {
            string path = SnapshotPath;
            var current = CaptureCurrent();
            if (!File.Exists(path))
            {
                // 首次生成快照文件
                File.WriteAllText(path, JsonConvert.SerializeObject(current, Formatting.Indented));
                check(true, $"已生成 0.8 中文快照：{current.Count} 条数据");
                return;
            }

            var snapshot = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            int diff = 0;
            var diffs = new List<string>();
            foreach (var (k, v) in snapshot)
            {
                if (!current.TryGetValue(k, out var curVal))
                {
                    diff++;
                    diffs.Add($"缺少键 [{k}]（预期: {v}）");
                }
                else if (curVal != v)
                {
                    diff++;
                    diffs.Add($"键 [{k}] 不一致：\n  预期: {v}\n  实际: {curVal}");
                }
            }
            foreach (var k in current.Keys.Where(k => !snapshot.ContainsKey(k)))
            {
                diff++;
                diffs.Add($"多出键 [{k}]（当前: {current[k]}）");
            }

            check(diff == 0, $"中文输出有 {diff} 处和 0.8 快照不同：\n" + string.Join("\n", diffs.Take(10)));
        });
    }
}
