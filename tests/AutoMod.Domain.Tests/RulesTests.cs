using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Persistence;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;
using AutoMod.Domain.Workforce;
// 排班与规则的单元测试。
internal static class RulesTests
{
    private static Action<bool, string> _check;
    private static void Check(bool ok, string what) => _check(ok, what);

    // ---------- 搭场景 ----------

    private class RatShop
    {
        public SimWorld W = FakeGame.Install(new SimWorld(7));
        public SimItem Elite, Breeding, Fat1, Fat2, Meat, Tools;

        public RatShop(bool wireCutter = true, int breedingCap = 22)
        {
            Elite = W.AddRoot("animal_cage", 1001, "精英笼");
            Breeding = W.AddRoot("animal_cage", 1002, "育种笼");
            Breeding.Capacity = breedingCap;
            Fat1 = W.AddRoot("animal_cage", 1003, "育肥笼1");
            Fat2 = W.AddRoot("animal_cage", 1004, "育肥笼2");
            Fat1.Capacity = Fat2.Capacity = 60;
            Meat = W.AddRoot("storage", 1005, "肉类储藏");
            Tools = W.AddRoot("toolbox", 1006, "工具箱");
            W.Add(Tools, "knife");
            if (wireCutter) W.Add(Tools, "wire_cutter");
            for (int i = 0; i < 5; i++) W.Add(Elite, "rat_m", r => r.Num["gene.growth"] = 1 + i * 0.2);
            for (int i = 0; i < 6; i++) W.Add(Elite, "rat_f", r => r.Num["gene.growth"] = 1 + i * 0.2);
            for (int i = 0; i < 5; i++) W.Add(Breeding, "rat_m", r => r.Num["gene.growth"] = 0.5 + i * 0.1);
            for (int i = 0; i < 10; i++) W.Add(Breeding, "rat_f", r => r.Num["gene.growth"] = 0.5 + i * 0.05);
            for (int i = 0; i < 3; i++) W.Add(Breeding, "rat_baby");
            for (int i = 0; i < 6; i++) W.Add(Fat1, "rat_fat", r => r.Num["neutered"] = 1);
            for (int i = 0; i < 3; i++) W.Add(Fat2, "rat_m", r => { r.Num["weight"] = 250; r.Num["age"] = 2; });
            // 设备名和场景的占位名一样（Scenarios.Instantiate 按名字引用）
            Elite.DeviceName = "精英笼"; Breeding.DeviceName = "育种笼"; Fat1.DeviceName = Fat2.DeviceName = "育肥笼";
            Meat.DeviceName = "肉类储藏"; Tools.DeviceName = "工具箱";
        }

        public Dictionary<string, List<int>> Bindings => new()
        {
            ["elite"] = new() { 1001 }, ["breeding"] = new() { 1002 }, ["fatten"] = new() { 1003, 1004 },
            ["meat"] = new() { 1005 }, ["tools"] = new() { 1006 },
        };
    }

    /// <summary>测试学徒：白板，等级 lvl。</summary>
    private static Apprentice App(string name, int ap, int lvl, string schedule, string shift = Shift.Morning) =>
        new() { Name = name, MaxAp = ap, Level = Math.Max(1, lvl), Duties = schedule == null ? new() : new() { new Duty { ScheduleId = schedule, Shift = shift } } };

    private static SelectorDef Sel(RulesData d, string id, string from, string kind, Action<SelectorDef> tweak = null)
    {
        var s = new SelectorDef { Id = id, Name = id, From = { from }, Kind = kind };
        tweak?.Invoke(s);
        d.Selectors.Add(s);
        return s;
    }

    /// <summary>给模拟物品起设备名（店里的“满电区”），返回引用 "#名字"。</summary>
    private static string Named(SimWorld w, string name, params int[] uids)
    {
        foreach (var x in w.All().Where(x => uids.Contains(x.Id))) x.DeviceName = name;
        return Places.Ref(name);
    }

    private static int Rats(SimItem cage, Func<SimItem, bool> f = null) => cage.Children.Count(c => c.Kind == "rat" && (f == null || f(c)));

    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        _check = check;

        // ================= 求值 =================

        test("规则·表达式：先乘除后加减、常量、除以 0 为空", () =>
        {
            var d = new RulesData { Objects = { new ObjectDef { Id = "k", Name = "k", Value = 10 } } };
            var c = new EvalCtx(new SimWorld(), d);
            Check(Eval.Expr(c, Expr.Of(2).Then("+", Term.N(3)).Then("*", Term.N(4))) == 14, "2 + 3 × 4 = 14");
            Check(Eval.Expr(c, Expr.Ref(TermKind.Const, "k").Then("-", Term.N(4)).Then("/", Term.N(2))) == 8, "k − 4 ÷ 2 = 8");
            Check(Eval.Expr(c, Expr.Of(1).Then("/", Term.N(0))) == null, "除以 0 为空");
            Check(Eval.Expr(c, Expr.Ref(TermKind.Const, "missing")) == null, "引用不存在的常量为空");
            Check(!Vocab.Compare(null, ">", 1) && !Vocab.Compare(1, "<", null), "与空值比较为假");
        });

        test("规则·来源展开：内容物、本身、筛选、槽位", () =>
        {
            var w = new SimWorld();
            var bay = w.AddRoot("storage", 1, "机器区");
            var ch1 = w.Add(bay, "recharger");
            var ch2 = w.Add(bay, "recharger");
            w.Add(ch1, "battery_full");
            w.Add(ch2, "battery_empty");
            var proj = w.AddRoot("projector", 2, "投影仪");
            w.Add(proj, "battery_empty", batterySlot: true);
            var cage = w.AddRoot("animal_cage", 3, "笼");
            w.Add(cage, "rat_m"); w.Add(cage, "rat_f");
            var d = new RulesData();
            Named(w, "bay", 1); Named(w, "proj", 2); Named(w, "cage", 3);
            Sel(d, "chargers", "#bay", "recharger");
            Sel(d, "bats", "chargers", "battery");
            Sel(d, "fullBats", "bats", "battery", s => s.Where.Items.Add(Cond.Cmp("energyPct", ">=", Expr.Of(95))));
            Sel(d, "self", "#cage", Catalog.Self);
            Sel(d, "cageIn", "#cage", "cage");
            Sel(d, "rats", "#cage", "rat");
            Sel(d, "slot", "#proj", "battery", s => s.Slot = "battery");
            Sel(d, "bayBats", "#bay", "battery");
            var c = new EvalCtx(w, d);
            Check(Eval.List(c, "chargers").Count == 2, "机器区的内容物：2 台充能器");
            Check(Eval.List(c, "bats").Count == 2, "充能器列表里面的电池：2 块");
            Check(Eval.List(c, "fullBats").Count == 1, "在电池列表内筛选：已充满 1 块");
            Check(Eval.List(c, "self").SequenceEqual(new object[] { cage }), "范围是“本身”：容器本身");
            Check(Eval.List(c, "cageIn").Count == 0, "范围是“里的东西”：即使类型和容器相同，也只找里面的（笼里没有笼子）");
            Check(Eval.List(c, "rats").Count == 2, "笼的内容物：2 只鼠");
            Check(Eval.List(c, "slot").Count == 1, "电池槽：1 块");
            Check(Eval.List(c, "bayBats").Count == 0, "直接内容物不往下翻：机器区里没有直接放着的电池");
        });

        test("规则·挑选器：排序稳定（相等按 uid）、截取、排除、统计、空值", () =>
        {
            var w = new SimWorld();
            var cage = w.AddRoot("animal_cage", 1);
            foreach (var wt in new[] { 300, 500, 300, 400 }) w.Add(cage, "rat_m", r => r.Num["weight"] = wt);
            var d = new RulesData();
            Named(w, "cage", 1);
            Sel(d, "all", "#cage", "rat", s => s.Sort = new SortSpec { Prop = "weight", Desc = true });
            Sel(d, "top2", "all", "rat", s => s.Take = new TakeSpec { Mode = TakeMode.First, N = Expr.Of(2) });
            Sel(d, "from3", "all", "rat", s => s.Take = new TakeSpec { Mode = TakeMode.From, N = Expr.Of(3) });
            Sel(d, "rest", "all", "rat", s => s.Minus = "top2");
            Sel(d, "minW", "#cage", "rat", s => { s.Mode = SelectorMode.Number; s.Agg = new AggSpec { Fn = AggFn.Min, Prop = "weight" }; });
            Sel(d, "none", "#cage", "battery_full", s => { s.Mode = SelectorMode.Number; s.Agg = new AggSpec { Fn = AggFn.Max, Prop = "energy" }; });
            var c = new EvalCtx(w, d);
            var all = Eval.List(c, "all").Cast<SimItem>().ToList();
            Check(all.Select(r => r.N("weight")).SequenceEqual(new double[] { 500, 400, 300, 300 }), "体重降序");
            Check(all[2].Id < all[3].Id, "体重相等时按 uid 升序");
            Check(Eval.List(c, "top2").Count == 2 && Eval.List(c, "from3").Count == 2, "前 2 个 / 第 3 个起");
            Check(Eval.List(c, "rest").SequenceEqual(Eval.List(c, "from3")), "排除前 2 个 = 第 3 个起");
            Check(Eval.Number(c, "minW") == 300, "最小体重 300");
            Check(Eval.Number(c, "none") == null && Eval.Number(c, "all") == 4, "无对象时统计为空；列表取数量");
        });

        test("规则·自定义属性：按公式计算，不适用的类型为空", () =>
        {
            var w = new SimWorld();
            var cage = w.AddRoot("animal_cage", 1);
            var rat = w.Add(cage, "rat_m", r => { r.Num["gene.growth"] = 1.5; r.Num["gene.litter"] = 4; });
            var bat = w.Add(cage, "battery_full");
            var d = new RulesData
            {
                Properties = { new PropertyDef { Id = "score", ItemKind = "rat", Formula = Expr.Of(2).Then("*", Term.Own("gene.growth")).Then("+", Term.Own("gene.litter")) } },
            };
            var c = new EvalCtx(w, d);
            Check(Eval.Prop(c, rat, "score") == 7, "2 × 1.5 + 4 = 7");
            Check(Eval.Prop(c, bat, "score") == null, "电池没有鼠评分");
        });

        // ================= 静态检查 =================

