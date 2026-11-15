using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;
using AutoMod.Domain.Workforce;
// 动作循环（单次 / 多次 / 无限）的单元测试。
internal static class LoopTests
{
    private static Action<bool, string> _check;
    private static void Check(bool ok, string what) => _check(ok, what);

    /// <summary>甲区 3 块电池、空的放置区；“一块”= 甲区电池的第 1 块；计数器“次数”从 0 开始。</summary>
    private sealed class Shop
    {
        public SimWorld W = FakeGame.Install(new SimWorld(7));
        public SimItem A, Dst;
        public RulesData D = new();
        public Schedule S = new() { Id = "main", Name = "总流程", Notepad = { new NoteDef { Id = "k", Name = "次数" } } };

        public Shop()
        {
            A = W.AddRoot("storage", 1, "甲区");
            Dst = W.AddRoot("storage", 2, "放置区");
            for (int i = 0; i < 3; i++) W.Add(A, "battery_full");
            A.DeviceName = "甲区"; Dst.DeviceName = "放置区";
            D.Schedules.Add(S);
            D.Selectors.Add(new SelectorDef { Id = "aBats", Name = "甲区电池", Owner = "main", From = { "#甲区" }, Kind = "battery" });
            D.Selectors.Add(new SelectorDef { Id = "one", Name = "一块", Owner = "main", From = { "#甲区" }, Kind = "battery", Take = new TakeSpec { N = Expr.Of(1) } });
        }

        public GroupRunReport Run(params WorkRule[] rules) => Run(80, rules);

        public GroupRunReport Run(int ap, params WorkRule[] rules)
        {
            S.Rules.AddRange(rules);
            var a = new Apprentice { Name = "甲", MaxAp = ap, Level = 2, Duties = { new Duty { ScheduleId = S.Id, Shift = Shift.Morning } } };
            return new Interpreter(W, D).RunShift(Shift.Morning, new[] { a }).Groups[0];
        }

        public int Moved => Dst.Children.Count(c => SimWorld.InGroup(c, "battery"));
    }

    private static ActionDef MoveOne(string loop = null, int? n = null) =>
        new() { Verb = ActVerb.Move, List = "one", To = "#放置区", Loop = loop, LoopTimes = n };

    private static WorkRule Rule(ActionDef act, params PreCond[] pre)
    {
        var r = new WorkRule { Id = "r", Name = "搬", Actions = { act } };
        r.Pre.AddRange(pre);
        return r;
    }

    private static PreCond MoreThan(int n) => new() { Ref = "aBats", Test = PreTest.CountGt, Right = Expr.Of(n) };

    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        _check = check;

        test("循环：单次只做一轮，多次每轮重新筛选", () =>
        {
            var once = new Shop();
            once.Run(Rule(MoveOne()));
            Check(once.Moved == 1, $"单次：搬 1 块（{once.Moved}）");

            var twice = new Shop();
            var g = twice.Run(Rule(MoveOne(LoopMode.Times, 2)));
            Check(twice.Moved == 2, $"多次 2：每轮重新取“第 1 块”，共搬 2 块（{twice.Moved}）");
            Check(g.Rules[0].Done.GetValueOrDefault(ActVerb.Move) == 2, "报告里件数累加");
            Check(g.Rules[0].Details.Contains("循环 2 轮"), string.Join(" | ", g.Rules[0].Details));
            Check(g.Rules[0].Details.Count(x => x.StartsWith("（启动")) == 1, "启动开销只算一次");
        });

        test("循环：多次不检查执行条件", () =>
        {
            var shop = new Shop();
            shop.Run(Rule(MoveOne(LoopMode.Times, 3), MoreThan(2)));
            Check(shop.Moved == 3, $"执行条件“甲区电池多于 2 件”第二轮就不成立，多次照样做满 3 轮（{shop.Moved}）");
        });

        test("循环：无限循环每轮前检查执行条件", () =>
        {
            var shop = new Shop();
            var g = shop.Run(Rule(MoveOne(LoopMode.Forever), MoreThan(1)));
            Check(shop.Moved == 2, $"甲区剩 1 块时执行条件不成立，停（搬了 {shop.Moved}）");
            Check(g.Rules[0].Details.Contains("循环 2 轮后执行条件不再成立"), string.Join(" | ", g.Rules[0].Details));
        });

        test("循环：一轮里没有任何动作就停", () =>
        {
            var shop = new Shop();
            var g = shop.Run(Rule(MoveOne(LoopMode.Forever)));
            Check(shop.Moved == 3, $"每班执行：搬完 3 块（{shop.Moved}）");
            Check(g.Rules[0].Details.Contains("循环 4 轮，最后一轮没有动作") && g.Rules[0].Idle == null,
                "第 4 轮什么都没做，停；不写“没做的原因”：" + string.Join(" | ", g.Rules[0].Details));

            var many = new Shop();
            many.Run(Rule(MoveOne(LoopMode.Times, 50)));
            Check(many.Moved == 3, "多次也一样：没东西可搬就提前停");
        });

