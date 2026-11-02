using System;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;
// Core 单元测试（不需要游戏）。每个 Test 是一个场景，失败时打印原因，最后返回非零退出码。
internal static class Program
{
    private static int _failed, _passed;

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
