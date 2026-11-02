using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;
using AutoMod.Domain.Workforce;

// 数值模拟：dotnet run --project tests/AutoMod.Domain.Tests -- balance
// 在模拟世界上跑几周，对比不同的熟练度 / AP 参数：每班完成与顺延、熟练度、AP 系数、等级、日薪，以及场景自己的指标。
internal static class Balance
{
    private record Setup(string Name, Proficiency.Tuning Tuning, Func<int, double> OldMult = null);

    private record Worker(string Name, int Level, int Ap, int Count = 1);

    private record Scenario(string Name, string Shift, Func<(SimWorld w, RulesData d, Schedule s, Func<SimWorld, double> metric)> Build, string MetricName, bool Cumulative);

    private const int Days = 28;

    /// <summary>旧模型：专精学徒的耗点倍率 max(0.35, 1 − 0.05L − 0.15)。</summary>
    private static double OldSpec(int level) => Math.Max(0.35, 1 - 0.05 * level - 0.15);

    public static void Run(string[] args)
    {
        // 每组都写全参数（默认值会随方案调整）
        var setups = new List<Setup>
        {
            new("旧·专精", null, OldSpec),
            new("E：学习 0.15（7 个操作维时定的）", new Proficiency.Tuning { FloorBase = 50, FloorPerLevel = 4, Scale = 0.75, Rate = 0.15, MinShare = 1 }),
            new("F：学习 0.12", new Proficiency.Tuning { FloorBase = 50, FloorPerLevel = 4, Scale = 0.75, Rate = 0.12, MinShare = 1 }),
            new("G：学习 0.10", new Proficiency.Tuning { FloorBase = 50, FloorPerLevel = 4, Scale = 0.75, Rate = 0.10, MinShare = 1 }),
            new("现行", new Proficiency.Tuning()),
        };
        var workers = new List<Worker> { new("Lv1 AP24", 1, 24), new("Lv3 AP24", 3, 24), new("Lv5 AP24", 5, 24), new("Lv1 AP24 ×2", 1, 24, 2) };
        var scenarios = new List<Scenario>
        {
            new("养鼠（约 70 只，4 个笼子）", Shift.Morning, RatFarm, "生肉（4 周累计）", true),
            new("电池轮转（6 台充能器，8 台用电机器）", Shift.Night, Batteries, "早上缺电的机器（台次）", true),
        };

        Console.WriteLine($"# 数值模拟（{Days} 天）\n");
        foreach (var sc in scenarios)
        {
            Console.WriteLine($"## {sc.Name}\n");
            Console.WriteLine($"| 参数 | 学徒 | 第 1 周 完成 / 顺延 | 第 2 周 | 第 4 周 | 熟练度 开始 → 第 1 / 2 / 4 周末 | AP 系数 首 → 末 | 等级（末） | 日薪 首 → 末 | {sc.MetricName} |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var wk in workers)
                foreach (var st in setups)
                    Console.WriteLine(Row(sc, wk, st));
            Console.WriteLine();
        }
        Proficiency.T = new Proficiency.Tuning();
    }

    private static string Row(Scenario sc, Worker wk, Setup st)
    {
        Proficiency.T = st.Tuning ?? new Proficiency.Tuning { FloorBase = 100, FloorPerLevel = 0, Scale = st.OldMult(wk.Level), Rate = 0 };
        var (w, d, s, metric) = sc.Build();
        var apps = Enumerable.Range(1, wk.Count).Select(i => new Apprentice { Name = "甲乙丙"[i - 1].ToString(), Level = wk.Level, MaxAp = wk.Ap, Duties = { new Duty { ScheduleId = s.Id, Shift = sc.Shift } } }).ToList();
        int wage0 = apps.Sum(a => a.Wage);
        var interp = new Interpreter(w, d) { UpdateProfiles = true };
        int score0 = Proficiency.Score(apps[0].Proficiency, Proficiency.ProfileOf(d, s, w));
        var done = new List<int>();
        var deferred = new List<int>();
        var mult = new List<double>();
        var score = new List<int>();
        double total = 0;
        GroupRunReport last = null;
        for (int day = 1; day <= Days; day++)
        {
            if (day > 1) w.Night();
            if (st.Tuning == null) Proficiency.T.Scale = st.OldMult(apps[0].Level); // 旧模型：倍率跟着等级走
            foreach (var a in apps) a.ApUsed = 0; // 每天 AP 回满
            var groups = interp.RunShift(sc.Shift, apps).Groups; // 每名学徒一组，依次执行
            last = groups[0];
            done.Add(groups.Sum(g => g.Rules.Sum(r => r.Done.Values.Sum())));
            deferred.Add(groups.Last().Deferred);
            mult.Add(last.Mult);
            score.Add(Proficiency.Score(apps[0].Proficiency, Proficiency.ProfileOf(d, s, w)));
            double m = metric(w);
            total = sc.Cumulative ? total + m : m;
            }
        string Week(int k) => $"{done.Skip(k * 7).Take(7).Average():0.#} / {deferred.Skip(k * 7).Take(7).Average():0.#}";
        string x = st.Tuning == null ? "—" : $"{score0} → {score[6]} / {score[13]} / {score[27]}";
        return $"| {st.Name} | {wk.Name} | {Week(0)} | {Week(1)} | {Week(3)} | {x} | {mult.First():0.00} → {mult.Last():0.00} | {string.Join("、", apps.Select(a => a.Level))} | {wage0} → {apps.Sum(a => a.Wage)} | {total:0} |";
    }

    // ================= 场景 =================

    /// <summary>养鼠：精英笼、育种笼、两个育肥笼，约 70 只；肉类储藏足够大，工具齐全，每天喂食。</summary>
    private static (SimWorld, RulesData, Schedule, Func<SimWorld, double>) RatFarm()
    {
        var w = FakeGame.Install(new SimWorld(7));
        var elite = w.AddRoot("animal_cage", 1001, "精英笼");
        var breeding = w.AddRoot("animal_cage", 1002, "育种笼");
        breeding.Capacity = 40;
        var fat1 = w.AddRoot("animal_cage", 1003, "育肥笼1");
        var fat2 = w.AddRoot("animal_cage", 1004, "育肥笼2");
        fat1.Capacity = fat2.Capacity = 60;
        var meat = w.AddRoot("storage", 1005, "肉类储藏");
        meat.Capacity = 2000;
        var tools = w.AddRoot("toolbox", 1006, "工具箱");
        w.Add(tools, "knife");
        w.Add(tools, "wire_cutter");
        for (int i = 0; i < 6; i++) w.Add(elite, "rat_m", r => r.Num["gene.growth"] = 1 + i * 0.2);
        for (int i = 0; i < 8; i++) w.Add(elite, "rat_f", r => r.Num["gene.growth"] = 1 + i * 0.2);
        for (int i = 0; i < 6; i++) w.Add(breeding, "rat_m", r => r.Num["gene.growth"] = 0.5 + i * 0.1);
        for (int i = 0; i < 14; i++) w.Add(breeding, "rat_f", r => r.Num["gene.growth"] = 0.5 + i * 0.05);
        for (int i = 0; i < 8; i++) w.Add(breeding, "rat_baby");
        for (int i = 0; i < 15; i++) w.Add(fat1, "rat_fat", r => r.Num["neutered"] = 1);
        for (int i = 0; i < 15; i++) w.Add(fat2, "rat_m", r => { r.Num["weight"] = 250; r.Num["age"] = 2; });
        var d = new RulesData();
        var s = Scenarios.Instantiate(d, Scenarios.Get("rats"), new()
        {
            ["elite"] = new() { 1001 }, ["breeding"] = new() { 1002 }, ["fatten"] = new() { 1003, 1004 }, ["meat"] = new() { 1005 }, ["tools"] = new() { 1006 },
        }, w: w); // 传 w：给容器起设备名，挑选器才找得到（不传时全部为空，出栏全是 0）
        int before = 0;
        double Metric(SimWorld x)
        {
            int now = meat.Children.Count;
            int gained = now - before;
            before = now;
            return gained;
        }
        return (w, d, s, Metric);
    }

    /// <summary>电池轮转示例：机器区 6 台充能器（各 2 块半满电池），4 台投影仪（每夜 −13）+ 4 台用电机器（每夜 −5），满电架 6 块，待充架 8 块。</summary>
    private static (SimWorld, RulesData, Schedule, Func<SimWorld, double>) Batteries()
    {
        var w = FakeGame.Install(new SimWorld(3));
        var bay = w.AddRoot("storage", 1, "机器区");
        bay.Capacity = 40;
        for (int i = 0; i < 6; i++)
        {
            var ch = w.Add(bay, "recharger");
            for (int k = 0; k < 2; k++) w.Add(ch, "battery_empty", b => b.Num["energy"] = 40 + 10 * k);
        }
        var machines = new List<SimItem>();
        for (int i = 0; i < 8; i++)
        {
            var m = w.AddRoot(i < 4 ? "projector" : "machine", 100 + i, i < 4 ? $"投影仪{i + 1}" : $"机器{i - 3}");
            w.Add(m, "battery_empty", b => b.Num["energy"] = 30 + 8 * i, batterySlot: true);
            machines.Add(m);
        }
        var full = w.AddRoot("storage", 2, "满电架");
        full.Capacity = 40;
        for (int i = 0; i < 6; i++) w.Add(full, "battery_full");
        var pending = w.AddRoot("storage", 3, "待充架");
        pending.Capacity = 40;
        for (int i = 0; i < 8; i++) w.Add(pending, "battery_empty", b => b.Num["energy"] = 20);
        var d = new RulesData();
        var s = Examples.BatteryRotation(d);
        double Metric(SimWorld x) => machines.Count(m => (m.Children.FirstOrDefault(c => c.InBatterySlot)?.N("energy") ?? 0) < 10);
        return (w, d, s, Metric);
    }
}