        test("循环：不花 AP 的无限循环满 100 轮停止", () =>
        {
            var shop = new Shop();
            var note = new ActionDef { Verb = ActVerb.Note, Note = "k", Value = Expr.Of(1), Loop = LoopMode.Forever };
            var g = shop.Run(Rule(note, new PreCond { RefKind = PreRef.Note, Ref = "k", Test = "<", Right = Expr.Of(1000) }));
            Check(g.Memo["k"] == LoopMode.Cap, $"记录也算动作：做满 {LoopMode.Cap} 轮（{g.Memo["k"]}）");
            Check(g.Rules[0].Issues.Any(i => i.Text.Contains("100")), string.Join(" | ", g.Rules[0].Issues.Select(i => i.Text)));

            var cond = new Shop();
            var g2 = cond.Run(Rule(new ActionDef { Verb = ActVerb.Note, Note = "k", Value = Expr.Of(1), Loop = LoopMode.Forever },
                new PreCond { RefKind = PreRef.Note, Ref = "k", Test = "<", Right = Expr.Of(5) }));
            Check(g2.Memo["k"] == 5 && g2.Rules[0].Issues.Count == 0, $"计数器到 5，执行条件不成立，停（{g2.Memo["k"]}）");
        });

        test("循环：AP 用完时停，后面的任务不再执行", () =>
        {
            var shop = new Shop();
            var g = shop.Run(3, Rule(MoveOne(LoopMode.Forever)), new WorkRule { Id = "后面", Name = "后面", Actions = { MoveOne() } });
            Check(shop.Moved is > 0 and < 3, $"搬了一部分（{shop.Moved}）");
            Check(g.Rules[0].Details.Any(x => x.Contains("AP 用完")) && g.Rules[1].Pre == PreResult.NotRun, string.Join(" | ", g.Rules[0].Details));
        });

        test("循环：执行流程循环，子流程的任务报告写轮次", () =>
        {
            var shop = new Shop();
            var sub = new Schedule { Id = "sub", Name = "搬一块" };
            sub.Rules.Add(new WorkRule { Id = "r_sub", Name = "搬", Actions = { MoveOne() } });
            shop.D.Schedules.Add(sub);
            shop.D.Selectors.First(x => x.Id == "one").Owner = null;
            shop.D.Selectors.First(x => x.Id == "aBats").Owner = null;
            var g = shop.Run(Rule(new ActionDef { Verb = ActVerb.Call, Flow = "sub", Loop = LoopMode.Forever }, MoreThan(0)));
            Check(shop.Moved == 3, $"无限循环执行子流程，搬完为止（{shop.Moved}）");
            var names = g.Rules.Select(r => r.Name).ToList();
            Check(names.SequenceEqual(new[] { "搬", "搬一块 › 搬（第 1 轮）", "搬一块 › 搬（第 2 轮）", "搬一块 › 搬（第 3 轮）" }), string.Join(" | ", names));
            Check(Math.Abs(g.Rules.Sum(r => r.Ap) - g.ApUsed) < 1e-9, $"各条 AP 加起来等于这一组用的 AP（{g.Rules.Sum(r => r.Ap)} / {g.ApUsed}）");
        });

        test("循环：句子、卡片、检查、换动作、存档", () =>
        {
            var shop = new Shop();
            var d = shop.D;
            var times = MoveOne(LoopMode.Times, 3);
            var forever = MoveOne(LoopMode.Forever);
            Check(Text.Action(d, shop.S, times).EndsWith("，循环 3 次") && Text.Action(d, shop.S, forever).EndsWith("，无限循环"), Text.Action(d, shop.S, times));
            string card = string.Concat(Card.Sentence(d, shop.S, Rule(times)).Last().Select(x => x.Text));
            Check(card.EndsWith("（循环 3 次）"), card);
            Check(!Text.Action(d, shop.S, MoveOne()).Contains("循环"), "单次不写");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, shop.S, Rule(times)).Count == 0, "正常的循环没有错误");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, shop.S, Rule(MoveOne(LoopMode.Times, 0))).Count > 0
                  && AutoMod.Domain.Rules.Check.RuleErrors(d, shop.S, Rule(MoveOne(LoopMode.Times, LoopMode.Cap + 1))).Count > 0, "次数不在 1–100 报错");
            var switched = Card.SwitchOp(d, times, ActVerb.Swap);
            Check(switched.Loop == LoopMode.Times && switched.LoopTimes == 3, "换动作时循环保留");
            Check(!JsonCodec.Serialize(MoveOne()).Contains("loop"), "单次不写进存档");
            var back = JsonCodec.Clone(times);
            Check(back.Loop == LoopMode.Times && back.LoopTimes == 3, "存档读回");
        });
    }
}