        test("规则·静态检查：依赖顺序、表达式作用域、类型、绑定", () =>
        {
            var d = new RulesData();
            var a = Sel(d, "a", "#cage", "rat");
            var b = Sel(d, "b", "a", "rat");
            Check(Check0(d, new List<SelectorDef> { a, b }) == null, "正常顺序没有反向引用");
            Check(Check0(d, new List<SelectorDef> { b, a }) is { } back && back.a == b && back.b == a, "b 在 a 上方时报反向引用");
            Check(AutoMod.Domain.Rules.Check.ExprErrors(d, Expr.Ref(TermKind.Note, "x"), ExprScope.Condition).Count > 0, "挑选器条件里不能用记事本");
            Check(AutoMod.Domain.Rules.Check.ExprErrors(d, Expr.Ref(TermKind.Sys, SysVar.ApLeft), ExprScope.Take).Count > 0, "数量限制里不能用系统变量");
            Check(AutoMod.Domain.Rules.Check.ExprErrors(d, Expr.Ref(TermKind.Stat, "b"), ExprScope.Condition, null, 1).Count == 0, "筛选器可以引用排在下方的筛选器");
            a.Minus = "b";
            Check(AutoMod.Domain.Rules.Check.SelectorErrors(d, a).Any(e => e.Contains("循环引用")), "a 排除 b、b 又以 a 为来源：报循环引用");
            a.Minus = null;
            var bats = Sel(d, "bats", "#cage", "battery");
            var s = new Schedule { Id = "s", Name = "s" };
            var r1 = new WorkRule { Name = "r1", Actions = { new ActionDef { Verb = ActVerb.Use, List = "bats" } } };
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, s, r1).Any(e => e.Contains("未选择工具")), "使用：没选工具报错");
            var r2 = new WorkRule { Name = "r2", Actions = { new ActionDef { Verb = ActVerb.Move, List = "a", To = "nothing" } } };
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, s, r2).Any(e => e.Contains("须为容器")), "目标不是设备名也不是筛选器：报错，写明须为容器");
            var r2b = new WorkRule { Name = "r2b", Actions = { new ActionDef { Verb = ActVerb.Move, List = "a", To = "#没人叫这个" } } };
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, s, r2b).Count == 0 && AutoMod.Domain.Rules.Check.MissingNames(d, AutoMod.Domain.Rules.Check.RuleRefs(r2b), new SimWorld()).Contains("没人叫这个"),
                "店里还没有这个设备名：不算错误，单独列出来");
            var r3 = new WorkRule { Name = "r3", Actions = { new ActionDef { Verb = ActVerb.Move, List = "a", To = "#cage" } } };
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, s, r3).Count == 0, "完整的规则没有错误");
            Check(AutoMod.Domain.Rules.Check.Referrers(d, "a").Contains("筛选器 b"), "被引用列表包含筛选器 b");
        });

        // ================= 解释器 =================

        test("发薪：勾选的上岗付日薪，付不起从名册后面往前罢工", () =>
        {
            Apprentice Wk(string n, int wage, bool paid) => new() { Name = n, Level = (wage - 30) / 10, Paid = paid }; // 日薪 30 + 10 × 等级
            var apps = new List<Apprentice> { Wk("甲", 100, true), Wk("乙", 80, false), Wk("丙", 60, true) };
            Check(Payday.Settle(apps, 1000) == 160 && apps[0].OnDuty && !apps[1].OnDuty && apps[2].OnDuty, "钱够：勾选的都上岗");
            Check(Payday.Settle(apps, 120) == 100 && apps[0].OnDuty && apps[2].Striking && !apps[2].OnDuty, "钱不够：后面的先罢工");
            Check(Payday.Settle(apps, 50) == 0 && apps.All(x => !x.OnDuty) && apps[0].Striking && !apps[1].Striking, "一个都付不起；没勾选的不算罢工");
        });

        test("熟练度：公式、保底、组系数、学习曲线、换排班", () =>
        {
            var w = Proficiency.Normalize(new Dictionary<string, double> { [ActVerb.Move] = 3, [ActVerb.Swap] = 2, [Category.Power] = 5 });
            Check(Proficiency.Score(w, w) == 100, "和排班一致：100");
            int blank = Proficiency.Score(Proficiency.Blank(), w);
            Check(blank > 35 && blank < 50, $"白板对一个只换电池的排班：{blank}");
            var a = App("甲", 24, 1, "s");
            var b = App("乙", 24, 5, "s");
            Check(Proficiency.Effective(a, w) == 54 && Proficiency.Effective(b, w) == 70, "等级保底：Lv1 = 54，Lv5 = 70");
            Check(Math.Abs(Proficiency.CostMult(100) - 0.75) < 1e-9 && Math.Abs(Proficiency.CostMult(54) - 0.75 * 100 / 54) < 1e-9, "AP 系数 = 0.75 × 100 / 有效熟练度");
            Check(Proficiency.CostMult(54) > 1.35 && Proficiency.CostMult(93) < 0.82, "新人约 1.39，做熟约 0.81（等级不再另打折）");

            // 学习：满负荷一直做同一个排班，两周（14 班）到 90 左右
            var c = App("丙", 24, 1, "s");
            var scores = new List<int>();
            for (int i = 0; i < 21; i++) { Proficiency.Learn(c, w, 12); scores.Add(Proficiency.Score(c.Proficiency, w)); }
            Check(scores[6] is > 75 and < 88 && scores[13] is >= 90 and <= 97 && scores[20] >= 95, $"7 / 14 / 21 班：{scores[6]} / {scores[13]} / {scores[20]}");
            Check(scores.Zip(scores.Skip(1)).All(x => x.Second >= x.First), "熟练度只升不降（做同一个排班）");
            var idle = App("丁", 24, 1, "s");
            var light = App("戊", 24, 1, "s");
            var full = App("己", 24, 1, "s");
            Proficiency.Learn(idle, w, 0);
            Proficiency.Learn(light, w, 2);
            Proficiency.Learn(full, w, 12);
            Check(Proficiency.Score(idle.Proficiency, w) == blank && Proficiency.Score(light.Proficiency, w) == Proficiency.Score(full.Proficiency, w),
                "每上一班学一次：没干活不学，轻活和满负荷学得一样多");

            // 换排班：相近的活继承一部分
            var moveRats = Proficiency.Normalize(new Dictionary<string, double> { [ActVerb.Move] = 1, [Category.Husbandry] = 1 });
            var butcher = Proficiency.Normalize(new Dictionary<string, double> { [ActVerb.Use] = 1, [Category.Husbandry] = 1 });
            int toMoveRats = Proficiency.Score(c.Proficiency, moveRats), toButcher = Proficiency.Score(c.Proficiency, butcher);
            int blankRats = Proficiency.Score(Proficiency.Blank(), moveRats);
            Check(toMoveRats > toButcher && toMoveRats > blankRats, $"电池熟手去搬老鼠（{toMoveRats}）比去用刀（{toButcher}）、比白板（{blankRats}）熟");

            // 经验：熟练的活长得快
            Check(SkillMath.XpGain(2, 100) == 2 && SkillMath.XpGain(2, 50) == 1.5, "经验 = 单价 × (0.5 + 0.5 × 有效熟练度/100)");

            // 模拟器的虚拟学徒：按目标熟练度生成向量
            Check(Math.Abs(Proficiency.Score(Proficiency.ForScore(w, 80), w) - 80) <= 1, "按目标熟练度生成经验构成");
        });

        test("存档·工作日报：店里那份日报的报告和“明早要发”经 JSON 往返保留", () =>
        {
            var rep = new ShiftReport { Shift = Shift.Morning, Day = 5, Groups = { new ReportGroup { Schedule = "自动换电", Workers = "甲", ApUsed = 3, ApTotal = 24 } } };
            var ed = new DailyEdition { Day = 5, Morning = rep, Wage = 50, Strikers = 1 };
            var back = ModState.FromJson(new ModState { Daily = ed, DailyPlanned = true }.ToJson());
            Check(back.DailyPlanned && back.Daily != null && back.Daily.Day == 5 && back.Daily.Morning != null && back.Daily.Morning.Shift == Shift.Morning && back.Daily.Morning.Groups.Single().Schedule == "自动换电" && back.Daily.Wage == 50 && back.Daily.Strikers == 1, "日报报告和计划保留");
            var none = ModState.FromJson(new ModState().ToJson());
            Check(!none.DailyPlanned && none.Daily == null, "没发过日报时为空");
        });

        test("日报·总结数据：只有早班、早晚合并、学徒分工、同一问题去重与 AP 汇总", () =>
        {
            // 1. 只有早班
            var morningOnly = new ShiftReport
            {
                Shift = Shift.Morning,
                Day = 3,
                Workers = { ["甲"] = new[] { 24, 10 } },
                Groups = { new ReportGroup { Schedule = "单早流程", Workers = "甲", ApUsed = 14, ApTotal = 24 } },
                Entries = { new ReportEntry { Ok = true, Ws = "单早流程", Rule = "整理", Cost = 14 } },
            };
            var edMorningOnly = new DailyEdition
            {
                Day = 3,
                Night = null,
                Morning = morningOnly,
                Wage = 0,
                Strikers = 0,
            };
            Check(edMorningOnly.Done == 1, "只有早班：完成件数正确");
            Check(edMorningOnly.ApUsed == 14 && edMorningOnly.ApTotal == 24, "只有早班：AP 正确");
            Check(edMorningOnly.Wage == 0 && edMorningOnly.Strikers == 0, "只有早班：无工资与罢工");
            Check(edMorningOnly.Flows.Count == 1 && edMorningOnly.Flows[0].Name == "单早流程" && edMorningOnly.Flows[0].MorningDone == 1 && edMorningOnly.Flows[0].NightDone == 0, "只有早班：流程正确");
            Check(edMorningOnly.Problems.Count == 0, "只有早班：无问题");

            // 2. 早晚都有，覆盖：
            // - 学徒只上晚班（乙）、只上早班（丙）、早晚都上（甲）
            // - 同一问题在晚班和早班各出现一次（原因数字不同，合并计数为 2）
            // - 没有罢工（Strikers == 0）
            // - AP 总和按学徒算
            var night = new ShiftReport
            {
                Shift = Shift.Night,
                Day = 4,
                Workers =
                {
                    ["甲"] = new[] { 24, 16 }, // 甲晚班起点 24，剩 16
                    ["乙"] = new[] { 20, 8 },  // 乙只上晚班，起点 20，剩 8
                },
                Groups =
                {
                    new ReportGroup { Schedule = "整理流程", Workers = "甲", ApUsed = 8, ApTotal = 24, Growth = { "甲 熟练度 40 → 42" } },
                    new ReportGroup { Schedule = "换电流程", Workers = "乙", ApUsed = 12, ApTotal = 20 },
                },
                Entries =
                {
                    new ReportEntry { Ok = true, Ws = "整理流程", Rule = "分类", Cost = 8 },
                    new ReportEntry { Ok = false, Ws = "换电流程", Rule = "插电池", Reason = "工位缺少 1 件电线", Code = FailCode.NoSource },
                },
            };

            var morning = new ShiftReport
            {
                Shift = Shift.Morning,
                Day = 5,
                Workers =
                {
                    ["甲"] = new[] { 16, 6 },  // 甲早班用晚班剩下的 16，剩 6
                    ["丙"] = new[] { 30, 20 }, // 丙只上早班，起点 30，剩 20
                },
                Groups =
                {
                    new ReportGroup { Schedule = "换电流程", Workers = "甲", ApUsed = 10, ApTotal = 16, Growth = { "甲 熟练度 40 → 42", "甲 熟练度 42 → 44" } },
                    new ReportGroup { Schedule = "水务流程", Workers = "丙", ApUsed = 10, ApTotal = 30 },
                },
                Entries =
                {
                    new ReportEntry { Ok = false, Ws = "换电流程", Rule = "插电池", Reason = "工位缺少 2 件电线", Code = FailCode.NoSource },
                    new ReportEntry { Ok = true, Ws = "水务流程", Rule = "接水", Cost = 10 },
                    new ReportEntry { Ok = false, Deferred = true, Code = FailCode.NoAp, Ws = "水务流程", Rule = "接水", Count = 3, Reason = "AP 耗尽，3 件未处理" },
                },
            };

            var ed = new DailyEdition
            {
                Day = 5,
                Night = night,
                Morning = morning,
                Wage = 150,
                Strikers = 0,
            };

            // AP 校验：
            // 甲：起点 24，剩余 6，用掉 18
            // 乙：起点 20，剩余 8，用掉 12
            // 丙：起点 30，剩余 20，用掉 10
            // 总用掉 = 18 + 12 + 10 = 40；总起点 = 24 + 20 + 30 = 74
            Check(ed.ApUsed == 40 && ed.ApTotal == 74, $"AP 按学徒算：已用 {ed.ApUsed}/40，总计 {ed.ApTotal}/74");

            // 完成件数校验：晚班 1 + 早班 1 = 2
            Check(ed.Done == 2, $"完成件数两班之和：{ed.Done}/2");

            // 流程汇总顺序与数据校验：先晚班（整理、换电），后早班（水务）
            Check(ed.Flows.Count == 3, $"流程总数 3，实际 {ed.Flows.Count}");
            Check(ed.Flows[0].Name == "整理流程" && ed.Flows[0].NightDone == 1 && ed.Flows[0].MorningDone == 0 && ed.Flows[0].ApUsed == 8, "流程 0：整理流程");
            Check(ed.Flows[1].Name == "换电流程" && ed.Flows[1].NightDone == 0 && ed.Flows[1].MorningDone == 0 && ed.Flows[1].ApUsed == 22 && ed.Flows[1].Problems == 2, "流程 1：换电流程（问题数 2）");
            Check(ed.Flows[2].Name == "水务流程" && ed.Flows[2].NightDone == 0 && ed.Flows[2].MorningDone == 1 && ed.Flows[2].ApUsed == 10, "流程 2：水务流程");

            // 问题合并校验：晚班“工位缺少 1 件电线”与早班“工位缺少 2 件电线”去掉数字后相同，合并为 1 条，Count = 2
            Check(ed.Problems.Count == 1, $"问题去重后为 1 条，实际 {ed.Problems.Count}");
            Check(ed.Problems[0].Flow == "换电流程" && ed.Problems[0].Rule == "插电池" && ed.Problems[0].Count == 2, $"同一问题出现两次合并为 Count 2：实际 {ed.Problems[0].Count}");
            Check(ed.ProblemCount == 2, $"总问题数统计正确：{ed.ProblemCount}");

            // 留到下次件数
            Check(ed.Deferred == 3, $"留到下次件数正确：{ed.Deferred}/3");

            // Growth 去重
            Check(ed.Growth.Count == 2 && ed.Growth[0] == "甲 熟练度 40 → 42" && ed.Growth[1] == "甲 熟练度 42 → 44", $"熟练度去重后 2 行：{ed.Growth.Count}");

            // 罢工与工资校验
            Check(ed.Wage == 150 && ed.Strikers == 0, "没有罢工与昨夜工资");

            // JSON 往返校验
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ed);
            var backEd = Newtonsoft.Json.JsonConvert.DeserializeObject<DailyEdition>(json);
            Check(backEd != null && backEd.Day == 5 && backEd.Wage == 150 && backEd.Strikers == 0, "JSON 往返基本字段");
            Check(backEd.Done == 2 && backEd.ApUsed == 40 && backEd.ApTotal == 74, "JSON 往返后计算属性正常");
            Check(backEd.Problems.Count == 1 && backEd.Problems[0].Count == 2, "JSON 往返后问题汇总正常");
            Check(backEd.Flows.Count == 3, "JSON 往返后流程汇总正常");
        });

        test("规则·存档：排班数据经 JSON 往返后定义与执行结果不变", () =>
        {
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), new RatShop().Bindings);
            sch.Notepad.Add(new NoteDef { Id = "m_x", Name = "测试", Init = 2 });
            var state = new ModState { Rules = d };
            var back = ModState.FromJson(state.ToJson()).Rules;
            var old = ModState.FromJson("{\"version\":5,\"apprentices\":[{\"name\":\"甲\"}]}");
            Check(old.Apprentices.Count == 0 && old.Rules.Schedules.Count == 0 && old.Version == ModState.Format, "旧格式的 mod 数据整份忽略");
            var sch2 = back.Schedules.Single();
            Check(back.Objects.Count == d.Objects.Count && back.Selectors.Count == d.Selectors.Count && back.Properties.Count == d.Properties.Count,
                $"对象 {back.Objects.Count}/{d.Objects.Count}、挑选器 {back.Selectors.Count}/{d.Selectors.Count}、属性 {back.Properties.Count}/{d.Properties.Count}");
            Check(d.Selectors.Select(x => Text.Selector(d, x)).SequenceEqual(back.Selectors.Select(x => Text.Selector(back, x))), "挑选器定义文字一致");
            Check(sch.Rules.Select(r => Text.Rule(d, sch, r)).SequenceEqual(sch2.Rules.Select(r => Text.Rule(back, sch2, r))), "规则文字一致");
            Check(d.Properties.Select(x => Text.Property(d, x)).SequenceEqual(back.Properties.Select(x => Text.Property(back, x))), "自定义属性公式一致");
            Check(sch2.Notepad.Any(n => n.Name == "测试" && n.Init == 2), "记事本保留");

            // 存档的 JSON 格式
            var app = new Apprentice { Name = "乙" };
            app.Proficiency[Category.Power] = 0.5;
            var raw = new ModState { Apprentices = { app } }.ToJson();
            Check(raw.Contains("\"apprentices\"") && raw.Contains($"\"{Category.Power}\"") && !raw.Contains("\"Apprentices\""), "属性名驼峰、字典的键原样");
            Check(!raw.Contains(":null"), "不写 null");
            Check(ModState.FromJson(raw).Apprentices.Single().Proficiency.Count == app.Proficiency.Count, "字典读回不追加到初始值上");
            var multi = new Apprentice { Name = "丙", ApUsed = 7.4, Duties = { new Duty { ScheduleId = "s1", Shift = Shift.Night }, new Duty { ScheduleId = "s2" } } };
            var back2 = ModState.FromJson(new ModState { Apprentices = { multi }, Slots = 7 }.ToJson());
            var m2 = back2.Apprentices.Single();
            Check(back2.Slots == 7 && m2.Duties.Count == 2 && m2.Duties[0].ScheduleId == "s1" && m2.Duties[0].Shift == Shift.Night && m2.Duties[1].Shift == Shift.Morning
                  && Math.Abs(m2.ApUsed - 7.4) < 1e-9, "存读档：学徒的多个流程（流程 + 班次）、当天已用 AP、名额都保留");
            Check(d.Objects.Select(o => o.Name + "|" + o.Value).SequenceEqual(back.Objects.Select(o => o.Name + "|" + o.Value)), "参数保留");
            var s1 = new RatShop();
            var s2 = new RatShop();
            var g1 = new Interpreter(s1.W, d).RunShift(Shift.Morning, new[] { App("甲", 80, 2, sch.Id) }).Groups[0];
            var g2 = new Interpreter(s2.W, back).RunShift(Shift.Morning, new[] { App("甲", 80, 2, sch2.Id) }).Groups[0];
            Check(g1.Rules.Select(r => r.Pre + string.Join(",", r.Done)).SequenceEqual(g2.Rules.Select(r => r.Pre + string.Join(",", r.Done))) && Math.Abs(g1.ApUsed - g2.ApUsed) < 1e-9,
                $"执行结果一致（AP {g1.ApUsed}/{g2.ApUsed}）");
        });

        test("规则·目录：类别、上级类别、类型检查、主类别与技能类别", () =>
        {
            var w = new SimWorld();
            var bay = w.AddRoot("storage", 1, "机器区");
            var ch = w.Add(bay, "recharger", force: true);
            var self = w.Add(ch, "battery_self");
            var cage = w.AddRoot("animal_cage", 2, "笼");
            var rat = w.Add(cage, "rat_m");
            Check(Catalog.HasTrait(w, self, "battery") && Catalog.HasTrait(w, self, "selfCharging"), "自充电电池也是电池（上级类别）");
            Check(Catalog.Matches(w, ch, Catalog.Container) && Catalog.Matches(w, ch, "recharger") && !Catalog.Matches(w, rat, Catalog.Container), "充能器是容器；鼠不是");
            Check(Catalog.PrimaryTrait(w, ch) == "machine" && Catalog.PrimaryTrait(w, bay) == "storage", "主类别：充能器是机器 / 存放类");
            Check(Catalog.SkillOf(w, rat) == Category.Husbandry && Catalog.SkillOf(w, self) == Category.Power && Catalog.SkillOf(w, bay) == Category.Misc, "技能类别按物品的类别决定");
            Check(Catalog.Satisfies("selfCharging", "battery") && !Catalog.Satisfies("battery", "animal") && Catalog.Satisfies("rat", "animal", w), "类型检查：下级类别满足上级；种类按种类档案");
            Check(Catalog.Satisfies("unknown_kind", "animal", w), "没见过的种类不报类型错误");
            Check(Catalog.IsContainerType("cage") && Catalog.IsContainerType(Catalog.Self) && !Catalog.IsContainerType("battery"), "容器类型写法");
            Check(Catalog.PropsFor("rat", w).Contains("weight") && !Catalog.PropsFor("rat", w).Contains("energy"), "属性按类别列出：鼠有体重、没有电量");
            Check(Catalog.PresetsFor("fish").Any(p => p.Id == "alive"), "下级类别也能用上级类别的预设（鱼用活物的“存活”）");
            rat.TraitsOverride = new HashSet<string> { "animal", "itemType:CONTRABAND" };
            Check(Catalog.Matches(w, rat, "itemType:CONTRABAND") && Catalog.TypeName("itemType:CONTRABAND") == "任意CONTRABAND", "游戏商品类型作为类别");
            var ctx = new EvalCtx(w, new RulesData());
            Check(Eval.Cond(ctx, Cond.Of("male"), rat) && !Eval.Cond(ctx, Cond.Of("female"), rat), "文字条件：性别");
            Check(Math.Abs((Eval.Prop(ctx, self, "energyPct") ?? 0) - 50) < 1e-9, "派生属性由目录计算：电量% = 50");
        });

        test("规则·事件：物品处理记录对象 uid 与效果", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var g = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { App("甲", 80, 2, sch.Id) }).Groups[0];
            var ready = g.Rules.First(r => r.Name == "育肥笼出栏");
            var first = ready.Items.First();
            Check(ready.Items.Count == ready.Done[ActVerb.Use] && first.Op == ActVerb.Use && first.Uid > 0 && first.Ap > 0, $"每宰一只记一条事件（{ready.Items.Count}）");
            Check(first.Effects.Any(e => e.Kind == EffectKind.Removed && e.Uid == first.Uid) && first.Effects.Count(e => e.Kind == EffectKind.Created) >= 1, "效果：鼠消失、生肉生成");
            var moved = g.Rules.SelectMany(r => r.Items).FirstOrDefault(i => i.Op == ActVerb.Move);
            Check(moved == null || moved.Effects.Any(e => e.Kind == EffectKind.Moved && e.To > 0), "移动的效果带目标容器");
        });

        test("排班卡片：参数、容器、一句话、卡片上的编辑", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var ps = Card.Parameters(d, sch);
            Check(ps.Count == 7 && ps.All(p => p.Unit != null && p.Hint != null), $"参数 = 规则用到的 7 个常量，都带单位和说明（{ps.Count}）");
            Check(Card.DeviceNames(d, sch).OrderBy(x => x).SequenceEqual(new[] { "精英笼", "育种笼", "育肥笼", "肉类储藏", "工具箱" }.OrderBy(x => x)), "设备名 = 任务用到的 5 个");
            var picked = new WorkRule
            {
                Pre = { new PreCond { RefKind = PreRef.Selector, Ref = "s1", Test = PreTest.NotEmpty }, new PreCond { RefKind = PreRef.Note, Ref = "m1", Test = ">=", Right = Expr.Ref(TermKind.Stat, "s9") } },
                Actions = { new ActionDef { Verb = ActVerb.Move, List = "s2", To = "s3" }, new ActionDef { Verb = ActVerb.Press, List = "s4", To = "stale" }, new ActionDef { Verb = ActVerb.Use, Tool = "s5", List = "s6" } },
            };
            Check(AutoMod.Domain.Rules.Check.PickerRefs(picked).SequenceEqual(new[] { "s1", "s2", "s3", "s4", "s6", "s5" }), "下拉框直接选的定义：不含表达式里的、动作没有显示的参数");
            var ready = sch.Rules.First(r => r.Name == "育肥笼出栏");
            string Line(WorkRule r) => string.Join(" / ", Card.Sentence(d, sch, r).Select(l => string.Concat(l.Select(x => x.Text))));
            Check(Line(ready) == "当 待出栏鼠 不为空 时： / 用 刀具 作用于 待出栏鼠（每件 2 次） / 将 待出栏鼠.数量 累加到 本班宰杀数", Line(ready));
            var segs = Card.Sentence(d, sch, ready).SelectMany(l => l).ToList();
            Check(segs.Any(x => x.Kind == SegKind.Box && x.Param == "tool" && x.Action == 0) && segs.Any(x => x.Kind == SegKind.Op) && segs.Any(x => x.Kind == SegKind.Cond),
                "可点的词：工具、动作、条件");
            Check(Card.ListDefs(d, ready).Any(x => x.StartsWith("待出栏鼠：")) && Card.ListDefs(d, ready).Any(x => x.StartsWith("刀具：")), "展开时显示用到的列表定义（对象和工具）");
            // 换动作：使用 → 添加标记，保留对象列表；再换回使用，工具要重新选
            var a0 = ready.Actions[0];
            var sw = Card.SwitchOp(d, a0, ActVerb.Mark, shop.W);
            Check(sw.Verb == ActVerb.Mark && sw.List == a0.List && sw.Tool == null && sw.Text == "已处理", "换动作保留新动作也有的参数");
            var mv = Card.SwitchOp(d, a0, ActVerb.Move, shop.W);
            Check(mv.Tool == null && mv.List == a0.List && mv.To == null, "换成放入：去掉工具，目标待选");
            Check(Card.Sentence(d, sch, new WorkRule { Actions = { mv } })[1].Any(x => x.Empty && x.Param == "to"), "没选的参数显示为空位");
            // 条件
            Card.SetPre(ready, PreTest.CountGt, 3);
            Check(Card.SimplePre(d, ready) && Card.CondText(ready) == "多于 3 个" && ready.Pre[0].Ref != null, "条件改成“多于 3 个”，列表保留");
            Card.SetPre(ready, null);
            Check(ready.Pre.Count == 0 && Card.CondText(ready) == "每班", "条件改成每班");
            // 上下移
            int i = sch.Rules.IndexOf(ready);
            Check(Card.Move(sch, ready, -1) && sch.Rules.IndexOf(ready) == i - 1 && !Card.Move(sch, sch.Rules[0], -1), "上移；第一条不能再上移");
            // 共用
            var s2 = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            Check(Card.SharedBy(d, ps[0].Id) == 2, "两个排班用到同一个常量时标为共用");
        });

        test("规则·执行：启动开销只在真正执行动作时计", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            sch.Rules.RemoveAll(r => r.Name != "清理死亡鼠");
            var a = App("甲", 40, 0, sch.Id);
            var rep = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { a });
            var rr = rep.Groups[0].Rules[0];
            Check(rr.Pre == PreResult.Fail && rr.Ap == 0, "没有死鼠：前提不满足，不消耗 AP");
            sch.Rules[0].Pre.Clear();
            rep = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { a });
            rr = rep.Groups[0].Rules[0];
            Check(rr.Pre == PreResult.Pass && rr.Ap == 0, "去掉前提：满足但没有可执行对象，仍不消耗 AP");
        });

        test("规则·执行：AP 耗尽时整个排班停止，余下记为未处理", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var a = App("甲", 16, 0, sch.Id);
            var g = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { a }).Groups[0];
            var ready = g.Rules.First(r => r.Name == "育肥笼出栏");
            Check(ready.Done.GetValueOrDefault(ActVerb.Use) > 0 && ready.Deferred > 0, $"宰杀 {ready.Done.GetValueOrDefault(ActVerb.Use)} 只，未处理 {ready.Deferred} 只");
            Check(g.Rules.SkipWhile(r => r.Name != "育肥笼出栏").Skip(1).All(r => r.Pre == PreResult.NotRun), "后面的规则未执行");
            Check(g.ApUsed <= g.ApTotal + 1e-9, $"AP {g.ApUsed}/{g.ApTotal}");
            Check(Rats(shop.Fat1) == 6 - ready.Done[ActVerb.Use], "宰掉的鼠离开了育肥笼");
        });

        test("规则·执行：模组配置的 AP 倍率和成长倍率", () =>
        {
            int Run(double apScale, double growth, out Apprentice a)
            {
                var shop = new RatShop();
                var d = new RulesData();
                var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
                a = App("甲", 16, 0, sch.Id);
                try
                {
                    Scales.Ap = apScale;
                    Scales.Growth = growth;
                    var g = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { a }).Groups[0];
                    Check(g.ApTotal == a.DailyAp && g.ApUsed <= a.DailyAp + 1e-9, $"AP ×{apScale}：用了 {g.ApUsed}/{g.ApTotal}，上限是 {a.DailyAp}");
                    return ApMath.Ceil(g.ApUsed);
                }
                finally { Scales.Ap = Scales.Growth = 1; }
            }
            int base1 = Run(1, 1, out var a1);
            int base2 = Run(2, 1, out var a2);
            Check(a2.MaxAp == 16 && base2 > 16 && base2 > base1, $"AP ×2：基础 16 AP 的学徒这一班能用到 {base2} AP（×1 时 {base1}），存的基础值不变");
            Run(1, 2, out var g2);
            static int Total(Apprentice x) => Enumerable.Range(1, x.Level - 1).Sum(SkillMath.XpToNext) + x.Xp;
            Check(Total(a1) > 0 && Math.Abs(Total(g2) - 2 * Total(a1)) <= 1, $"成长 ×2：同样的活经验 {Total(a1)} → {Total(g2)}");
        });

        test("规则·执行：动作独立（缺剪线钳仍移动）、记事本按次重置", () =>
        {
            var shop = new RatShop(wireCutter: false, breedingCap: 18);
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var a = App("甲", 400, 5, sch.Id);
            var g = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { a }).Groups[0];
            var nb = g.Rules.First(r => r.Name == "新生鼠转入育肥笼");
            Check(nb.Issues.Any(i => i.Code == FailCode.NoTool) && nb.Done.GetValueOrDefault(ActVerb.Move) == 3, "绝育报工具缺失，后面的移动照做（3 只）");
            Check(Rats(shop.Elite, r => r.Str["sex"] == "MALE") == 2, $"精英笼公鼠只留 2 只（{Rats(shop.Elite, r => r.Str["sex"] == "MALE")}）");
            var killed = sch.Notepad.Single();
            Check(g.Memo[killed.Id] == 6, $"本班宰杀数 = 6（{g.Memo[killed.Id]}）");
            shop.W.Night();
            var g2 = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { a }).Groups[0];
            int k2 = g2.Rules.First(r => r.Name == "育肥笼出栏").Done.GetValueOrDefault(ActVerb.Use);
            Check(g2.Memo[killed.Id] == k2, $"下一次执行记事本重新计数（{g2.Memo[killed.Id]} = 本次宰杀 {k2}）");
        });

        test("规则·执行：按学徒顺序，每名学徒串行执行自己的流程，各用自己当天的 AP", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var s1 = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var s2 = new Schedule { Id = "empty", Name = "空排班" };
            d.Schedules.Add(s2);
            var a = App("甲", 20, 0, s1.Id);
            var b = App("乙", 20, 0, s2.Id);
            var c = App("丙", 12, 0, s1.Id);
            var idle = App("丁", 12, 0, null);
            var rep = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { a, b, c, idle });
            Check(rep.Groups.Select(g => g.Workers[0]).SequenceEqual(new[] { "甲", "乙", "丙" }) && rep.Groups[0].ScheduleId == s1.Id, "三组，按学徒顺序；未指派的不执行");
            Check(rep.Groups[0].ApTotal == 20 && rep.Groups[2].ApTotal == 12, "各用自己的 AP");
            Check(rep.Groups.All(g => g.ApUsed == Math.Floor(g.ApUsed)) && rep.Groups.SelectMany(g => g.Rules).All(r => r.Ap == Math.Floor(r.Ap)), "AP 都是整数");
        });

        test("学徒·添加流程：默认选还没排过的流程，都排过了选第一个", () =>
        {
            var app = new Apprentice { Name = "甲" };
            var ids = new[] { "a", "b", "c" };
            var d1 = app.NewDuty(ids); app.Duties.Add(d1);
            var d2 = app.NewDuty(ids); app.Duties.Add(d2);
            var d3 = app.NewDuty(ids); app.Duties.Add(d3);
            var d4 = app.NewDuty(ids);
            Check(d1.ScheduleId == "a" && d2.ScheduleId == "b" && d3.ScheduleId == "c", "依次选 a、b、c，不重复");
            Check(d4.ScheduleId == "a" && d4.Shift == Shift.Morning, "都排过了就选第一个，早班");
            Check(new Apprentice().NewDuty(new[] { "x" }).ScheduleId == "x", "只有一个流程");
        });

        test("规则·执行：一名学徒多个流程；当天的 AP 晚班先用，剩下的留给第二天早班", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var s1 = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var s2 = new Schedule { Id = "empty", Name = "空排班" };
            d.Schedules.Add(s2);
            var a = App("甲", 40, 1, s1.Id, Shift.Night);
            a.Duties.Add(new Duty { ScheduleId = s2.Id, Shift = Shift.Night });
            a.Duties.Add(new Duty { ScheduleId = s1.Id, Shift = Shift.Morning });
            var interp = new Interpreter(shop.W, d);
            var night = interp.RunShift(Shift.Night, new[] { a });
            Check(night.Groups.Select(g => g.ScheduleId).SequenceEqual(new[] { s1.Id, s2.Id }), "晚班：按顺序执行两个流程");
            int usedNight = (int)night.Groups.Sum(g => g.ApUsed);
            Check(usedNight > 0 && a.ApLeft == 40 - usedNight, $"晚班用了 {usedNight} AP，剩 {a.ApLeft}");
            var morning = interp.RunShift(Shift.Morning, new[] { a });
            Check(morning.Groups.Count == 1 && morning.Groups[0].ApTotal == 40 - usedNight, "早班从晚班剩下的 AP 开始");
            Payday.Settle(new[] { a }, 1000);
            Check(a.ApLeft == 40, "发薪时 AP 回满（新的一天）");
        });

        // ================= 模板 =================

        test("规则·模板：相同定义复用、重名加后缀、可选占位对象", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var s1 = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            int sels = d.Selectors.Count, objs = d.Objects.Count;
            var s2 = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            Check(d.Selectors.Count == sels && d.Objects.Count == objs, "同样的绑定再实例化一次：定义全部复用");
            Check(s2.Name == "养鼠日常（2）" && s2.Rules.Count == s1.Rules.Count, "排班重名加后缀");
            var other = shop.Bindings;
            other["elite"] = new() { 1004 };
            Scenarios.Instantiate(d, Scenarios.Get("rats"), other, w: shop.W);
            Check(d.Selectors.Any(x => x.Name == "精英公鼠（2）"), "精英笼换成另一个设备：新建筛选器，名字加后缀");
            Check(d.Selectors.Count(x => x.Name.StartsWith("待出栏鼠")) == 1, "与精英笼无关的挑选器仍复用");
            var water = Scenarios.Instantiate(new RulesData(), Scenarios.Get("water"), new() { ["farms"] = new() { 1 }, ["pitcher"] = new() { 2 }, ["product"] = new() { 3 }, ["spares"] = new() { 4 } });
            Check(water.Rules.Count == 3 && water.Rules.All(r => !r.Name.Contains("净水器") && !r.Name.Contains("标签")), "未绑定的可选占位对象（净水器、工具箱）：相关规则省略");
            foreach (var t in Scenarios.All)
            {
                var dd = new RulesData();
                var b = t.Slots.ToDictionary(x => x.Key, x => new List<int> { 1 });
                var sch = Scenarios.Instantiate(dd, t, b);
                var errs = sch.Rules.SelectMany(r => AutoMod.Domain.Rules.Check.RuleErrors(dd, sch, r)).ToList();
                Check(errs.Count == 0, $"{t.Name} 实例化后没有结构错误：{string.Join("；", errs)}");
                Check(AutoMod.Domain.Rules.Check.FirstBackRef(dd, dd.Selectors) == null, $"{t.Name} 挑选器没有反向引用");
                Check(sch.Rules.All(r => Text.Rule(dd, sch, r).Length > 0), $"{t.Name} 摘要可生成");
            }
        });

        test("规则·养鼠模板：运行 7 天", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var a = App("甲", 140, 10, sch.Id); // 0.6 起 AP 系数 ≥ 1（熟练度）：用 Lv10 熟手、给足 AP，让排班在 7 天内跑到稳态
            var interp = new Interpreter(shop.W, d);
            int meat = 0;
            GroupRunReport last = null;
            for (int day = 0; day < 7; day++)
            {
                if (day > 0) shop.W.Night();
                a.ApUsed = 0; // 新的一天
                last = interp.RunShift(Shift.Morning, new[] { a }).Groups[0];
                meat = shop.Meat.Children.Count;
            }
            // 选优排在超员之后：本班刚降级进育种笼的公鼠，下个班才由超员规则处理
            int demotedM = last.Rules.First(r => r.Name == "精英笼选优 · 公").Details.Count(x => x.Contains("移至 育种笼"));
            Check(meat > 0, $"肉类储藏有生肉 {meat} 块");
            Check(Rats(shop.Elite, r => r.Str["sex"] == "MALE") <= 2 && Rats(shop.Elite, r => r.Str["sex"] == "FEMALE") <= 4, "精英笼按名额保留");
            Check(Rats(shop.Breeding, r => r.Str["sex"] == "MALE" && r.N("age") >= 3) <= 3 + demotedM, "育种笼成年公鼠 ≤ 种公名额（加上本班刚降级的）");
            Check(shop.W.All().Count(x => x.Kind == "rat" && x.N("dead") > 0) == 0, "没有死鼠");
            Check(shop.Fat1.Children.Concat(shop.Fat2.Children).Where(r => r.Kind == "rat" && r.N("age") >= 3).All(r => r.N("neutered") > 0), "育肥笼的成年鼠都已绝育");
        });

        test("规则·电力模板：充电轮转与机器更换电池", () =>
        {
            var w = new SimWorld();
            var bay = w.AddRoot("storage", 1, "机器区");
            var ch = w.Add(bay, "recharger");
            w.Add(ch, "battery_full");
            var full = w.AddRoot("storage", 2, "满电架");
            w.Add(full, "battery_full");
            var pending = w.AddRoot("storage", 3, "待充架");
            for (int i = 0; i < 5; i++) w.Add(pending, "battery_empty");
            var proj = w.AddRoot("projector", 4, "投影仪");
            var low = w.Add(proj, "battery_empty", batterySlot: true);
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("power"), new() { ["bay"] = new() { 1 }, ["full"] = new() { 2 }, ["pending"] = new() { 3 }, ["machines"] = new() { 4 } }, w: w);
            var a = App("甲", 40, 2, sch.Id, Shift.Night);
            var g = new Interpreter(w, d).RunShift(Shift.Night, new[] { a }).Groups[0];
            var slotBat = proj.Children.Single(c => c.InBatterySlot);
            Check(slotBat != low && slotBat.N("energy") == 100, "投影仪换上满电电池");
            Check(low.Parent == full, "交换：换下的电池放到满电电池原来的位置（满电架）");
            Check(ch.Children.Count == 4 && ch.Children.All(b => b.N("energy") < 95), $"充能器装满待充电池（{ch.Children.Count} 块）");
            Check(full.Children.Count(b => b.N("energy") >= 95) >= 1, "已充满的电池转入满电架");
            Check(g.Rules.All(r => r.Pre != PreResult.Broken), "没有结构错误");
        });

        test("规则·常量：改常量会改变挑选结果，预设“可出栏”的数值固定", () =>
        {
            var shop = new RatShop();
            foreach (var r in shop.Fat1.Children.Concat(shop.Fat2.Children)) { r.Num["age"] = 1; r.Num["weight"] = 300; } // 只按体重判断
            var d = new RulesData();
            Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var ready = d.Selectors.Single(x => x.Name == "待出栏鼠");
            var weight = d.Objects.Single(o => o.Name == "出栏体重");
            int Count() => Eval.List(new EvalCtx(shop.W, d), ready.Id).Count;
            foreach (var r in shop.Fat2.Children) r.Num["weight"] = 420;
            weight.Value = 400;
            int at400 = Count();
            weight.Value = 450;
            int at450 = Count();
            Check(at400 == 3 && at450 == 0, $"出栏体重 400 → {at400} 只，450 → {at450} 只");
            Check(Catalog.Presets.All(p => !System.Text.RegularExpressions.Regex.IsMatch(p.Name, @"\d")), "常用条件不带自定的数字门槛");
        });

        test("规则·工具：工具是集合（筛选器），从全店找；找不到报缺少工具", () =>
        {
            // 剪线钳放在一个没登记的工具箱里：工具筛选器改成从全店找
            var shop = new RatShop(wireCutter: false);
            foreach (var r in shop.Fat2.Children) r.Num["age"] = 3; // 育肥笼里 3 只成年（≥3 天）未绝育的公鼠，还不到出栏
            var spare = shop.W.AddRoot("toolbox", 2001, "备用工具箱");
            shop.W.Add(spare, "wire_cutter");
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            d.Selectors.Single(x => x.Name == "剪线钳").From = new List<string> { Places.Shop };
            Check(sch.Rules.All(r => AutoMod.Domain.Rules.Check.RuleErrors(d, sch, r).Count == 0), "工具从全店找：没有结构错误");
            var g = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { App("甲", 400, 5, sch.Id) }).Groups[0];
            var nb = g.Rules.First(r => r.Name == "育肥笼补充绝育");
            Check(nb.Done.GetValueOrDefault(ActVerb.Use) == 3 && shop.Fat2.Children.All(r => r.N("neutered") == 1), $"用备用工具箱里的剪线钳绝育 {nb.Done.GetValueOrDefault(ActVerb.Use)} 只");

            // 店里哪儿都没有剪线钳：缺少工具，不再学徒自带
            var shop2 = new RatShop(wireCutter: false);
            foreach (var r in shop2.Fat2.Children) r.Num["age"] = 3;
            var d2 = new RulesData();
            var sch2 = Scenarios.Instantiate(d2, Scenarios.Get("rats"), shop2.Bindings, w: shop2.W);
            var g2 = new Interpreter(shop2.W, d2).RunShift(Shift.Morning, new[] { App("甲", 400, 5, sch2.Id) }).Groups[0];
            var nb2 = g2.Rules.First(r => r.Name == "育肥笼补充绝育");
            Check(nb2.Done.Count == 0 && nb2.Issues.Any(i => i.Code == FailCode.NoTool && i.Text.Contains("缺少工具") && i.Text.Contains("剪线钳")), "报缺少工具，写明缺的是哪个工具");
        });

        test("规则·激活、开关：游戏决定能否执行，产物去向，次数，需要玩家操作", () =>
        {
            var w = FakeGame.Install(new SimWorld());
            var box = w.AddRoot("storage", 1, "储藏");
            var prize = w.AddRoot("storage", 2, "奖品箱");
            var area = w.AddRoot("storage", 3, "充电区");
            for (int i = 0; i < 3; i++) w.Add(box, "battery_full");
            w.Add(box, "other");
            var ch = w.Add(area, "recharger");
            var d = new RulesData();
            Named(w, "box", 1); Named(w, "prize", 2); Named(w, "area", 3);
            Sel(d, "all", "#box", "battery|other");
            Sel(d, "chargers", "#area", "recharger");
            var sch = new Schedule { Id = "s", Name = "s" };
            sch.Rules.Add(new WorkRule { Id = "a", Name = "激活", Actions = { new ActionDef { Verb = ActVerb.Activate, List = "all" } } });
            sch.Rules.Add(new WorkRule { Id = "t", Name = "开关", Actions = { new ActionDef { Verb = ActVerb.Toggle, List = "chargers", Times = 5 } } });
            d.Schedules.Add(sch);
            Check(sch.Rules.All(r => AutoMod.Domain.Rules.Check.RuleErrors(d, sch, r).Count == 0), "激活、开关的规则没有结构错误");
            var g = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 5, sch.Id) }).Groups[0];
            var act = g.Rules[0];
            Check(act.Done.GetValueOrDefault(ActVerb.Activate) == 3 && prize.Children.Count == 0 && box.Children.Count(c => c.Kind == "other") == 4, $"3 节电池激活，产物留在原处（{box.Children.Count}）");
            Check(act.Issues.Any(i => i.Code == FailCode.NeedsPlayer), "文件：需要玩家操作");
            Check(ch.N("on") == 1 && g.Rules[1].Done.GetValueOrDefault(ActVerb.Toggle) == 1, "开关只切换一次（次数对开关无效）");
            Check(g.Rules[1].Items[0].Ap == Math.Floor(g.Rules[1].Items[0].Ap) && g.Rules[1].Ap >= 1, "开关的 AP 是整数");
            // 按按钮：机器上有这个按钮才按；没选按钮是结构错误
            ch.Buttons.Add("开始");
            sch.Rules.Clear();
            sch.Rules.Add(new WorkRule { Id = "p", Name = "按按钮", Actions = { new ActionDef { Verb = ActVerb.Press, List = "chargers", Button = "开始", Times = 2 } } });
            sch.Rules.Add(new WorkRule { Id = "q", Name = "没有的按钮", Actions = { new ActionDef { Verb = ActVerb.Press, List = "chargers", Button = "停止" } } });
            var bad = new WorkRule { Id = "x", Name = "未选", Actions = { new ActionDef { Verb = ActVerb.Press, List = "chargers" } } };
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, sch, bad).Any(e => e.Contains("未选择按钮")), "按按钮：没选按钮报错");
            var g3 = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 5, sch.Id) }).Groups[0];
            Check(ch.N("pressed") == 2 && g3.Rules[0].Done.GetValueOrDefault(ActVerb.Press) == 1, $"按两次【开始】（{ch.N("pressed")}）");
            Check(g3.Rules[1].Done.Count == 0 && g3.Rules[1].Issues.Any(i => i.Code == FailCode.NotAllowed), "机器上没有的按钮：跳过");
            Check(AutoMod.Domain.Rules.Card.Sentence(d, sch, sch.Rules[0]).SelectMany(l => l).Any(sg => sg.Param == "button" && sg.Text == "【开始】"), "卡片：按 [对象] 的【按钮】");
            sch.Rules.Clear();
            sch.Rules.Add(new WorkRule { Id = "a", Name = "激活", Actions = { new ActionDef { Verb = ActVerb.Activate, List = "all" } } });
            var card = AutoMod.Domain.Rules.Card.Sentence(d, sch, sch.Rules[0]);
            Check(card.SelectMany(l => l).Any(sg => sg.Kind == SegKind.Op && sg.Text == "激活"), "卡片：激活 [对象]");
        });

        test("规则·使用的方向由游戏判断：用水龙头作用于空桶 = 空桶接水", () =>
        {
            var w = FakeGame.Install(new SimWorld());
            var box = w.AddRoot("storage", 1, "空桶区");
            for (int i = 0; i < 2; i++) w.Add(box, "jug_empty");
            var faucet = new SimItem { Id = 900, Kind = "faucet", Name = "水龙头" };
            w.FixtureItems[Fixtures.Faucet] = faucet;
            var d = new RulesData();
            Named(w, "box", 1);
            Sel(d, "jugs", "#box", "water_jug");
            var sch = new Schedule { Id = "s", Name = "s" };
            sch.Rules.Add(new WorkRule { Id = "f", Name = "接水", Actions = { new ActionDef { Verb = ActVerb.Use, List = "jugs", Tool = Fixtures.Faucet } } });
            d.Schedules.Add(sch);
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, sch, sch.Rules[0]).Count == 0, "工具来源写水龙头不算结构错误：" + string.Join("；", AutoMod.Domain.Rules.Check.RuleErrors(d, sch, sch.Rules[0])));
            var g = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 5, sch.Id) }).Groups[0];
            Check(box.Children.All(j => j.N("volumeMl") == j.N("capacityMl")) && g.Rules[0].Done.GetValueOrDefault(ActVerb.Use) == 2, "两个空桶都接满了");
            var line = string.Concat(AutoMod.Domain.Rules.Card.Sentence(d, sch, sch.Rules[0])[1].Select(sg => sg.Text));
            Check(line.StartsWith("用 水龙头 作用于"), $"卡片：{line}");
            var g2 = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 5, sch.Id) }).Groups[0];
            Check(g2.Rules[0].Issues.Any(i => i.Code == FailCode.NoEffect) && g2.ApUsed == 0, "已满：没有变化，不扣 AP");
        });

        test("执行顺序：按学徒列表的顺序；后面的流程看到前面处理后的状态", () =>
        {
            var w = new SimWorld();
            var m = w.AddRoot("machine", 1, "机器");
            w.Add(m, "battery_empty", batterySlot: true);
            var shelf = w.AddRoot("storage", 2, "仓库");
            for (int i = 0; i < 2; i++) w.Add(shelf, "battery_full");
            var d = new RulesData();
            Named(w, "m", 1);
            Named(w, "shelf", 2);
            Sel(d, "low", "#m", "battery", s => { s.Slot = "battery"; s.Where = new CondGroup { Items = { Cond.Cmp("energyPct", "<", Expr.Of(30)) } }; });
            Sel(d, "spare", "#shelf", "battery");
            Schedule Sch(string id) =>
                new() { Id = id, Name = id, Rules = { new WorkRule { Id = id + "r", Name = "换电池", Actions = { new ActionDef { Verb = ActVerb.Swap, List = "low", To = "spare" } } } } };
            var first = Sch("电池轮转");
            var second = Sch("水培");
            d.Schedules.Add(first);
            d.Schedules.Add(second);
            // 流程列表里电池轮转在前，但学徒列表里做水培的学徒在前：先执行水培
            var run = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 80, 3, "电池轮转"), App("乙", 80, 3, "水培") });
            Check(run.Groups.Select(g => g.ScheduleName).SequenceEqual(new[] { "电池轮转", "水培" }), "按学徒列表的顺序执行");
            Check(run.Groups[0].Rules[0].Done.GetValueOrDefault(ActVerb.Swap) == 1 && run.Groups[1].Rules[0].Done.Count == 0 && run.Groups[1].ApUsed == 0,
                "电池轮转换过了，水培再筛选时没有低电量电池，不做也不花 AP");
        });

        test("执行：筛选结果在没动过店里的任务之间接着用，动过之后重新筛", () =>
        {
            var w = new SimWorld();
            var m = w.AddRoot("machine", 1, "机器");
            w.Add(m, "battery_empty", batterySlot: true);
            var shelf = w.AddRoot("storage", 2, "仓库");
            for (int i = 0; i < 2; i++) w.Add(shelf, "battery_full");
            var bin = w.AddRoot("storage", 3, "回收箱");
            var d = new RulesData();
            Named(w, "m", 1);
            Named(w, "shelf", 2);
            Named(w, "bin", 3);
            Sel(d, "low", "#m", "battery", s => { s.Slot = "battery"; s.Where = new CondGroup { Items = { Cond.Cmp("energyPct", "<", Expr.Of(30)) } }; });
            Sel(d, "spare", "#shelf", "battery");
            PreCond Empty(string sel) => new() { Ref = sel, Test = PreTest.Empty };
            var swap = new ActionDef { Verb = ActVerb.Swap, List = "low", To = "spare" };
            var store = new ActionDef { Verb = ActVerb.Move, List = "spare", To = "#bin" };
            var sch = new Schedule
            {
                Id = "s", Name = "s", Rules =
                {
                    // 条件不成立：两个筛选器都求值了，但什么都没动
                    new WorkRule { Id = "r1", Name = "空转", Join = "any", Pre = { Empty("low"), Empty("spare") }, Actions = { swap } },
                    new WorkRule { Id = "r2", Name = "换电池", Actions = { swap } },
                    // 上一条动过店里：这里必须重新筛，低电量电池已经换下来了
                    new WorkRule { Id = "r3", Name = "再换", Actions = { swap } },
                    // 上一条没做事：接着用它筛出的结果，照样把仓库里的两块搬走
                    new WorkRule { Id = "r4", Name = "收走", Actions = { store } },
                    // 上一条搬走了：重新筛，仓库空了，条件成立
                    new WorkRule { Id = "r5", Name = "仓库空了", Pre = { Empty("spare") }, Actions = { store } },
                },
            };
            d.Schedules.Add(sch);
            var rules = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 3, "s") }).Groups[0].Rules;
            Check(rules.All(r => r.Pre != PreResult.Broken), "没有结构错误：" + string.Join("；", rules.SelectMany(r => r.Issues).Select(i => i.Text)));
            Check(rules[0].Pre == PreResult.Fail && rules[1].Done.GetValueOrDefault(ActVerb.Swap) == 1, "空转的任务之后照常换电池");
            Check(rules[2].Done.Count == 0, "换过之后重新筛选：没有低电量电池可换");
            Check(rules[3].Done.GetValueOrDefault(ActVerb.Move) == 2 && bin.Children.Count == 2, $"仓库里的两块搬进回收箱：{bin.Children.Count}");
            Check(rules[4].Pre == PreResult.Pass && rules[4].Done.Count == 0, "搬走之后重新筛选：仓库空了");
        });

        test("导入导出：设备名照用，定义归新排班，一样的共用定义复用，重名改名", () =>
        {
            var d = new RulesData();
            d.Objects.Add(new ObjectDef { Id = "th", Name = "阈值", Value = 30, Owner = "sch" });
            d.Objects.Add(new ObjectDef { Id = "gk", Name = "共用阈值", Value = 50 });
            Sel(d, "shared", Places.Shop, "battery", s => s.Where = new CondGroup { Items = { Cond.Cmp("energyPct", ">=", Expr.Ref(TermKind.Const, "gk")) } });
            Sel(d, "low", "#水培机", "battery", s => { s.Slot = "battery"; s.Where = new CondGroup { Items = { Cond.Cmp("energyPct", "<", Expr.Ref(TermKind.Const, "th")) } }; s.Owner = "sch"; });
            Sel(d, "unused", "#仓库", "rat");
            var sch = new Schedule { Id = "sch", Name = "水培", Rules = { new WorkRule { Id = "r", Name = "换电池", Actions = { new ActionDef { Verb = ActVerb.Swap, List = "low", To = "shared" } } } } };
            d.Schedules.Add(sch);
            string text = Transfer.Export(d, sch, "test");
            Check(text.StartsWith(Transfer.Marker) && !text.Contains("\"unused\"") && text.Contains("#水培机"), "导出：带标记，只带用到的和本排班的定义，设备名原样");

            // 导回同一份数据：共用的直接用，本排班的换新
            var s1 = Transfer.Import(d, text);
            Check(s1.Id != "sch" && s1.Name == "水培（2）" && s1.Rules[0].Id != "r", $"新排班：{s1.Name}");
            Check(d.Objects.Count(o => o.Owner == s1.Id) == 1 && d.Selectors.Count(x => x.Owner == s1.Id) == 1, "本排班的参数、筛选器归新排班");
            Check(d.Selectors.Count(x => x.Name == "shared") == 1 && s1.Rules[0].Actions[0].To == "shared" && d.Objects.Count(o => o.Name == "共用阈值") == 1, "一样的共用定义直接用");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, s1, s1.Rules[0]).Count == 0, "引用都对：" + string.Join("；", AutoMod.Domain.Rules.Check.RuleErrors(d, s1, s1.Rules[0])));
            var low2 = d.Selectors.Single(x => x.Owner == s1.Id);
            Check(low2.Where.Items[0].Right.Terms[0].Ref == d.Objects.Single(o => o.Owner == s1.Id).Id, "条件里的参数换成新的");

            // 导进一份空数据：店里还没有这些设备名
            var fresh = new RulesData();
            var s2 = Transfer.Import(fresh, text);
            Check(fresh.Schedules.Count == 1 && s2.Name == "水培", "空店：排班名不变");
            var missing = AutoMod.Domain.Rules.Check.MissingNames(fresh, s2.Rules.SelectMany(AutoMod.Domain.Rules.Check.RuleRefs), new SimWorld());
            Check(missing.SequenceEqual(new[] { "水培机" }), "列出店里还没有的设备名：" + string.Join("、", missing));
            bool bad = false;
            try { Transfer.Import(fresh, "随便一段话"); } catch (TransferException) { bad = true; }
            Check(bad, "不是排班的文字：报错，不改数据");
        });

        test("模拟·液体：接水龙头、倒进别的容器、倒进下水道", () =>
        {
            var w = new SimWorld();
            var box = w.AddRoot("storage", 1, "仓库");
            var jug = w.Add(box, "jug_empty");
            var pitcher = w.AddRoot("water_pitcher", 2, "水罐");
            var faucet = new SimItem { Id = 900, Kind = "faucet", Name = "水龙头" };
            var drain = new SimItem { Id = 901, Kind = "drain", Name = "下水道" };
            w.FixtureItems[Fixtures.Faucet] = faucet;
            w.FixtureItems[Fixtures.Drain] = drain;
            var r1 = w.Apply(jug, faucet, new List<object>());
            Check(r1.Ok && jug.N("volumeMl") == jug.N("capacityMl") && Math.Abs(jug.N("purity") - SimWorld.FaucetPurity) < 0.01, $"接满水龙头的水：{r1.Text}");
            Check(w.Num(jug, "liq:water") == jug.N("volumeMl"), "含水（ml）跟着水量变");
            Check(w.Apply(jug, faucet, new List<object>()).Code == FailCode.NoEffect, "已满再接：没有变化");
            double had = jug.N("volumeMl");
            var r2 = w.Apply(jug, pitcher, new List<object>());
            Check(r2.Ok && pitcher.N("volumeMl") == Math.Min(had, pitcher.N("capacityMl")) && jug.N("volumeMl") == had - pitcher.N("volumeMl"), $"倒进水罐：{r2.Text}");
            Check(w.Apply(pitcher, drain, new List<object>()).Ok && pitcher.N("volumeMl") == 0, "倒进下水道：倒空");
            var merged = AutoMod.Domain.Rules.Text.MergeSteps(new[] { "桶 → 水箱：水量 (ml) 0 → 6000，水质 (%) 0 → 90", "桶 → 水箱：水量 (ml) 6000 → 12000，水质 (%) 90 → 90" });
            Check(merged == "桶 → 水箱：水量 (ml) 0 → 12000，水质 (%) 0 → 90", $"连做两次合成一句：{merged}");
        });

        test("规则·使用：工具没有效果（空桶）就换下一件，再试", () =>
        {
            var w = FakeGame.Install(new SimWorld());
            var src = w.AddRoot("storage", 1, "水源");
            w.Add(src, "jug_empty");
            w.Add(src, "jug_dirty");
            w.Add(src, "jug_dirty");
            var dst = w.AddRoot("storage", 2, "目标");
            var tank = w.Add(dst, "jug_empty");
            var d = new RulesData();
            Named(w, "src", 1); Named(w, "dst", 2);
            Sel(d, "tanks", "#dst", "water_jug");
            var sch = new Schedule { Id = "s", Name = "s" };
            sch.Rules.Add(new WorkRule { Id = "p", Name = "加水", Actions = { new ActionDef { Verb = ActVerb.Use, List = "tanks", Tool = "#src", Times = 2 } } });
            d.Schedules.Add(sch);
            var g = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 5, sch.Id) }).Groups[0];
            Check(tank.N("volumeMl") == 6000 && g.Rules[0].Done.GetValueOrDefault(ActVerb.Use) == 1, $"跳过空桶，用两桶脏水倒满（{tank.N("volumeMl")}ml）");
        });

        test("示例·各职业示例不用设置就没有结构错误", () =>
        {
            foreach (var spec in ProfessionExamples.All)
            {
                var d = new RulesData();
                var s = ProfessionExamples.Build(d, spec.Id);
                var errs = s.Rules.SelectMany(r => AutoMod.Domain.Rules.Check.RuleErrors(d, s, r)).Distinct().ToList();
                Check(s.Rules.Count >= 2 && errs.Count == 0, $"{spec.Name}：{s.Rules.Count} 个任务；{string.Join("；", errs)}");
            }
        });

        test("示例·放料、按按钮只找有电的机器（没电的备用机不算），检查种子槽、芯片槽也只看这些机器", () =>
        {
            bool Powered(SelectorDef x) => x.Where.Items.Any(c => c.Prop == "powered");
            foreach (var (id, machine, slots) in new[] { ("hydro", "空闲水培机", new[] { "种子槽中的种子" }), ("gun", "空闲打印机", new[] { "芯片槽", "输入槽中的耗材" }) })
            {
                var d = new RulesData();
                ProfessionExamples.Build(d, id);
                var m = d.Selectors.First(x => x.Name == machine);
                Check(Powered(m) && slots.All(n => d.Selectors.First(x => x.Name == n).From.SequenceEqual(new[] { m.Id })), $"{id}：{machine} 要求有电，{string.Join("、", slots)} 只看它");
            }
            var w = new RulesData();
            ProfessionExamples.Build(w, "water");
            Check(new[] { "水瓶打印机", "净水器", "售水机" }.All(n => Powered(w.Selectors.First(x => x.Name == n))), "卖水：水瓶打印机、净水器、售水机都要求有电");
            var aq = new RulesData();
            ProfessionExamples.Build(aq, "aqua");
            Check(aq.Selectors.First(x => x.Name == "闲置的桶").Take?.N?.Terms[0].Num == 2, "水产养殖：只取两个桶接水（水箱空时不会把 AP 全花在接水上）");
        });

        test("示例·整理：散放的药品放进【药品仓库】（可以有几个），已在里面的、机器里的不动", () =>
        {
            var w = FakeGame.Install(new SimWorld());
            var shelf = w.AddRoot("storage", 1, "杂物架");
            var med1 = w.AddRoot("storage", 2, "药品仓库甲");
            var med2 = w.AddRoot("storage", 3, "药品仓库乙");
            var machine = w.AddRoot("machine", 4, "机器");
            SimItem Med(SimItem box) { var x = w.Add(box, "other", force: true); x.TraitsOverride = new HashSet<string> { "itemType:MEDICAL" }; return x; }
            var loose = Med(shelf);
            var kept = Med(med2);
            var inMachine = Med(machine);
            Named(w, "药品仓库", 2, 3);
            var d = new RulesData();
            var s = ProfessionExamples.Build(d, "tidy");
            var a = App("甲", 25, 1, s.Id);
            new Interpreter(w, d).RunShift(Shift.Morning, new[] { a });
            Check(loose.Parent == med1 || loose.Parent == med2, "散放的药品放进了一个【药品仓库】");
            Check(kept.Parent == med2 && inMachine.Parent == machine, "已在【药品仓库】里的、机器里的不动");
        });

        test("标签规则：液体成分、上限配对、内部标签、鼠和鱼共用活物的属性与预设", () =>
        {
            Check(GameTags.LiquidOf("CURRENT_PART_ORGANIC_WASTE") == "organic_waste" && GameTags.LiquidOf("FISH_AGE_TAG") == null, "液体成分");
            var keys = new HashSet<string> { "ANIMAL_HUNGER_TAG", "ANIMAL_MAX_HUNGER_TAG", "DURABILITY_TAG", "MAX_DURABILITY_TAG", "power_source_item_energy", "power_source_item_max_energy", "FISH_AGE_TAG" };
            Check(GameTags.MaxOf("ANIMAL_HUNGER_TAG", keys) == "ANIMAL_MAX_HUNGER_TAG", "ANIMAL_HUNGER → ANIMAL_MAX_HUNGER");
            Check(GameTags.MaxOf("DURABILITY_TAG", keys) == "MAX_DURABILITY_TAG", "DURABILITY → MAX_DURABILITY");
            Check(GameTags.MaxOf("power_source_item_energy", keys) == "power_source_item_max_energy", "小写键");
            Check(GameTags.MaxOf("FISH_AGE_TAG", keys) == null && GameTags.MaxOf("ANIMAL_MAX_HUNGER_TAG", keys) == null, "没有上限 / 本身就是上限");
            var mk = new HashSet<string> { "MACHINE_PROGRESS_CURRENT_TAG", "MACHINE_PROGRESS_TARGET_TAG", "GROWING_PROGRESS", "REQUIRED_PROGRESS", "CAGE_HEALTH_TAG", "CAGE_MAX_HEALTH_TAG" };
            Check(GameTags.MaxOf("MACHINE_PROGRESS_CURRENT_TAG", mk) == "MACHINE_PROGRESS_TARGET_TAG", "机器进度 CURRENT → TARGET");
            Check(GameTags.MaxOf("GROWING_PROGRESS", mk) == "REQUIRED_PROGRESS", "生长进度 → REQUIRED_PROGRESS");
            Check(GameTags.MaxOf("CAGE_HEALTH_TAG", mk) == "CAGE_MAX_HEALTH_TAG" && GameTags.MaxOf("MACHINE_PROGRESS_TARGET_TAG", mk) == null, "笼子耐久；目标本身不配对");
            Check(GameTags.IsInternal("FISH_ITEM_TAG") && GameTags.IsInternal("IS_OWNED_TAG") && GameTags.IsInternal("DURABILITY_FAKE_TAG") && GameTags.IsInternal("ASSOCIATED_HYDROPONIC_ID")
                  && !GameTags.IsInternal("FISH_HUNGER_TAG") && !GameTags.IsInternal("MACHINE_STATE_TAG"), "内部标签");
            Check(Catalog.Satisfies("fish", "livestock") && Catalog.Satisfies("animal", "livestock") && !Catalog.Satisfies("fish", "animal"), "鼠、鱼都是活物");
            var fishProps = Catalog.PropsFor("fish").ToList();
            Check(fishProps.Contains("dead") && fishProps.Contains("hungerPct") && fishProps.Contains("gravid") && !fishProps.Contains("neutered"), "鱼：共用属性 + 怀卵，没有绝育");
            Check(Catalog.PresetsFor("fish").Any(p => p.Id == "dead") && Catalog.PresetsFor("fish").Any(p => p.Id == "female") && Catalog.PresetsFor("fish").All(p => p.Id != "unneut"), "鱼：存活 / 已死亡 / 雌雄预设可用，绝育预设不可用");
        });

        test("分类：物品家族类别、品质 / 液体 / % 属性、滤芯算有耐久的、属性名可由游戏提供", () =>
        {
            Check(Catalog.IsTrait("family:CONTRABAND") && Catalog.TypeName("family:CONTRABAND") == "任意CONTRABAND_ITEM_TAG", "家族类别：默认显示原始键名");
            var old = Catalog.FamilyName;
            Catalog.FamilyName = f => f == "CONTRABAND" ? "违禁品" : f;
            Check(Catalog.TypeName("family:CONTRABAND") == "任意违禁品", "家族类别：游戏侧给中文名");
            Catalog.FamilyName = old;
            Check(new[] { "q:CATEGORY_WATER_PURITY", "qt:CATEGORY_WATER_PURITY", "qok:X", "liq:water", "pct:CAGE_HEALTH_TAG", "has:FISH_IS_DEAD_TAG" }.All(GameTags.IsDynamicProp), "动态属性都认得");
            Check(GameTags.IsTextProp("qt:CATEGORY_WATER_PURITY") && GameTags.IsTextProp("str:MACHINE_STATE_TAG") && !GameTags.IsTextProp("q:CATEGORY_WATER_PURITY"), "文字属性");
            Check(Catalog.Satisfies("filter", "durable") && Catalog.PropsFor("filter").Contains("durabilityPct") && Catalog.PropsFor("durable").Contains("durability"), "滤芯算有耐久的");
            var hook = Catalog.PropNameHook;
            Catalog.PropNameHook = id => id == "health" ? "Health" : null;
            Check(Catalog.PropName("health") == "Health" && Catalog.PropName("weight") == "体重", "属性名：游戏给的优先，其余用目录");
            Catalog.PropNameHook = hook;
            var d = new RulesData();
            var sch = new Schedule { Id = "s", Name = "s" };
            Sel(d, "jugs", "#b", "liquid", x => x.Where.Items.Add(new Cond { Prop = "qt:CATEGORY_WATER_PURITY", Op = "=", Text = "基础水" }));
            Check(AutoMod.Domain.Rules.Check.SelectorErrors(d, d.Selectors[0]).Count == 0, "文字条件（品质等级名）没有结构错误");
        });

        test("条件：类型是 / 不是（排除违禁品）", () =>
        {
            var w = new SimWorld();
            var box = w.AddRoot("storage", 1, "仓库");
            w.Add(box, "meat");
            var dust = w.Add(box, "other");
            dust.TraitsOverride = new HashSet<string> { "family:CONTRABAND" };
            var d = new RulesData();
            Named(w, "b", 1);
            Sel(d, "goods", "#b", Catalog.Any, x => x.Where.Items.Add(new Cond { Prop = Catalog.TypeProp, Op = "!=", Text = "family:CONTRABAND" }));
            var list = Eval.List(new EvalCtx(w, d), "goods");
            Check(list.Count == 1 && !list.Contains(dust), "不是违禁品：只剩生肉");
            Check(AutoMod.Domain.Rules.Check.SelectorErrors(d, d.Selectors[0]).Count == 0, "类型条件没有结构错误");
            Check(AutoMod.Domain.Rules.Text.Cond(d, d.Selectors[0].Where.Items[0]).StartsWith("不是"), "文字：不是…");
        });

        test("模拟·夜间：水培种子长成收成、鱼吃饲料长大、没饲料饿死", () =>
        {
            var w = new SimWorld();
            var hydro = w.AddRoot("storage", 1, "水培机");
            hydro.Num["speed"] = 8;
            hydro.TraitsOverride = new HashSet<string> { "container", "machine" };
            var water = w.Add(hydro, "jug_dirty");
            water.InSlot = SlotNames.Prefix + "water";
            var seed = w.Add(hydro, "other");
            seed.TraitsOverride = new HashSet<string> { "seed" };
            seed.InSlot = SlotNames.Prefix + "seed";
            seed.Num["growRequired"] = 23; seed.Num["waterUse"] = 100; seed.Num["harvestQty"] = 2; seed.Str["harvestKind"] = "nutrifruit";
            w.Night(); w.Night();
            Check(seed.N("growPct") > 60 && seed.N("growPct") < 100 && water.N("volumeMl") == 2800, $"两晚：进度 {seed.N("growPct"):0}%，水 {water.N("volumeMl")}ml");
            w.Night();
            var crops = hydro.Children.Where(c => c.InSlot == SlotNames.Prefix + "output").ToList();
            Check(crops.Count == 2 && crops.All(c => c.Kind == "nutrifruit") && !hydro.Children.Contains(seed), "第三晚收成 2 个营养果进产出格，种子用掉");

            var tank = w.AddRoot("storage", 2, "鱼缸");
            SimItem Fish(double hunger) { var f = w.Add(tank, "other"); f.TraitsOverride = new HashSet<string> { "fish" }; f.Num["maxHunger"] = 700; f.Num["hunger"] = hunger; f.Num["age"] = 6; f.Num["adultAge"] = 8; return f; }
            var fed = Fish(600);
            var feed = w.Add(tank, "other"); feed.Num["calories"] = 5000;
            for (int i = 0; i < 3; i++) w.Night();
            Check(fed.N("age") == 9 && w.Num(fed, "mature") == 1 && feed.N("calories") < 5000 && fed.N("dead") == 0, $"有饲料：长到 9 天成年，饲料 {feed.N("calories"):0}");
            var tank2 = w.AddRoot("storage", 3, "空鱼缸");
            var hungry = w.Add(tank2, "other"); hungry.TraitsOverride = new HashSet<string> { "fish" }; hungry.Num["maxHunger"] = 700; hungry.Num["hunger"] = 100;
            for (int i = 0; i < 4; i++) w.Night();
            Check(hungry.N("dead") == 1 && w.Num(hungry, "dead") == 1, "没饲料：饿到 0 后下一晚死");
        });

        test("规则·未知动作（旧版的绝育 / 宰杀等）报结构错误，不当成移动执行", () =>
        {
            var shop = new RatShop();
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("rats"), shop.Bindings, w: shop.W);
            var r = sch.Rules.First(x => x.Actions.Any(a => a.Verb == ActVerb.Use));
            r.Actions.First(a => a.Verb == ActVerb.Use).Verb = "butcher";
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, sch, r).Any(e => e.Contains("未知动作")), "报未知动作");
            var g = new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { App("甲", 400, 5, sch.Id) }).Groups[0];
            Check(g.Rules.First(x => x.Name == r.Name).Done.Count == 0, "有结构错误的任务不执行");
        });

        test("规则·使用：次数、换工具、没有效果、需要玩家操作", () =>
        {
            var w = FakeGame.Install(new SimWorld());
            var cage = w.AddRoot("animal_cage", 1, "笼");
            var meat = w.AddRoot("storage", 2, "肉类储藏");
            var tools = w.AddRoot("toolbox", 3, "工具箱");
            w.Add(tools, "knife");
            w.Add(tools, "labeler");
            w.Add(tools, "other"); // 假游戏里扮演放大镜
            for (int i = 0; i < 2; i++) w.Add(cage, "rat_m", r => r.Num["weight"] = 320);
            var d = new RulesData();
            Named(w, "cage", 1); Named(w, "meat", 2); Named(w, "tools", 3);
            Sel(d, "rats", "#cage", "rat");
            var jug = w.AddRoot("jug_empty", 4, "水桶");
            Named(w, "jug", 4);
            Sel(d, "jugs", "#jug", Catalog.Self);
            var sch = new Schedule { Id = "s", Name = "s" };
            Sel(d, "knives", "#tools", "blade");
            Sel(d, "labelers", "#tools", "labeler");
            Sel(d, "looks", "#tools", "other");
            sch.Rules.Add(new WorkRule { Id = "kill", Name = "宰杀", Actions = { new ActionDef { Verb = ActVerb.Use, List = "rats", Tool = "knives", Times = 2 } } });
            d.Schedules.Add(sch);
            var g = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 5, sch.Id) }).Groups[0];
            var kill = g.Rules[0];
            Check(kill.Done[ActVerb.Use] == 2 && cage.Children.All(c => c.Kind == "raw_meat") && cage.Children.Count == 4, $"每只作用两次：先杀死再分解，生肉留在笼里（{cage.Children.Count} 块）");
            Check(kill.Ap == ApMath.Ceil(Interpreter.Walk + kill.Done[ActVerb.Use] * 2 * Catalog.Op(ActVerb.Use).Cost * g.Mult), $"AP 按次数计：{kill.Ap}");

            // 标签打印机：游戏允许但没有变化 → 跳过，不扣 AP；放大镜：要打开界面 → 需要玩家操作，不扣 AP
            for (int i = 0; i < 2; i++) w.Add(cage, "rat_m");
            sch.Rules.Clear();
            sch.Rules.Add(new WorkRule { Id = "label", Name = "贴标签", Actions = { new ActionDef { Verb = ActVerb.Use, List = "jugs", Tool = "labelers" } } });
            sch.Rules.Add(new WorkRule { Id = "look", Name = "查看", Actions = { new ActionDef { Verb = ActVerb.Use, List = "rats", Tool = "looks" } } });
            var g2 = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 200, 5, sch.Id) }).Groups[0];
            Check(g2.Rules[0].Done.Count == 0 && g2.Rules[0].Issues.Any(i => i.Code == FailCode.NoEffect) && g2.ApUsed == 0, "没有效果、需要玩家操作都不扣 AP");
            Check(g2.Rules[1].Issues.Any(i => i.Code == FailCode.NeedsPlayer) && g2.Rules[1].Items.All(e => e.Outcome == Outcomes.Issue), "放大镜：需要玩家操作");
        });

        test("规则·作用域：可见性、依赖方向、重名、提升、复制与删除排班、存档", () =>
        {
            var d = new RulesData();
            var a = new Schedule { Id = "sa", Name = "电池轮转" };
            var b = new Schedule { Id = "sb", Name = "养鼠" };
            d.Schedules.Add(a); d.Schedules.Add(b);
            d.Objects.Add(new ObjectDef { Id = "g_k", Name = "机器区", Value = 1 });
            d.Objects.Add(new ObjectDef { Id = "a_full", Name = "满电架", Value = 3, Owner = "sa" });
            d.Objects.Add(new ObjectDef { Id = "a_k", Name = "满电标准", Value = 95, Owner = "sa" });
            Sel(d, "a_ch", Places.Shop, "recharger", x => x.Owner = "sa");
            Sel(d, "g_ch", Places.Shop, "recharger");
            Sel(d, "a_bat", "a_ch", "battery", x => { x.Owner = "sa"; x.Where.Items.Add(Cond.Cmp("energyPct", ">=", Expr.Ref(TermKind.Const, "a_k"))); });
            a.Rules.Add(new WorkRule { Id = "r1", Name = "取出", Actions = { new ActionDef { Verb = ActVerb.Move, List = "a_bat", To = "#满电架" } } });

            Scope.Normalize(d);
            Check(d.Selectors.Select(x => x.Id).SequenceEqual(new[] { "g_ch", "a_ch", "a_bat" }), "整理后全局挑选器在本排班的上方，各自保持顺序");
            Check(Scope.Visible(d, "sa", "a_full") && !Scope.Visible(d, "sb", "a_full") && Scope.Visible(d, "sb", "g_k") && Scope.Visible(d, "sb", "#满电架"), "本排班定义只有所属排班看得到，全局的、设备名都看得到");
            Check(Scope.Selectors(d, "sa").First().Id == "a_ch" && Scope.Selectors(d, null).All(x => x.Owner == null), "下拉选项本排班的在前；全局视角只有全局");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, a, a.Rules[0]).Count == 0, "本排班任务引用本排班 + 全局定义：没有错误");

            // 依赖方向
            var bad = new WorkRule { Id = "r2", Name = "越界", Actions = { new ActionDef { Verb = ActVerb.Move, List = "a_bat", To = "#满电架" } } };
            b.Rules.Add(bad);
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, b, bad).Any(e => e.Contains("其他流程的本流程定义")), "其他排班的任务引用本排班定义：结构错误");
            b.Rules.Remove(bad);
            d.Selectors.Single(x => x.Id == "g_ch").Where.Items.Add(Cond.Cmp("energyPct", ">", Expr.Ref(TermKind.Const, "a_full")));
            Check(AutoMod.Domain.Rules.Check.SelectorErrors(d, d.Selectors.Single(x => x.Id == "g_ch")).Any(e => e.Contains("全局定义不能引用本流程")), "全局挑选器引用本排班定义：结构错误");
            d.Selectors.Single(x => x.Id == "g_ch").Where.Items.Clear();

            // 重名
            Check(Scope.NameConflict(d, false, "机器区", "sa") != null, "本排班对象不能和全局对象重名");
            Check(Scope.NameConflict(d, false, "满电架", "sb") == null, "另一个排班可以有自己的“满电架”");
            Check(Scope.NameConflict(d, false, "满电架", null)?.Contains("电池轮转") == true, "新建全局“满电架”和排班的本排班对象重名，提示是哪个排班");
            Check(Scope.NameConflict(d, true, "a_ch", "sa", "a_ch") == null, "改名时不和自己比");

            // 复制排班：本排班定义跟着复制，引用改到副本
            var copy = Scope.CopySchedule(d, a, "电池轮转 副本");
            var cr = copy.Rules.Single();
            var cList = d.Selectors.Single(x => x.Id == cr.Actions[0].List);
            Check(cList.Owner == copy.Id && cList.Name == "a_bat" && cList.Id != "a_bat", "副本有自己的挑选器，名字不变");
            Check(d.Selectors.Single(x => x.Id == cList.From[0]).Owner == copy.Id, "副本挑选器的来源也改成副本的");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, copy, cr).Count == 0, "副本没有结构错误");
            var cK = d.Objects.Single(o => o.Owner == copy.Id && o.Name == "满电标准");
            cK.Value = 50;
            Check(d.Objects.Single(o => o.Id == "a_k").Value == 95, "改副本的常量不影响原排班");

            // 删除排班：本排班定义一起删
            int before = d.Objects.Count + d.Selectors.Count;
            int removed = Scope.DeleteSchedule(d, copy);
            Check(removed == 4 && d.Objects.Count + d.Selectors.Count == before - 4 && d.Objects.Any(o => o.Id == "g_k"), $"删除副本连同 {removed} 个本排班定义，全局定义还在");

            // 提升为全局：连带引用的一起提升；重名时拒绝
            Check(Scope.PromoteSet(d, "a_bat").OrderBy(x => x).SequenceEqual(new[] { "a_bat", "a_ch", "a_k" }), "提升挑选器要连带它引用的本排班挑选器和常量");
            d.Selectors.Single(x => x.Id == "g_ch").Name = "a_ch"; // 全局挑选器和要连带提升的“a_ch”同名
            Check(Scope.Promote(d, "a_bat")?.Contains("重名") == true && d.Selectors.Single(x => x.Id == "a_bat").Owner == "sa", "和全局重名时不提升，也不改动");
            d.Selectors.Single(x => x.Id == "g_ch").Name = "全部充能器";
            Check(Scope.Promote(d, "a_bat") == null && Scope.OwnerOf(d, "a_bat") == null && Scope.OwnerOf(d, "a_k") == null, "提升成功");
            Check(AutoMod.Domain.Rules.Check.FirstBackRef(d, d.Selectors) == null && d.Selectors.TakeWhile(x => x.Owner == null).Count() == 3, "提升后挑选器接在全局组末尾，依赖仍在上方");

            // 存档：Owner 往返；没有 Owner 的是全局；所属排班不在了改为全局
            var back = ModState.FromJson(new ModState { Rules = d }.ToJson()).Rules;
            Check(back.Objects.Single(o => o.Id == "a_full").Owner == "sa", "Owner 存档往返");
            var old = ModState.FromJson("{\"version\":8,\"rules\":{\"objects\":[{\"id\":\"o1\",\"name\":\"满电架\"}],\"selectors\":[{\"id\":\"s1\",\"name\":\"x\",\"from\":[\"o1\"]}],\"schedules\":[]}}").Rules;
            Check(old.Objects.Single().Owner == null && old.Selectors.Single().Owner == null, "没有 Owner 的定义是全局");
            var orphan = new RulesData { Objects = { new ObjectDef { Id = "x", Name = "孤儿", Owner = "gone" } } };
            Scope.Normalize(orphan);
            Check(orphan.Objects.Single().Owner == null, "所属排班不存在的定义改成全局，不丢");
        });

        test("规则·示例：电池轮转（不用设置：交换低电量电池与满电电池，未充满的闲置电池去充电）", () =>
        {
            var w = new SimWorld();
            var bay = w.AddRoot("storage", 1, "机器区");
            var ch = w.Add(bay, "recharger");
            var charged = w.Add(ch, "battery_full");
            var proj = w.AddRoot("machine", 2, "投影仪");
            var low = w.Add(proj, "battery_empty", batterySlot: true);
            var shelf = w.AddRoot("storage", 3, "杂物架");
            var waiting = w.Add(shelf, "battery_empty", b => b.Num["energy"] = 40);
            var stored = w.Add(shelf, "machine");
            var storedBat = w.Add(stored, "battery_empty", batterySlot: true);
            var d = new RulesData();
            var sch = Examples.BatteryRotation(d);
            Check(sch.Rules.Count == 2 && d.Objects.All(o => o.Owner == sch.Id) && d.Selectors.All(x => x.Owner == sch.Id), "两个任务，定义都在本排班里");
            Check(sch.Rules.All(r => AutoMod.Domain.Rules.Check.RuleErrors(d, sch, r, w).Count == 0), "不用设置就没有结构错误");
            var g = new Interpreter(w, d).RunShift(Shift.Night, new[] { App("甲", 60, 2, sch.Id, Shift.Night) }).Groups[0];
            var slot = proj.Children.Single(c => c.InBatterySlot);
            Check(slot == charged, "投影仪换上了充能器里那块满的");
            Check(low.Parent == ch, "换下的电池放回那块满电池原来的地方：充能器");
            Check(waiting.Parent == ch || waiting.Parent == shelf && ch.Free == 0, "杂物架上没充满的电池去充能器（有空位时）");
            Check(storedBat.Parent == stored, "收在架子上的机器（没装好）不换电池");
            Check(g.Rules.All(r => r.Pre != PreResult.Broken), "没有结构错误");
            Check(Text.Members(new[] { "甲#1", "甲#2", "乙", "丙", "丁" }) == "甲 ×2、乙、丙 等（共 5 个）" && Text.Members(new[] { "甲", "乙" }) == "甲、乙", "名字列表多了按名字归并");
            var spareSel = d.Selectors.First(x => x.Name == "满电电池");
            Check(Text.Selector(d, spareSel).Contains("全店") && Text.Selector(d, spareSel).Contains("不在电池槽里"), "定义写成“全店 · 不在电池槽里”：" + Text.Selector(d, spareSel));
            Check(Catalog.PropNotName("dead") == "未死亡" && Catalog.PropNotName("powered") == "没电" && Catalog.PropNotName("inBatterySlot") == "不在电池槽里", "是非属性的否定说法");
            var again = Examples.BatteryRotation(d);
            Check(again.Name != sch.Name, "再建一个：排班名加编号");
        });

        test("规则·执行：AP 耗尽时，留到下次的物品也记一条事件", () =>
        {
            var w = new SimWorld();
            var a1 = w.AddRoot("storage", 1, "甲架");
            var b1 = w.AddRoot("storage", 2, "乙架");
            for (int i = 0; i < 6; i++) w.Add(a1, "battery_full");
            var d = new RulesData();
            Named(w, "oa", 1);
            Named(w, "满电架", 2);
            Sel(d, "s", "#oa", "battery");
            var sch = new Schedule { Id = "sch", Name = "搬电池" };
            sch.Rules.Add(new WorkRule { Id = "r", Name = "搬", Actions = { new ActionDef { Verb = ActVerb.Move, List = "s", To = "#满电架" } } });
            d.Schedules.Add(sch);
            var rr = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 4, 5, "sch") }).Groups[0].Rules[0];
            int deferred = rr.Items.Count(e => e.Outcome == Outcomes.Deferred);
            Check(rr.Deferred > 0 && deferred == rr.Deferred, $"留待 {rr.Deferred} 件，事件 {deferred} 条");
            Check(rr.Items.Count(e => e.Outcome == Outcomes.Done) + deferred == 6, "完成 + 留待 = 全部 6 件");
            // 第二个任务轮不到：写原因，它的物品也记“留到下次”
            var w3 = new SimWorld();
            var a3 = w3.AddRoot("storage", 1, "甲架");
            w3.AddRoot("storage", 2, "乙架");
            for (int i = 0; i < 6; i++) w3.Add(a3, "battery_full");
            Named(w3, "oa", 1); Named(w3, "满电架", 2);
            var d3 = Defs.Clone(d);
            d3.Schedules[0].Rules.Add(new WorkRule { Id = "r2", Name = "再搬", Actions = { new ActionDef { Verb = ActVerb.Move, List = "s", To = "#满电架" } } });
            var g3 = new Interpreter(w3, d3).RunShift(Shift.Morning, new[] { App("甲", 4, 5, "sch") }).Groups[0];
            var r2 = g3.Rules[1];
            Check(r2.Pre == PreResult.NotRun && r2.Why.Contains("AP 不足") && r2.Why.Contains("已用"), $"没执行的任务写原因：{Text.Outcome(r2)}");
            Check(r2.Items.Count > 0 && r2.Items.All(e => e.Outcome == Outcomes.Deferred) && r2.Deferred == r2.Items.Count, "没执行的任务，它的物品记成留到下次");

            // 班次报告：每条 AP 是整数且合计等于已用、未处理按件数、表头写“已用 X / 共 Y AP”
            var w2 = new SimWorld();
            var a2 = w2.AddRoot("storage", 1, "甲架");
            w2.AddRoot("storage", 2, "乙架");
            for (int i = 0; i < 6; i++) w2.Add(a2, "battery_full");
            Named(w2, "oa", 1); Named(w2, "满电架", 2);
            var app = App("甲", 4, 5, "sch");
            var run = new Interpreter(w2, d).RunShift(Shift.Morning, new[] { app });
            var rep = ShiftReport.From(run, false, new[] { app });
            var g = run.Groups[0];
            Check(rep.Entries.Where(e => e.Ok).All(e => e.Cost == Math.Floor(e.Cost)) && rep.Entries.Where(e => e.Ok).Sum(e => e.Cost) == g.ApUsed, "每条 AP 是整数，合计等于已用");
            Check(rep.Deferred == g.Deferred, $"未处理 {rep.Deferred} 件 = 解释器的 {g.Deferred} 件");
            Check(rep.Detail().Contains(Text.Ap(g.ApUsed, g.ApTotal)) && rep.Summary().Contains($"{rep.Deferred} 件因 AP 不足未做"), $"明细写 AP，早报的一句话写 AP 不足没做的件数：{rep.Summary()}");
            Check(rr.Items.Where(e => e.Outcome == Outcomes.Done).All(e => e.Text == "移至 满电架"), "移动结果写设备名（不是东西本来的名字“乙架”）");

            // 同样的事合成一条：6 件一样的电池都搬走 → 一条“×6”，件数、AP 照算
            var w4 = new SimWorld();
            var a4 = w4.AddRoot("storage", 1, "甲架");
            w4.AddRoot("storage", 2, "乙架");
            for (int i = 0; i < 6; i++) w4.Add(a4, "battery_full");
            Named(w4, "oa", 1); Named(w4, "满电架", 2);
            var run4 = new Interpreter(w4, d).RunShift(Shift.Morning, new[] { App("甲", 100, 5, "sch") });
            var rep4 = ShiftReport.From(run4, false, new Apprentice[0]);
            var ok4 = rep4.Entries.Where(e => e.Ok).ToList();
            Check(ok4.Count == 1 && ok4[0].Count == 6 && rep4.Done == 6 && !ok4[0].Item.Contains("#"), $"同样的 6 件合成一条：{string.Join("；", ok4.Select(e => $"{e.Item} ×{e.Count}"))}");
            Check(ShiftReport.Plain("使用 水桶#1490（0ml，水质 0%）") == "使用 水桶" && ShiftReport.Plain("放入 生肉（小）#1680") == "放入 生肉（小）",
                "合并时去掉编号和它后面的状态，名字里的括号留着");
            Check(ok4[0].Cost == run4.Groups[0].ApUsed && rep4.Detail().Contains("×6") && rep4.Summary().Contains("6 件"), $"合成后 AP 合计不变，明细写 ×6：{rep4.Summary()}");
        });

        test("规则·水务模板：收集 → 过滤 → 成品罐 → 贴标签", () =>
        {
            var w = FakeGame.Install(new SimWorld());
            var farm = w.AddRoot("moisture_farm", 1, "收集器");
            w.Add(farm, "jug_dirty", j => j.Num["volumeMl"] = 2500);
            var pitcher = w.AddRoot("water_pitcher", 2, "过滤水罐");
            w.Add(pitcher, "filter", f => f.Num["durability"] = 20);
            var product = w.AddRoot("jug_empty", 3, "成品罐");
            var spares = w.AddRoot("storage", 4, "备件箱");
            w.Add(spares, "filter");
            var tools = w.AddRoot("toolbox", 5, "工具箱");
            var d = new RulesData();
            var sch = Scenarios.Instantiate(d, Scenarios.Get("water"), new() { ["farms"] = new() { 1 }, ["pitcher"] = new() { 2 }, ["product"] = new() { 3 }, ["spares"] = new() { 4 }, ["tools"] = new() { 5 } }, w: w);
            var a = App("甲", 40, 2, sch.Id); // 早班、晚班都执行这个流程
            a.Duties.Add(new Duty { ScheduleId = sch.Id, Shift = Shift.Night });
            var interp = new Interpreter(w, d);
            interp.RunShift(Shift.Morning, new[] { a });
            a.ApUsed = 0; // 入睡发薪：新的一天从晚班开始
            var g1 = interp.RunShift(Shift.Night, new[] { a }).Groups[0];
            var label1 = g1.Rules.First(r => r.Name.Contains("标签"));
            Check(product.N("labelOk") == 0 && label1.Issues.Any(i => i.Code == FailCode.NoTool), "工具箱里没有标签打印机：贴标签报缺少工具");
            w.Add(tools, "labeler");
            var g2 = interp.RunShift(Shift.Morning, new[] { a }).Groups[0];
            Check(pitcher.Children.Single().N("durability") > 300, "过滤水罐换上了新滤芯");
            Check(product.N("volumeMl") > 0 && product.N("purity") >= 90, $"成品罐有水 {product.N("volumeMl"):0}ml，水质 {product.N("purity"):0}%");
            Check(product.N("labelOk") == 0 && g2.Rules.First(r => r.Name.Contains("标签")).Issues.Any(i => i.Code == FailCode.NoEffect),
                "有了标签打印机：游戏的标签打印机不改正标签，记为没有效果");
        });

        test("规则·设备名直接当列表：前提带条件、工具来自它本身或它里面", () =>
        {
            // 集水器里的桶叫 A，大桶叫 B：不建筛选器，“A 含水 > 1000 时用 A 倒进 B”
            (SimWorld w, SimItem farm, SimItem jug, SimItem big) Shop()
            {
                var w = FakeGame.Install(new SimWorld());
                var farm = w.AddRoot("moisture_farm", 1, "集水器");
                var jug = w.Add(farm, "jug_dirty", j => j.Num["volumeMl"] = 2500);
                var big = w.AddRoot("jug_empty", 3, "大桶");
                return (w, farm, jug, big);
            }
            WorkRule Pour(string from) => new()
            {
                Id = "r", Name = "倒水",
                Pre = { new PreCond { Ref = "#A", Test = PreTest.NotEmpty, Where = Cond.Cmp("liq:water", ">", Expr.Of(1000)) } },
                Actions = { new ActionDef { Verb = ActVerb.Use, List = "#B", Tool = from } },
            };
            {
                var (w, _, jug, big) = Shop();
                Named(w, "A", jug.Id); Named(w, "B", 3);
                var d = new RulesData();
                var sch = new Schedule { Id = "s", Name = "s", Rules = { Pour("#A") } };
                d.Schedules.Add(sch);
                var errs = AutoMod.Domain.Rules.Check.RuleErrors(d, sch, sch.Rules[0], w);
                Check(errs.Count == 0 && d.Selectors.Count == 0, $"不用筛选器，没有结构错误：{string.Join("；", errs)}");
                string pre = AutoMod.Domain.Rules.Text.Pre(d, sch, sch.Rules[0].Pre[0]);
                Check(pre.StartsWith("有 ") && pre.Contains("> 1000") && pre.EndsWith("的 A"), $"前提的说法：{pre}");
                var interp = new Interpreter(w, d);
                var g = interp.RunShift(Shift.Morning, new[] { App("甲", 100, 3, sch.Id) }).Groups[0];
                Check(big.N("volumeMl") == 2500 && jug.N("volumeMl") == 0 && g.Rules[0].Pre == PreResult.Pass, $"桶 A 本身当工具，倒进 B（{big.N("volumeMl")}ml）");
                jug.Num["volumeMl"] = 800;
                g = interp.RunShift(Shift.Night, new[] { App("乙", 100, 3, sch.Id, Shift.Night) }).Groups[0];
                Check(g.Rules[0].Pre == PreResult.Fail && big.N("volumeMl") == 2500, "A 只有 800ml：前提不满足，不倒");

                // 展开成筛选器：前提和动作对象换成新筛选器，工具来源不变
                var sel = Defs.ExpandName(d, sch, "A");
                Check(d.Selectors.Count == 1 && sch.Rules[0].Pre[0].Ref == sel.Id && sch.Rules[0].Actions[0].Tool == "#A" && sel.Owner == "s",
                    "展开成筛选器：前提改用它，工具来源还是【A】");
                Check(Eval.List(new EvalCtx(w, d), sel.Id).Single() == (object)jug, "展开出来的筛选器选的是 A 本身");

            }
            {
                // 起名的是集水器：工具来自【A】= 它本身和它里面的，按工具类型挑出里面的桶
                var (w, farm, jug, big) = Shop();
                Named(w, "A", farm.Id); Named(w, "B", 3);
                var d = new RulesData();
                var r = Pour("#A");
                r.Pre.Clear();
                var sch = new Schedule { Id = "s", Name = "s", Rules = { r } };
                d.Schedules.Add(sch);
                new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 100, 3, sch.Id) });
                Check(big.N("volumeMl") == 2500, $"集水器叫 A：用它里面的桶倒进 B（{big.N("volumeMl")}ml）");
            }
        });

        test("反馈：没做事写原因、执行条件写明哪条不成立、提醒、数量较多、预演只算一次、发工资按学徒顺序", () =>
        {
            var w = FakeGame.Install(new SimWorld());
            var box = w.AddRoot("storage", 1, "仓库");
            for (int i = 0; i < 3; i++) w.Add(box, "jug_dirty");
            var dst = w.AddRoot("storage", 2, "目标");
            Named(w, "box", 1); Named(w, "dst", 2);
            var d = new RulesData();
            Sel(d, "low", "#box", "water_jug", x => x.Where = new CondGroup { Items = { Cond.Cmp("volumeMl", "<", Expr.Of(100)) } });
            var sch = new Schedule { Id = "s", Name = "s", Rules =
            {
                new WorkRule { Id = "r1", Name = "搬空桶", Actions = { new ActionDef { Verb = ActVerb.Move, List = "low", To = "#dst" } } },
                new WorkRule { Id = "r2", Name = "有空桶才做", Pre = { new PreCond { Ref = "low", Test = PreTest.NotEmpty } }, Actions = { new ActionDef { Verb = ActVerb.Move, List = "low", To = "#dst" } } },
            } };
            d.Schedules.Add(sch);
            var g = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 100, 3, sch.Id) }).Groups[0];
            Check(g.Rules[0].Idle == "「low」当前为空（来源中有 3 件，都不满足条件）", $"没做事写原因：{g.Rules[0].Idle}");
            Check(g.Rules[1].Why != null && g.Rules[1].Why.Contains("为空") && !g.Rules[1].Why.Contains("不成立") && g.Rules[1].Why.Contains("low"), $"执行条件写明实际情况：{g.Rules[1].Why}");
            var rep = ShiftReport.From(new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 100, 3, sch.Id) }), false, new Apprentice[0]);
            Check(rep.Groups[0].Idle.Count == 2, "报告里列出没做事的任务和原因");

            // 提醒：全店的全部物品；直接用容器的设备名当放入对象；工具是容器
            Sel(d, "all", Places.Shop, Catalog.Any);
            var bad = new WorkRule { Actions = { new ActionDef { Verb = ActVerb.Move, List = "all", To = "#dst" }, new ActionDef { Verb = ActVerb.Move, List = "#box", To = "#dst" },
                new ActionDef { Verb = ActVerb.Use, List = "low", Tool = "#box" } } };
            var warns = AutoMod.Domain.Rules.Check.RuleWarnings(d, bad, w);
            Check(warns.Count == 3 && warns[0].Contains("全店的全部物品") && warns[1].Contains("容器本身") && warns[2].Contains("工具"), "三种提醒：" + string.Join("；", warns));

            // 一次处理很多件：报“数量较多”，不算没做成
            for (int i = 0; i < 40; i++) w.Add(box, "jug_empty");
            var many = new Interpreter(w, d).RunShift(Shift.Morning, new[] { App("甲", 1000, 5, sch.Id) }).Groups[0].Rules[0];
            Check(many.Issues.Any(i => i.Code == FailCode.Many) && !AutoMod.Domain.Rules.Text.RunSummary(new RunReport { Groups = { new GroupRunReport { Rules = { many } } } }).Contains("未完成"),
                "一次处理 40 件：提示数量较多，不算未完成");

            // 预演：效果未知的使用，同一件只算第一次
            var sim = new SimWorld();
            var cage = sim.AddRoot("animal_cage", 1, "笼");
            var rat = sim.Add(cage, "rat_m");
            var knife = sim.AddRoot("knife", 2, "刀");
            Check(sim.Apply(knife, rat, null).Ok && sim.Apply(knife, rat, null).Code == FailCode.Simulated, "预演：同一只鼠第二次宰杀报“预演中已处理过”");

            // 发工资：按排班顺序，没指派的排最后
            var a1 = App("甲", 20, 1, "B"); var a2 = App("乙", 20, 1, null); var a3 = App("丙", 20, 1, "A");
            Check(Payday.Order(new[] { a1, a2, a3 }, new List<string> { "A", "B" }).Select(x => x.Name).SequenceEqual(new[] { "甲", "丙", "乙" }), "发工资按学徒列表顺序，未指派的最后");
        });

        test("选排班的顺序：新建的在前；执行顺序不变", () =>
        {
            var d = new RulesData { Schedules = { new Schedule { Id = "old1", Name = "旧1" }, new Schedule { Id = "old2", Name = "旧2" } } };
            var a = Examples.BatteryRotation(d);
            System.Threading.Thread.Sleep(2);
            var b = ProfessionExamples.Build(d, "hydro");
            Check(Defs.PickOrder(d).Select(x => x.Id).SequenceEqual(new[] { b.Id, a.Id, "old1", "old2" }), "下拉框：最新的在最前，旧数据按列表顺序排在后面");
            Check(d.Schedules.Last() == b, "排班列表（执行顺序）里新建的仍加在最后");
        });

        RunSubFilters(test);
    }

    // ================= 子筛选器 =================

    /// <summary>一个笼子，五只鼠，体重 100～500，uid 依次递增。</summary>
    private static (SimWorld w, RulesData d, SelectorDef heavy) SubScene()
    {
        var w = new SimWorld();
        var cage = w.AddRoot("animal_cage", 1, "笼");
        foreach (var kg in new[] { 300, 100, 500, 200, 400 }) w.Add(cage, kg % 200 == 0 ? "rat_m" : "rat_f", r => r.Num["weight"] = kg);
        Named(w, "笼", 1);
        var d = new RulesData();
        var heavy = Sel(d, "heavy", "#笼", "rat", s => { s.Sort = new SortSpec { Prop = "weight", Desc = true }; s.Take = new TakeSpec { Mode = TakeMode.First, N = Expr.Of(3) }; });
        return (w, d, heavy);
    }

    private static List<double> Weights(SimWorld w, RulesData d, string id) =>
        Eval.List(new EvalCtx(w, d), id).Select(x => w.Num(x, "weight") ?? -1).ToList();

    private static void RunSubFilters(Action<string, Action> test)
    {
        test("子筛选器·求值：先取前 N 再筛，沿用上级顺序，可多层", () =>
        {
            var (w, d, _) = SubScene();
            Sel(d, "sub", "heavy", Catalog.Any, s => s.Where.Items.Add(Cond.Cmp("weight", "<", Expr.Of(500))));
            Sel(d, "sub2", "sub", Catalog.Any, s => s.Where.Items.Add(Cond.Cmp("weight", "<", Expr.Of(400))));
            Check(Weights(w, d, "heavy").SequenceEqual(new double[] { 500, 400, 300 }), "上级：体重最高的 3 只");
            Check(Weights(w, d, "sub").SequenceEqual(new double[] { 400, 300 }), "子筛选器在上级结果里筛选，顺序沿用上级");
            Check(Weights(w, d, "sub2").SequenceEqual(new double[] { 300 }), "第三层继续筛选");
        });

        test("子筛选器·求值：上级是容器取内容物，上级是数值时为空", () =>
        {
            var (w, d, _) = SubScene();
            Sel(d, "cages", "#笼", Catalog.Self);
            Sel(d, "inside", "cages", "rat");
            Sel(d, "self", "cages", Catalog.Self);
            Sel(d, "n", "#笼", "rat", s => { s.Mode = SelectorMode.Number; s.Agg = new AggSpec { Fn = AggFn.Count }; });
            Sel(d, "fromN", "n", Catalog.Any);
            var c = new EvalCtx(w, d);
            Check(Eval.List(c, "inside").Count == 5, "容器：默认取里面的物品");
            Check(Eval.List(c, "self").Count == 1, "容器：选“本身”继续筛选容器");
            Check(Eval.List(c, "fromN").Count == 0, "数值不能作为范围");
            Check(SelectorFamily.IsItemList(d, "heavy", w) && !SelectorFamily.IsItemList(d, "cages", w) && !SelectorFamily.IsItemList(d, "n", w) && !SelectorFamily.IsItemList(d, Places.Shop, w) && !SelectorFamily.IsItemList(d, "#笼", w), "只有结果是物品的筛选器算物品列表");
        });

        test("子筛选器·层级：上级从根到近、菱形去重、循环不死循环、跨流程不可见", () =>
        {
            var d = new RulesData { Schedules = { new Schedule { Id = "x" }, new Schedule { Id = "y" } } };
            var a = Sel(d, "a", Places.Shop, Catalog.Any);
            var b = Sel(d, "b", "a", Catalog.Any);
            var c = Sel(d, "c", "a", Catalog.Any);
            var e = Sel(d, "e", "b", Catalog.Any, s => s.From.Add("c"));
            Check(SelectorFamily.Ancestors(d, e).Select(x => x.Id).SequenceEqual(new[] { "a", "b", "c" }), "菱形：a 只出现一次，排在最前");
            Sel(d, "p", "q", Catalog.Any);
            var q = Sel(d, "q", "p", Catalog.Any);
            Check(SelectorFamily.Ancestors(d, q).Select(x => x.Id).SequenceEqual(new[] { "p" }), "循环引用不死循环");
            Sel(d, "mine", "a", Catalog.Any, s => s.Owner = "x");
            Sel(d, "other", "a", Catalog.Any, s => s.Owner = "y");
            Check(SelectorFamily.Children(d, a, "x").Select(s => s.Id).SequenceEqual(new[] { "b", "c", "mine" }), "只列全局和当前流程的子筛选器");
            Check(SelectorFamily.Children(d, a, null).Select(s => s.Id).SequenceEqual(new[] { "b", "c" }), "全局视角只列全局的");
        });

        test("子筛选器·草稿：打开时的顺序，展开不移除已显示的项", () =>
        {
            var (w, d, heavy) = SubScene();
            var sub = Sel(d, "sub", "heavy", Catalog.Any);
            Sel(d, "other", "heavy", Catalog.Any);
            Sel(d, "leaf", "sub", Catalog.Any);
            var r = SelectorDrafts.Edit(d, w, sub, null);
            Check(r.Entries.Select(e => e.Def.Id).SequenceEqual(new[] { "heavy", "sub", "leaf" }) && r.Active.Def.Id == "sub", "上级在上、当前展开、直接子筛选器在下");
            r.Expand(r.Entries[0]);
            Check(r.Entries.Select(e => e.Def.Id).SequenceEqual(new[] { "heavy", "other", "sub", "leaf" }) && r.Active.Def.Id == "heavy", "展开上级：加上它的其他子筛选器，已显示的保留");
            r.Collapse();
            Check(r.Active == null, "允许全部收起");
            Check(d.Selectors.Contains(heavy) && !r.AnyDirty, "打开不算改动，原数据不变");
        });

        test("子筛选器·草稿：未保存的上级可被下级求值，保存一次写入，取消不写", () =>
        {
            var (w, d, heavy) = SubScene();
            var r = SelectorDrafts.Edit(d, w, heavy, null);
            r.Active.Def.Take.N = Expr.Of(2);
            var child = r.AddChild(r.Active, null);
            child.Def.Name = "子";
            child.Def.Where.Items.Add(Cond.Cmp("weight", "<", Expr.Of(500)));
            var view = r.Overlay();
            Check(Weights(w, view, child.Def.Id).SequenceEqual(new double[] { 400 }), "上级未保存的改动已反映到子筛选器");
            Check(r.Active == child && d.Selectors.Count == 1 && heavy.Take.N.Terms[0].Num == 3, "保存前规则数据不变");
            Check(r.Commit() == null && d.Selectors.Count == 2 && d.Selectors[0].Take.N.Terms[0].Num == 2 && d.Selectors[1].From[0] == "heavy", "保存后全部写入");
        });

        test("子筛选器·草稿：未改动项不写入，校验失败什么都不写", () =>
        {
            var (w, d, heavy) = SubScene();
            var sub = Sel(d, "sub", "heavy", Catalog.Any);
            var r = SelectorDrafts.Edit(d, w, sub, null);
            r.Active.Def.Where.Items.Add(Cond.Cmp("weight", "<", Expr.Of(9)));
            Check(r.Commit() == null && ReferenceEquals(d.Selectors[0], heavy) && !ReferenceEquals(d.Selectors[1], sub), "只替换改动的项");

            var r2 = SelectorDrafts.Edit(d, w, d.Selectors[1], null);
            r2.Entries[0].Def.Name = "  ";
            r2.Entries[1].Def.Where.Items.Clear();
            string why = r2.Commit();
            Check(why != null && r2.Active == r2.Entries[0], "校验失败：返回原因并展开出错项");
            Check(d.Selectors[0].Name == "heavy" && d.Selectors[1].Where.Items.Count == 1, "校验失败时什么都没写");

            var r3 = SelectorDrafts.Edit(d, w, d.Selectors[1], null);
            r3.Entries[1].Def.Name = "heavy";
            Check(r3.Commit() != null, "草稿之间也不能重名");
            var r4 = SelectorDrafts.New(d, w, new SelectorDef { Id = "n1", Name = "新", From = { Places.Shop }, Kind = Catalog.Any }, null);
            r4.AddChild(r4.Active, null).Def.Name = "新";
            Check(r4.Commit() != null && d.Selectors.Count == 2, "两份新草稿重名：拒绝且不写入");
        });

        test("子筛选器·草稿：删除与复制", () =>
        {
            var (w, d, heavy) = SubScene();
            var sub = Sel(d, "sub", "heavy", Catalog.Any);
            var r = SelectorDrafts.Edit(d, w, heavy, null);
            Check(r.Remove(r.Entries[0]) != null && r.Entries.Count == 2, "仍被子筛选器引用：拒绝删除");
            Check(r.Remove(r.Entries[1]) == null && r.Entries.Count == 1 && d.Selectors.Contains(sub), "已保存的项：保存前规则数据里还在");
            Check(r.AnyDirty, "标记删除算改动");
            Check(r.Remove(r.Entries[0]) == null, "子筛选器移除后可删上级");
            Check(d.Selectors.Count == 2, "不保存就不删除，可放弃");
            Check(r.Commit() == null && d.Selectors.Count == 0, "保存时才删除");

            var (w2, d2, heavy2) = SubScene();
            var r2 = SelectorDrafts.Edit(d2, w2, heavy2, null);
            var kid = r2.AddChild(r2.Active, null);
            kid.Def.Name = "子";
            Check(r2.Remove(r2.Entries[0]) != null, "被未保存的草稿引用：也拒绝");
            Check(r2.Remove(kid) == null && r2.Active == null && r2.Entries.Count == 1, "未保存的草稿直接移除");
            var copy = r2.Copy(r2.Entries[0], null);
            Check(r2.Entries[1] == copy && r2.Active == copy && copy.Def.Id != heavy2.Id && copy.Def.Name != heavy2.Name, "复制：新草稿在下方展开，新 Id，不重名");
        });

        test("子筛选器·草稿：所属流程、数值上级、任务引用", () =>
        {
            var w = new SimWorld();
            var d = new RulesData { Schedules = { new Schedule { Id = "x" } } };
            var g = Sel(d, "g", Places.Shop, Catalog.Any);
            var local = Sel(d, "l", Places.Shop, Catalog.Any, s => s.Owner = "x");
            var n = Sel(d, "n", Places.Shop, Catalog.Any, s => s.Mode = SelectorMode.Number);
            var r = SelectorDrafts.Edit(d, w, g, "x");
            Check(r.AddChild(r.Active, "x").Def.Owner == "x", "全局上级：子筛选器属于当前流程");
            Check(SelectorDrafts.Edit(d, w, local, "x") is var rl && rl.AddChild(rl.Active, "y").Def.Owner == "x", "本流程上级：子筛选器同属该流程");
            Check(SelectorDrafts.Edit(d, w, n, "x") is var rn && rn.AddChild(rn.Active, "x") == null, "数值上级不能建子筛选器");

            var root = new SelectorDef { Id = "root", Name = "根", From = { Places.Shop }, Kind = Catalog.Any };
            var t = SelectorDrafts.New(d, w, root, "x");
            Check(t.TaskTarget == root, "没有新建子筛选器：引用最初的筛选器");
            var a = t.AddChild(t.Active, "x");
            var b = t.AddChild(a, "x");
            b.Def.Mode = SelectorMode.Number;
            Check(t.TaskTarget == a.Def, "引用最后新建的物品筛选器，跳过数值的");
            Check(t.Remove(b) == null && t.Remove(a) == null && t.TaskTarget == root, "移除后回到最初的筛选器");
        });

        test("子筛选器·草稿：物品列表上级的残留范围方式在打开时清除，不算改动", () =>
        {
            var (w, d, _) = SubScene();
            var sub = Sel(d, "sub", "heavy", Catalog.Self, s => s.Slot = "battery");
            var r = SelectorDrafts.Edit(d, w, sub, null);
            Check(r.Active.Def.Slot == null && r.Active.Def.Kind == Catalog.Any, "打开时清除");
            Check(!r.AnyDirty && Weights(w, r.Overlay(), "sub").SequenceEqual(Weights(w, d, "sub")), "结果不变，也不算改动");
            Check(r.Commit() == null && ReferenceEquals(d.Selectors[1], sub), "未改动就不写回");
        });

        test("子筛选器·当前结果：件数、截断、数值、空结果原因、设备名", () =>
        {
            var (w, d, _) = SubScene();
            Sel(d, "none", "heavy", Catalog.Any, s => s.Where.Items.Add(Cond.Cmp("weight", ">", Expr.Of(9999))));
            Sel(d, "n", "#笼", "rat", s => { s.Mode = SelectorMode.Number; s.Agg = new AggSpec { Fn = AggFn.Count }; });
            var c = new EvalCtx(w, d);
            var p = SelectorPreview.Of(c, "heavy", 2);
            Check(p.Count == 3 && p.Items.Count == 2 && w.Num(p.Items[0], "weight") == 500, "件数是全部结果，列表按输出顺序截断");
            var e = SelectorPreview.Of(c, "none", 8);
            Check(e.Count == 0 && !string.IsNullOrEmpty(e.EmptyReason), "结果为空时给出原因");
            var n = SelectorPreview.Of(c, "n", 8);
            Check(n.IsNumber && n.Number == 5, "统计数值");
            Check(SelectorPreview.Of(c, "#笼", 8).Count == 1, "设备名：叫这个名字的东西本身");
            Check(SelectorPreview.Of(c, "nothing", 8) == null && SelectorPreview.Of(c, null, 8) == null, "不是筛选器时为空");
            Check(SelectorPreview.Of(c, "#不存在", 8) is { Count: 0 } m && !string.IsNullOrEmpty(m.EmptyReason), "店内没有这个名字时给出原因");
        });

        test("子筛选器·兼容：带上下级的流程包导出再导入，引用正确", () =>
        {
            var d = new RulesData();
            var sch = new Schedule { Id = "x", Name = "流程" };
            d.Schedules.Add(sch);
            var a = Sel(d, "a", Places.Shop, "rat", s => { s.Owner = "x"; s.Name = "上级"; });
            var b = Sel(d, "b", "a", Catalog.Any, s => { s.Owner = "x"; s.Name = "下级"; });
            sch.Rules.Add(new WorkRule { Id = "r", Name = "任务", Actions = { new ActionDef { Verb = ActVerb.Mark, List = b.Id, Text = "m" } } });
            string text = Transfer.Export(d, sch, "test");
            var fresh = new RulesData();
            var s2 = Transfer.Import(fresh, text);
            var nb = fresh.Selectors.First(x => x.Name == "下级");
            var na = fresh.Selectors.First(x => x.Name == "上级");
            Check(nb.From.Single() == na.Id && s2.Rules[0].Actions[0].List == nb.Id, "导入后下级仍引用上级，任务引用下级");
        });
    }

    private static (SelectorDef a, SelectorDef b)? Check0(RulesData d, List<SelectorDef> order) => AutoMod.Domain.Rules.Check.FirstBackRef(d, order);
}
