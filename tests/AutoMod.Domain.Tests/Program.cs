using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;
// Core 单元测试（不需要游戏）。每个 Test 是一个场景，失败时打印原因，最后返回非零退出码。
internal static class Program
{
    private static int _failed, _passed;

    private static string Fixtures
    {
        get
        {
            var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "ProbablyStolenMod.sln"))) d = d.Parent;
            return System.IO.Path.Combine(d.FullName, "tests", "AutoMod.Domain.Tests", "fixtures");
        }
    }

    private static void Check(bool ok, string what)
    {
        if (ok) _passed++;
        else { _failed++; Console.WriteLine("  ✗ " + what); }
    }

    private static void Test(string name, Action body)
    {
        Console.WriteLine("• " + name);
        try { body(); }
        catch (Exception ex) { _failed++; Console.WriteLine("  ✗ 异常: " + ex); }
    }

    private static int Main(string[] args)
    {
        if (args.Contains("balance")) { Balance.Run(args); return 0; }

        // 初始化本地化表（开发/测试环境直接从源码 ModFiles 读取）
        var zh = LocTests.LoadTable("zh");
        var en = LocTests.LoadTable("en");
        AutoMod.Domain.L.Use("zh", zh, en);

        Test("槽位：放入指定标签页", () =>
        {
            var w = FakeGame.Install(new AutoMod.Domain.Simulation.SimWorld());
            var meat = "slot:storage@肉类";
            var bay = w.AddRoot("storage", 1, "储藏区");
            bay.SlotList.Add(("slot:storage#1", "标签页1"));
            bay.SlotList.Add((meat, "肉类"));
            var plain = w.AddRoot("storage", 2, "未扩建");
            var a = w.Add(bay, "battery_empty"); a.InSlot = "slot:storage#1";
            var b2 = w.Add(bay, "battery_empty"); b2.InSlot = meat;
            var d = new RulesData();
            d.Selectors.Add(new SelectorDef { Id = "all", Name = "全部", From = { Places.Shop }, Kind = Catalog.Any });
            d.Selectors.Add(new SelectorDef { Id = "tab", Name = "肉类", From = { Places.Shop }, Kind = Catalog.Any, Slot = meat });
            d.Selectors.Add(new SelectorDef { Id = "panel", Name = "旧规则", From = { Places.Shop }, Kind = Catalog.Any, Slot = "slot:storage" });
            var ctx = new EvalCtx(w, d);
            Check(Eval.List(ctx, "tab").Count == 1, "筛选器取自标签页时只有那一页的物品");
            Check(Eval.List(new EvalCtx(w, d), "panel").Count == 2, "旧规则选的面板本身仍是全部标签页");
            Check(w.Fits(bay, a, meat) && !w.Fits(plain, a, meat), "只有带这个标签页的储藏区放得下");
            Check(w.Fits(plain, a, "slot:" + SlotNames.FirstTab), "未扩建的储藏区算有第一页");
            var r = w.Place(a, bay, meat);
            Check(r.Ok && a.InSlot == meat && w.Contents(bay, meat).Count == 2, "放入后物品在那个标签页里");
            Check(SlotNames.Name("slot:storage@肉类") == "肉类", "读不到标题时显示标签页名");
            Check(SlotNames.TabRole("storage", 1, null) == "storage#2" && SlotNames.TabRole("storage", 1, "肉类") == "storage@肉类", "改过名的按名字，没改过的按序号");
            Check(SlotNames.IsTab(meat) && !SlotNames.IsTab("slot:seed"), "机器的槽位不算标签页");
        });

        Test("槽位：动作的目标槽位", () =>
        {
            var w = FakeGame.Install(new AutoMod.Domain.Simulation.SimWorld());
            var meat = "slot:storage@肉类";
            var bay = w.AddRoot("storage", 1, "储藏区");
            bay.DeviceName = "仓库";
            bay.SlotList.Add(("slot:storage#1", "标签页1"));
            bay.SlotList.Add((meat, "肉类"));
            var item = w.Add(bay, "battery_empty"); item.InSlot = "slot:storage#1";
            var d = new RulesData();
            d.Selectors.Add(new SelectorDef { Id = "all", Name = "全部", From = { Places.Ref("仓库") }, Kind = Catalog.Any });
            var act = new ActionDef { Verb = ActVerb.Move, List = "all", To = Places.Ref("仓库"), ToSlot = meat };
            var s = new Schedule { Id = "s", Name = "s", Rules = { new WorkRule { Id = "r", Name = "r", Enabled = true, Actions = { act } } } };
            d.Schedules.Add(s);
            var ap = new Apprentice { Name = "甲", Level = 1, MaxAp = 100, Duties = { new Duty { ScheduleId = s.Id, Shift = Shift.Morning } } };
            new Interpreter(w, d).RunShift(Shift.Morning, new[] { ap });
            Check(item.InSlot == meat, $"同一个储藏区里换标签页也算要放入，实际在 {item.InSlot}");
            var card = Card.Sentence(d, s, s.Rules[0]).SelectMany(x => x).Select(x => x.Text);
            Check(string.Concat(card).Contains("肉类"), "卡片显示目标槽位");
            Check(Text.Action(d, s, act).Contains("肉类"), "动作文字显示目标槽位");
            act.ToSlot = "slot:storage@改名前";
            item.InSlot = "slot:storage#1";
            var rep = new Interpreter(w, d).RunShift(Shift.Morning, new[] { ap });
            var issue = rep.Groups.SelectMany(g => g.Rules).SelectMany(r => r.Issues).FirstOrDefault();
            Check(issue?.Text != null && issue.Text.Contains("改名前") && item.InSlot == "slot:storage#1", $"标签页改名后旧规则报没有这个槽位，物品不动：{issue?.Text}");
            Card.Set(act, "to", Places.Ref("别处"));
            Check(act.ToSlot == null, "换目标时清掉槽位");
            Check(!JsonCodec.Serialize(new ActionDef { Verb = ActVerb.Move }).Contains("toSlot"), "没选槽位时不写进存档");
            Check(JsonCodec.Deserialize<ActionDef>("{\"verb\":\"move\",\"to\":\"#x\"}").ToSlot == null, "旧存档的动作没有这个字段，读出来为空");
        });

        Test("数据升级：步骤和格式号对得上", () =>
        {
            var steps = AutoMod.Domain.Persistence.Migrations.Migrator.Steps;
            Check(AutoMod.Domain.Persistence.ModState.Format == AutoMod.Domain.Persistence.Migrations.Migrator.First + steps.Count, "格式号 = 最早格式 + 步骤数");
            Check(steps.Select((x, i) => x.From == AutoMod.Domain.Persistence.Migrations.Migrator.First + i).All(x => x), "步骤从最早格式起逐级连续");
            for (int v = AutoMod.Domain.Persistence.Migrations.Migrator.First; v <= AutoMod.Domain.Persistence.ModState.Format; v++)
            {
                string file = System.IO.Path.Combine(Fixtures, $"state_v{v}.json");
                Check(System.IO.File.Exists(file), $"每个格式都有真实样本：state_v{v}.json");
                if (!System.IO.File.Exists(file)) continue;
                var st = AutoMod.Domain.Persistence.ModState.FromJson(System.IO.File.ReadAllText(file));
                Check(st.Frozen == null && st.Rules.Schedules.Count == 7 && st.Rules.Selectors.Count > 50 && st.Apprentices.Count == 1 && st.Wages.Count == 1, $"v{v} 的样本读出全部数据");
                Check(st.Apprentices[0].Duties[0].ScheduleId == st.Rules.Schedules[0].Id, $"v{v} 的学徒和流程还连着");
            }
        });

        Test("数据升级：流程包", () =>
        {
            var d = new RulesData();
            var s = Transfer.Import(d, System.IO.File.ReadAllText(System.IO.Path.Combine(Fixtures, "pack_v8.txt")));
            Check(s != null && s.Rules.Count > 0 && d.Selectors.Count > 0, "1.2.1 导出的流程可以导入");
            string json = "{\"format\":3,\"schedule\":{\"name\":\"甲\",\"rules\":[{\"name\":\"旧\"}]},\"selectors\":[]}";
            var steps = new[] { new AutoMod.Domain.Persistence.Migrations.Migrator.Step { From = 8, Rules = r => r["schedules"][0]["rules"][0]["name"] = "新" } };
            var up = JsonCodec.Deserialize<SchedulePack>(Transfer.Upgraded(json, steps, 9));
            Check(up.Schedule.Rules[0].Name == "新" && up.DataFormat == 9, "没有 dataFormat 的流程包按最早格式升级，步骤作用在规则部分");
            bool newer = false;
            try { Transfer.Upgraded("{\"dataFormat\":99}", steps, 9); } catch (TransferException) { newer = true; }
            Check(newer, "比当前新的流程包不能导入");
        });

        Test("数据升级：存档", () =>
        {
            string v8 = "{\"version\":8,\"lastMorningDay\":5,\"note\":\"2026-11-07T10:00:00\",\"rules\":{\"schedules\":[{\"id\":\"s\",\"name\":\"旧\"}]}}";
            var steps = new[]
            {
                new AutoMod.Domain.Persistence.Migrations.Migrator.Step { From = 8, State = x => x["lastMorningDay"] = (int)x["lastMorningDay"] + 1, Rules = r => r["schedules"][0]["name"] = "中" },
                new AutoMod.Domain.Persistence.Migrations.Migrator.Step { From = 9, Rules = r => r["schedules"][0]["name"] = (string)r["schedules"][0]["name"] + "新" },
            };
            var up = AutoMod.Domain.Persistence.ModState.FromJson(v8, steps, 10);
            Check(up.Frozen == null && up.LastMorningDay == 6 && up.Rules.Schedules[0].Name == "中新", "按顺序走完每一步");
            Check(up.Backup == v8, "升级前的原文留作备份");

            string newer = "{\"version\":99,\"futureField\":1}";
            var f = AutoMod.Domain.Persistence.ModState.FromJson(newer);
            Check(f.Frozen == newer && f.FrozenFormat == 99 && f.ToJson() == newer, "比当前新的数据原样写回");

            string broken = "{\"version\":8,";
            var b = AutoMod.Domain.Persistence.ModState.FromJson(broken);
            Check(b.Frozen == broken && b.FrozenFormat == 0 && b.ToJson() == broken, "损坏的数据原样写回");

            var bad = new[] { new AutoMod.Domain.Persistence.Migrations.Migrator.Step { From = 8, State = _ => throw new InvalidOperationException("x") } };
            var e = AutoMod.Domain.Persistence.ModState.FromJson(v8, bad, 9);
            Check(e.Frozen == v8 && e.ToJson() == v8, "步骤出错时原样写回");
            var gap = AutoMod.Domain.Persistence.ModState.FromJson(v8, Array.Empty<AutoMod.Domain.Persistence.Migrations.Migrator.Step>(), 9);
            Check(gap.Frozen == v8, "缺步骤时原样写回");

            var cur = AutoMod.Domain.Persistence.ModState.FromJson(v8);
            Check(cur.Frozen == null && cur.Backup == null && cur.LastMorningDay == 5, "当前格式不升级、不备份");
            Check(AutoMod.Domain.Persistence.ModState.FromJson("{\"version\":7}").Frozen == null, "发布前的格式仍按空数据");
            var w = new AutoMod.Domain.Persistence.ModState { ModVersion = "1.2.2" };
            Check(w.ToJson().Contains("\"modVersion\":\"1.2.2\"") && !w.ToJson().Contains("frozen") && !w.ToJson().Contains("backup"), "写 mod 版本，冻结和备份不进存档");
        });

        Test("数值：升级曲线、升级涨薪", () =>
        {
            int total = 0;
            for (int l = 1; l < 5; l++) total += SkillMath.XpToNext(l);
            Check(total == 1520, $"Lv1→5 需要 1520 经验，实际 {total}");
            var a = new Apprentice { MaxAp = 25, Level = 1 };
            Check(a.Wage == 40, $"Lv1 日薪 40，实际 {a.Wage}");
            int ups = SkillMath.AddXp(a, SkillMath.XpToNext(1));
            Check(ups == 1 && a.Level == 2 && a.MaxAp == 30 && a.Wage == 50, $"升到 Lv2：每天 AP 25 → {a.MaxAp}，日薪涨到 {a.Wage}");
            var rich = new Apprentice { MaxAp = 40, Level = 1 };
            Check(ApprenticeFactory.WageOf(rich) == 40, "日薪只看等级，和每天的 AP 无关");
            var wages = new[] { 3, 5, 10 }.Select(l => { a.Level = l; return ApprenticeFactory.WageOf(a); }).ToList();
            Check(wages.SequenceEqual(new[] { 60, 80, 130 }), $"日薪 30 + 10 × 等级：Lv3/5/10 = 60/80/130：{string.Join("/", wages)}");
            var cand = ApprenticeFactory.Generate(new Random(3));
            Check(cand.Level == 1 && cand.MaxAp == 25 && cand.Wage == 40 && Proficiency.Score(cand.Proficiency, Proficiency.Blank()) == 100, "新学徒固定：白板、Lv1、每天 25 AP、日薪 40");
            var roster = new AutoMod.Domain.Workforce.Roster { Slots = 2 };
            var rng = new Random(1);
            var h1 = Agency.Hire(roster, rng, 1);
            var h2 = Agency.Hire(roster, rng, 1);
            Check(h1 != null && h2 != null && h1.Name != h2.Name && Agency.Hire(roster, rng, 1) == null, "中介直接雇用；名字不重复；名额满了不能再雇");
            var fees = Enumerable.Range(1, 9).Select(Agency.ExpandFee).ToList();
            Check(fees[0] == 500 && fees[1] == 600 && fees[8] == 1300 && fees.Zip(fees.Skip(1)).All(x => x.Second > x.First) && Agency.ExpandFee(10) == 0 && AutoMod.Domain.Workforce.Roster.MaxSlots == 10,
                $"名额最多 10，扩编一次比一次贵：{string.Join(" / ", fees)}");
        });

        Test("模组配置的倍率：费用、AP、成长", () =>
        {
            Check(Scales.Parse("×0", Scales.CostChoices) == 0 && Scales.Parse("×0.25", Scales.Choices) == 0.25 && Scales.Parse("×4", Scales.Choices) == 4,
                "档位文字读成倍率");
            Check(Scales.Parse("×0", Scales.Choices) == 1 && Scales.Parse("×7", Scales.Choices) == 1 && Scales.Parse("abc", Scales.Choices) == 1,
                "不在档位里的值按 ×1（AP、成长没有 ×0）");
            var a = new Apprentice { MaxAp = 25, Level = 1 };
            try
            {
                Scales.Cost = 0;
                Check(ApprenticeFactory.WageOf(a) == 0 && Agency.HireFee == 0 && Agency.ExpandFee(1) == 0 && Agency.TrainingFee(1) == 0, "×0：日薪和各项费用全部免费");
                Scales.Cost = 0.25;
                Check(ApprenticeFactory.WageOf(a) == 10 && Agency.HireFee == 15 && Agency.ExpandFee(1) == 125 && Agency.TrainingFee(1) == 13, $"×0.25：日薪 10、雇用 15、扩编 125、培训 13（实际 {Agency.TrainingFee(1)}）");
                Scales.Cost = 4;
                Check(ApprenticeFactory.WageOf(a) == 160 && Agency.ExpandFee(9) == 5200, "×4：日薪 160、最后一次扩编 5200");
                Scales.Cost = 1;

                Scales.Ap = 0.25;
                Check(a.DailyAp == 6 && a.ApLeft == 6, $"AP ×0.25：25 → 6（向下取整），实际 {a.DailyAp}");
                Scales.Ap = 3;
                a.ApUsed = 10;
                Check(a.MaxAp == 25 && a.DailyAp == 75 && a.ApLeft == 65, "AP ×3：存的基础值不变，每天 75、剩 65");
                a.FixedAp = 30;
                Check(a.DailyAp == 30, "模拟器虚拟学徒直接用填的 AP，不乘倍率");
                a.FixedAp = null;
                a.ApUsed = 0;
                Scales.Ap = 1;

                Scales.Growth = 2;
                Check(Scales.Xp(10.4) == 21 && Agency.TrainingXp(1) == 120, $"成长 ×2：经验翻倍，培训经验 60 → 120（实际 {Agency.TrainingXp(1)}）");
                Scales.Growth = 0.25;
                Check(Agency.TrainingXp(1) == 15 && Agency.TrainingXp(9) >= 1, "成长 ×0.25：培训经验 60 → 15");
                var p = new Apprentice { MaxAp = 25 };
                var work = new System.Collections.Generic.Dictionary<string, double> { [ActVerb.Move] = 1, [Category.Water] = 1 };
                Proficiency.Learn(p, work, 10);
                double slow = p.Proficiency[ActVerb.Move];
                Scales.Growth = 1;
                var q = new Apprentice { MaxAp = 25 };
                Proficiency.Learn(q, work, 10);
                Check(q.Proficiency[ActVerb.Move] > slow, "成长倍率也作用于熟练度学习");
            }
            finally { Scales.Cost = Scales.Ap = Scales.Growth = 1; }
        });

        RulesTests.Run(Test, Check);
        LocTests.Run(Test, Check);
        SnapshotTest.Run(Test, Check);
        ArchitectureTests.Run(Test, Check);

        Console.WriteLine($"\n{_passed} 通过，{_failed} 失败");
        return _failed == 0 ? 0 : 1;
    }
}
