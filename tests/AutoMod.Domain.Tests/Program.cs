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
            a.Wage = ApprenticeFactory.WageOf(a);
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

        RulesTests.Run(Test, Check);
        LocTests.Run(Test, Check);
        SnapshotTest.Run(Test, Check);
        ArchitectureTests.Run(Test, Check);

        Console.WriteLine($"\n{_passed} 通过，{_failed} 失败");
        return _failed == 0 ? 0 : 1;
    }
}
