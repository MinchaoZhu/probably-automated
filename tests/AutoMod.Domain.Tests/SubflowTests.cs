using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Simulation;
using AutoMod.Domain.Workforce;
// 子流程（执行流程）的单元测试。
internal static class SubflowTests
{
    private static Action<bool, string> _check;
    private static void Check(bool ok, string what) => _check(ok, what);

    /// <summary>
    /// 两组储藏区：甲区 3 块电池、乙区 2 块电池，两个空的放置区。子流程“搬运”把“来源”里的电池放进“放置区”，
    /// 每次最多搬“件数”块。本流程参数：件数（数值 9）、放置区（设备 =【放置区1】）、来源（列表 = 甲区电池）。
    /// </summary>
    private sealed class Shop
    {
        public SimWorld W = FakeGame.Install(new SimWorld(7));
        public SimItem A, B, Dst1, Dst2;
        public RulesData D = new();
        public Schedule Sub;

        public Shop()
        {
            A = W.AddRoot("storage", 1, "甲区");
            B = W.AddRoot("storage", 2, "乙区");
            Dst1 = W.AddRoot("storage", 3, "放置区1");
            Dst2 = W.AddRoot("storage", 4, "放置区2");
            for (int i = 0; i < 3; i++) W.Add(A, "battery_full");
            for (int i = 0; i < 2; i++) W.Add(B, "battery_full");
            A.DeviceName = "甲区"; B.DeviceName = "乙区"; Dst1.DeviceName = "放置区1"; Dst2.DeviceName = "放置区2";

            Sub = new Schedule { Id = "sub", Name = "搬运" };
            D.Schedules.Add(Sub);
            D.Objects.Add(new ObjectDef { Id = "n", Name = "件数", Owner = "sub", Value = 9 });
            D.Objects.Add(new ObjectDef { Id = "dst", Name = "放置区", Owner = "sub", Type = ParamType.Device, Ref = "#放置区1" });
            D.Objects.Add(new ObjectDef { Id = "src", Name = "来源", Owner = "sub", Type = ParamType.List, Ref = "aBats" });
            D.Selectors.Add(new SelectorDef { Id = "aBats", Name = "甲区电池", Owner = "sub", From = { "#甲区" }, Kind = "battery" });
            D.Selectors.Add(new SelectorDef
            {
                Id = "batch", Name = "这一批", Owner = "sub", From = { "src" }, Kind = "battery",
                Take = new TakeSpec { Mode = TakeMode.First, N = Expr.Ref(TermKind.Const, "n") },
            });
            Sub.Rules.Add(new WorkRule { Id = "r_sub", Name = "搬过去", Actions = { new ActionDef { Verb = ActVerb.Move, List = "batch", To = "dst" } } });
        }

        public Schedule Caller(params WorkRule[] rules)
        {
            var s = new Schedule { Id = "main", Name = "总流程" };
            s.Rules.AddRange(rules);
            D.Schedules.Add(s);
            return s;
        }

        public RunReport Run(Schedule s, int ap = 80) =>
            new Interpreter(W, D).RunShift(Shift.Morning, new[] { new Apprentice { Name = "甲", MaxAp = ap, Level = 2, Duties = { new Duty { ScheduleId = s.Id, Shift = Shift.Morning } } } });
    }

    private static WorkRule CallRule(string id, string flow, params CallArg[] args) =>
        new() { Id = id, Name = id, Actions = { new ActionDef { Verb = ActVerb.Call, Flow = flow, Args = args.Length == 0 ? null : args.ToList() } } };

    private static int Bats(SimItem box) => box.Children.Count(c => SimWorld.InGroup(c, "battery"));

    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        _check = check;