// 示例“自动增压基因改造”：写入 → 热度到上限送去繁殖 → 子代回写入机；有超科技涡轮增压器时最后两个任务无限循环增压（模拟器照游戏规则模拟增压器）
internal static class BoostExampleTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("示例·自动增压基因改造：一个班写完一次，下个班繁殖，再下个班子代回写入机；没有增压器时只走不靠增压的任务", () =>
        {
            var w = FakeGame.Install(new SimWorld(3));
            w.Genetics.Traits.Add(new SimTrait { Id = "purifying", Complexity = 3, MaxRank = int.MaxValue });
            w.Genetics.Innate["hydroreed_seed"] = new() { ["purifying"] = 4 };
            SimItem Machine(string kind, string name, double speed, params string[] slots)
            {
                var m = w.AddRoot("other"); m.Kind = kind; m.Capacity = 20; m.HasBatterySlot = true; m.Num["speed"] = speed; m.Num["dnaQuality"] = 100; m.Num["has:STANDARD_MACHINE_TAG"] = 1; m.DeviceName = name;
                w.Add(m, "other", x => x.Kind = "current_converter", batterySlot: true, force: true);
                foreach (var role in slots) m.SlotList.Add(("slot:" + role, role));
                return m;
            }
            var writer = Machine("plant_dna_manipulator", "写入机", 10, "seed", "input_disk", "output_disk", "eject");
            var hydro = Machine("hydroponic", "育种水培机", 8, "seed", "water", "output");
            var seed = w.Add(writer, "other", y =>
            {
                y.Kind = "hydroreed_seed"; y.Str["plantId"] = "hydroreed_seed"; y.Num["tag:PLANT_TRAIT_purifying"] = 4; y.Num["tag:GENETIC_HEAT_INT"] = 0;
                y.Num["growRequired"] = 8; y.Num["seedReturn"] = 1; y.Num["harvestQty"] = 1; y.Str["harvestKind"] = "hydroreed"; y.TraitsOverride = new() { "seed" };
            }, force: true);
            seed.InSlot = "slot:seed";
            var bay = w.AddRoot("storage", 50, "基因库"); bay.DeviceName = "基因库";
            var disk = w.Add(bay, "other", y => { y.Kind = "plant_dna_disk"; y.Str[SimWorld.DiskTraitTag] = "purifying"; y.Num[SimWorld.DiskUsesTag] = 2; }, force: true);
            w.Add(bay, "other", y => { y.Kind = "plant_dna_disk"; y.Str[SimWorld.DiskTraitTag] = "purifying"; y.Num[SimWorld.DiskUsesTag] = 2; }, force: true);
            var booster = w.AddRoot("other"); booster.Kind = "turbo_booster_hypertech"; booster.Num[SimWorld.BoostChargeTag] = 15000;

            var d = new RulesData();
            var s = ProfessionExamples.Build(d, "dna_boost");
            check(s.Rules.Count == 5 && s.Rules.Skip(3).All(r => r.Actions[0].Verb == ActVerb.Use && r.Actions[0].Loop == LoopMode.Forever)
                  && s.Rules.Take(3).All(r => r.Actions[0].Verb == ActVerb.Move), "五个任务：前三个放入，最后两个增压（无限循环）");
            check(s.Rules.All(r => Check.RuleErrors(d, s, r, w).Count == 0), "示例没有结构错误");

            var a = new Apprentice { Name = "甲", MaxAp = 200, Level = 2, Duties = { new Duty { ScheduleId = s.Id, Shift = Shift.Morning } } };
            GroupRunReport Shift1() => new Interpreter(w, d).RunShift(Shift.Morning, new[] { a }).Groups[0];
            SimItem Child() => hydro.Children.FirstOrDefault(c => c.Kind == "hydroreed_seed" && c.InSlot == "slot:output");

            var g = Shift1();
            check(SimWorld.Rank(seed, "purifying") == 5 && seed.N(SimWorld.HeatTag) == 5, $"第一个班：增压两次写完，净化水质 5、热度 5（{SimWorld.Rank(seed, "purifying")}，{seed.N(SimWorld.HeatTag)}）");
            check(disk.InSlot == "slot:eject" && g.Rules[3].Done.GetValueOrDefault(ActVerb.Use) == 2, "写完盘弹出，热度到上限，增压写入停");
            check(booster.N(SimWorld.BoostChargeTag) == 15000 - 2 * SimWorld.BoostCost, "用了两次加速");

            Shift1();
            var child = Child();
            check(disk.InSlot == "slot:eject" && seed.Parent == null && child != null, "第二个班：盘留在输出格，种子送去繁殖，增压催熟收获出子代");
            check(child != null && SimWorld.Rank(child, "purifying") == 5 && child.N(SimWorld.HeatTag) == 0, "子代继承净化水质 5，热度 0");

            Shift1();
            check(child != null && child.Parent == writer && SimWorld.Rank(child, "purifying") == 6, $"第三个班：子代回写入机，写到 6（{(child == null ? -1 : SimWorld.Rank(child, "purifying"))}）");

            // 没有增压器：增压的两个任务执行条件不成立，不报缺少工具；机器夜里自己做一个周期
            w.Remove(booster);
            g = Shift1();
            check(g.Rules.Skip(3).All(r => r.Pre == PreResult.Fail && r.Issues.Count == 0), "没有增压器：最后两个任务不执行，也不报问题");
        });
    }
}