        test("参数：设备、列表参数当设备名 / 筛选器用", () =>
        {
            var shop = new Shop();
            var d = shop.D;
            var c = new EvalCtx(shop.W, d);
            Check(Eval.List(c, "src").Count == 3 && Eval.List(c, "batch").Count == 3, "列表参数取它的值；筛选器从列表参数里再筛");
            Check(Eval.Boxes(c, "dst").SequenceEqual(new object[] { shop.Dst1 }), "设备参数是叫那个名字的东西");
            Check(Eval.Expr(c, Expr.Ref(TermKind.Const, "dst")) == null, "设备参数不是数值");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, shop.Sub, shop.Sub.Rules[0]).Count == 0, "引用参数的任务没有错误");
            Check(AutoMod.Domain.Rules.Check.ExprErrors(d, Expr.Ref(TermKind.Const, "dst"), ExprScope.Runtime).Any(), "数值里引用设备参数报错");
            d.Objects.First(o => o.Id == "dst").Ref = null;
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, shop.Sub, shop.Sub.Rules[0]).Any(e => e.Contains("放置区")), "设备参数没选值报错");
            d.Objects.First(o => o.Id == "dst").Ref = "#放置区1";
            Check(AutoMod.Domain.Rules.Check.Referrers(d, "aBats").Any(x => x.Contains("来源")), "筛选器被列表参数引用，删除前列出");
            Check(Card.Parameters(d, shop.Sub).Count == 3 && Card.DeviceNames(d, shop.Sub).Contains("放置区1"), "卡片：三个参数；设备参数的值也算用到的设备名");
            Check(Text.ParamValue(d, d.Objects.First(o => o.Id == "dst")) == "【放置区1】" && Text.ParamValue(d, d.Objects.First(o => o.Id == "n")) == "9", "参数的值的写法");

            var other = new Schedule { Id = "o", Name = "另一个" };
            d.Selectors.Add(new SelectorDef { Id = "bBats", Name = "乙区电池", Owner = "o", From = { "#乙区" }, Kind = "battery" });
            other.Rules.Add(new WorkRule { Id = "r_o", Name = "r", Actions = { new ActionDef { Verb = ActVerb.Move, List = "bBats", To = "#放置区2" } } });
            d.Schedules.Add(other);
            var p = Defs.DeviceToParam(d, other, "放置区2");
            Check(other.Rules[0].Actions[0].To == p.Id && p.Type == ParamType.Device && p.Ref == "#放置区2" && p.Owner == "o", "设备名转为参数：引用改成参数");
            new Interpreter(shop.W, d).RunShift(Shift.Morning, new[] { new Apprentice { Name = "乙", MaxAp = 80, Level = 2, Duties = { new Duty { ScheduleId = "o", Shift = Shift.Morning } } } });
            Check(Bats(shop.Dst2) == 2, $"转换后照样执行（放置区2 {Bats(shop.Dst2)}）");
        });

        test("子流程：直接指派用默认值", () =>
        {
            var shop = new Shop();
            shop.Run(shop.Sub);
            Check(Bats(shop.A) == 0 && Bats(shop.Dst1) == 3, $"甲区 3 块都搬到放置区1（甲区 {Bats(shop.A)}，放置区1 {Bats(shop.Dst1)}）");
        });

        test("子流程：调用方覆盖列表、设备、数值参数，报告接在调用后面", () =>
        {
            var shop = new Shop();
            shop.D.Selectors.Add(new SelectorDef { Id = "bSrc", Name = "乙区电池", Owner = "main", From = { "#乙区" }, Kind = "battery" });
            var main = shop.Caller(
                CallRule("默认", "sub"),
                CallRule("改参数", "sub", new CallArg { Param = "src", Ref = "bSrc" }, new CallArg { Param = "dst", Ref = "#放置区2" }, new CallArg { Param = "n", Num = 1 }));
            var g = shop.Run(main).Groups[0];
            Check(Bats(shop.Dst1) == 3, $"第一次调用用默认值：甲区 3 块进放置区1（{Bats(shop.Dst1)}）");
            Check(Bats(shop.Dst2) == 1 && Bats(shop.B) == 1, $"第二次调用：来源换成乙区、放到放置区2、件数 1（放置区2 {Bats(shop.Dst2)}，乙区 {Bats(shop.B)}）");
            var names = g.Rules.Select(r => $"{r.Depth}:{r.Name}").ToList();
            Check(names.SequenceEqual(new[] { "0:默认", "1:搬运 › 搬过去", "0:改参数", "1:搬运 › 搬过去" }), string.Join(" | ", names));
            Check(g.Rules[0].Called == "搬运" && g.Rules[0].Ap == 0 && g.Rules[1].Ap > 0, "调用这一条记 0 AP，子流程的任务各算各的");
            Check(Math.Abs(g.Rules.Sum(r => r.Ap) - g.ApUsed) < 1e-9, $"各条 AP 加起来等于这一组用的 AP（{g.Rules.Sum(r => r.Ap)} / {g.ApUsed}）");
            Check(g.Rules[1].FlowId == "sub" && g.ScheduleId == "main", "子流程的任务报告记在调用方那一组");
        });

        test("子流程：AP 用完整条调用链都停", () =>
        {
            var shop = new Shop();
            var main = shop.Caller(CallRule("调用", "sub"),
                new WorkRule { Id = "后面", Name = "后面", Actions = { new ActionDef { Verb = ActVerb.Move, List = "#乙区", To = "#放置区2" } } });
            var g = shop.Run(main, ap: 3).Groups[0];
            Check(Bats(shop.Dst1) is > 0 and < 3, $"子流程搬了一部分就没 AP 了（{Bats(shop.Dst1)}）");
            Check(g.Rules.Last().Name == "后面" && g.Rules.Last().Pre == PreResult.NotRun, $"调用方后面的任务不再执行（{g.Rules.Last().Pre}）");
        });

        test("子流程：检查成环、嵌套层数、流程不存在、参数传入的筛选器不存在", () =>
        {
            var shop = new Shop();
            var d = shop.D;
            var main = shop.Caller(CallRule("调用", "sub"));
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, main, main.Rules[0]).Count == 0, "正常的调用没有错误");
            shop.Sub.Rules.Add(CallRule("回调", "main"));
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, main, main.Rules[0]).Any(e => e.Contains("搬运")), "成环：调用方的这条任务报错");
            shop.Sub.Rules.RemoveAt(1);
            var self = CallRule("自己", "main");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, main, self).Count > 0, "调用自己报错");
            // 一条链：c0 → c1 → c2 → c3 → c4
            for (int i = 0; i < 5; i++) d.Schedules.Add(new Schedule { Id = "c" + i, Name = "c" + i, Rules = { i < 4 ? CallRule("下一层", "c" + (i + 1)) : new WorkRule { Id = "end", Name = "end", Actions = { new ActionDef { Verb = ActVerb.Move, List = "#甲区", To = "#放置区2" } } } } });
            var top = d.Schedules.First(s => s.Id == "c0");
            var second = d.Schedules.First(s => s.Id == "c1");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, top, top.Rules[0]).Count > 0, "套 4 层超过上限，报错");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, second, second.Rules[0]).Count == 0, "套 3 层可以");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, main, CallRule("没了", "gone")).Count > 0, "流程不存在报错");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, main, CallRule("坏参数", "sub", new CallArg { Param = "src", Ref = "deleted" })).Count > 0, "参数传入的筛选器不存在报错");
            var stale = CallRule("旧参数", "sub", new CallArg { Param = "gone", Ref = "deleted" }, new CallArg { Param = "n", Num = 2 });
            main.Rules.Add(stale);
            Scope.Normalize(d);
            Check(stale.Actions[0].Args.Count == 1 && stale.Actions[0].Args[0].Param == "n", "读档整理：去掉已经不是子流程参数的覆盖");
            d.Objects.First(o => o.Id == "n").Owner = null;
            Scope.Normalize(d);
            Check(stale.Actions[0].Args == null, "参数改成全局后，调用方对它的覆盖去掉");
        });

        test("子流程：参数、复制、卡片句子、工作构成", () =>
        {
            var shop = new Shop();
            var d = shop.D;
            d.Objects.Add(new ObjectDef { Id = "g", Name = "全局阈值", Value = 1 });
            Check(Calls.KindOf(d, shop.Sub, "n") == Calls.ParamKind.Number && Calls.KindOf(d, shop.Sub, "src") == Calls.ParamKind.List
                  && Calls.KindOf(d, shop.Sub, "dst") == Calls.ParamKind.Device, "参数种类：数值、列表、设备");
            Check(Calls.Params(d, shop.Sub).SequenceEqual(new[] { "n", "dst", "src" }) && Calls.KindOf(d, shop.Sub, "g") == Calls.ParamKind.None,
                "能覆盖的是本流程参数，全局参数不能");

            var copy = Scope.CopySchedule(d, shop.Sub, "搬运 2");
            var copied = d.Objects.First(o => o.Owner == copy.Id && o.Name == "来源");
            Check(copied.Ref != "aBats" && d.Selectors.First(x => x.Id == copied.Ref).Owner == copy.Id, "复制流程：列表参数的值指向复制出的筛选器");

            var main = shop.Caller(CallRule("改参数", "sub", new CallArg { Param = "dst", Ref = "#放置区2" }, new CallArg { Param = "n", Num = 2 },
                new CallArg { Param = "g", Num = 5 }, new CallArg { Param = "src", Num = 3 }));
            Scope.Normalize(d);
            Check(main.Rules[0].Actions[0].Args.Count == 2, "读档整理：去掉全局参数和类型对不上的覆盖");
            string line = string.Join(" / ", Card.Sentence(d, main, main.Rules[0]).Select(l => string.Concat(l.Select(x => x.Text))));
            Check(line.Contains("执行流程") && line.Contains("【搬运】") && line.Contains("件数 = 2") && line.Contains("放置区 = 【放置区2】"), line);
            Check(Card.Sentence(d, main, main.Rules[0]).SelectMany(l => l).Any(x => x.Param == "flow" && x.Kind == SegKind.Type), "流程名可点");
            Check(Card.DeviceNames(d, main).Contains("放置区2"), "调用方卡片列出覆盖用的设备名");
            Check(Calls.Callers(d, shop.Sub).SequenceEqual(new[] { main }), "被哪些流程调用");
            var profile = Proficiency.ProfileOf(d, main, shop.W);
            Check(profile.GetValueOrDefault(ActVerb.Move) > 0, "调用方的工作构成包括子流程里的活");
        });

        test("子流程：调用方把自己的参数传下去", () =>
        {
            var shop = new Shop();
            var d = shop.D;
            d.Objects.Add(new ObjectDef { Id = "mDst", Name = "去处", Owner = "main", Type = ParamType.Device, Ref = "#放置区2" });
            var main = shop.Caller(CallRule("传参数", "sub", new CallArg { Param = "dst", Ref = "mDst" }));
            Check(AutoMod.Domain.Rules.Check.RuleErrors(d, main, main.Rules[0]).Count == 0, "传自己的设备参数没有错误");
            shop.Run(main);
            Check(Bats(shop.Dst2) == 3 && Bats(shop.Dst1) == 0, $"放到调用方参数的值【放置区2】（{Bats(shop.Dst2)}）");
        });

        test("子流程：导出带上子流程，导入后照样能跑", () =>
        {
            var shop = new Shop();
            shop.D.Selectors.Add(new SelectorDef { Id = "bSrc", Name = "乙区电池", Owner = "main", From = { "#乙区" }, Kind = "battery" });
            var main = shop.Caller(CallRule("改参数", "sub", new CallArg { Param = "src", Ref = "bSrc" }, new CallArg { Param = "n", Num = 1 }));
            string text = Transfer.Export(shop.D, main, "test");

            var fresh = new Shop();
            fresh.D = new RulesData();
            var imported = Transfer.Import(fresh.D, text);
            Check(fresh.D.Schedules.Count == 2, $"导入了流程和它调用的子流程（{fresh.D.Schedules.Count}）");
            var sub = Calls.Callee(fresh.D, imported.Rules[0].Actions[0]);
            Check(sub != null && sub.Name == "搬运" && sub.Id != "sub", "调用指向导入的子流程（新 Id）");
            Check(sub != null && Calls.Params(fresh.D, sub).Count == 3 && fresh.D.Objects.First(o => o.Owner == sub.Id && o.Name == "来源").Ref is { } r
                  && fresh.D.Selectors.Any(x => x.Id == r && x.Owner == sub.Id), "子流程的参数归导入的子流程，列表参数的值换成新 Id");
            var arg = imported.Rules[0].Actions[0].Args.First(x => x.Num == null);
            Check(sub != null && Calls.Params(fresh.D, sub).Contains(arg.Param) && fresh.D.Selectors.Any(x => x.Id == arg.Ref && x.Owner == imported.Id), "覆盖的参数和传入的筛选器都换成新 Id");
            Check(AutoMod.Domain.Rules.Check.RuleErrors(fresh.D, imported, imported.Rules[0]).Count == 0, "导入后没有错误");
            fresh.Run(imported);
            Check(Bats(fresh.Dst1) == 1 && Bats(fresh.B) == 1, $"导入后执行：乙区搬 1 块到放置区1（{Bats(fresh.Dst1)}）");
        });
    }
}
