using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Workforce;

namespace AutoMod.Domain.Rules;

// ================= 报告 =================

public class RunIssue
{
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public int Count { get; set; }
}

public class RuleRunReport
{
    public string RuleId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>满足 / 不满足（跳过）/ 未执行 / 已停用 / 结构错误</summary>
    public string Pre { get; set; } = "";

    /// <summary>没执行的原因（例：“AP 不足（前面的任务已用 11 / 共 11 AP）”），可为空。</summary>
    public string Why { get; set; }

    /// <summary>执行了但一件也没做、也没有问题时的原因（“「作物输出」当前为空”），可为空。</summary>
    public string Idle { get; set; }

    /// <summary>动作 → 完成件数。</summary>
    public Dictionary<string, int> Done { get; set; } = new();

    public double Ap { get; set; }
    public int Deferred { get; set; }

    public List<RunIssue> Issues { get; set; } = new();
    public List<string> Details { get; set; } = new();

    /// <summary>物品处理事件，按发生顺序。</summary>
    public List<ItemHandled> Items { get; set; } = new();

    public void Issue(string code, string text, int n = 1)
    {
        var x = Issues.FirstOrDefault(i => i.Code == code && i.Text == text);
        if (x == null) Issues.Add(x = new RunIssue { Code = code, Text = text });
        x.Count += n;
    }
}

/// <summary>物品处理事件：哪条规则的哪个操作处理了哪件物品，结果、AP 与效果。</summary>
public class ItemHandled
{
    public string Op { get; set; } = "";
    public int Uid { get; set; }
    public string Item { get; set; } = "";
    public string Outcome { get; set; } = Outcomes.Done;
    public string Text { get; set; } = "";
    public double Ap { get; set; }
    public List<Effect> Effects { get; set; } = new();
}

public static class Outcomes
{
    public const string Done = "done";
    public const string Issue = "issue";

    /// <summary>轮到它时 AP 已经用完，留到下次（报告据此写“AP 不足”）。</summary>
    public const string Deferred = "deferred";
}

public class GroupRunReport
{
    public string ScheduleId { get; set; } = "";
    public string ScheduleName { get; set; } = "";
    public List<string> Workers { get; set; } = new();
    public double ApTotal { get; set; }
    public double ApUsed { get; set; }

    /// <summary>组的 AP 系数（100 / 有效熟练度，按 AP 加权平均）。</summary>
    public double Mult { get; set; } = 1;

    /// <summary>学徒 → 有效熟练度（班次开始时）。</summary>
    public Dictionary<string, int> Proficiency { get; set; } = new();

    /// <summary>班后熟练度的变化（“甲 熟练度 76 → 81”）。</summary>
    public List<string> Learned { get; set; } = new();

    public int Deferred { get; set; }


    public Dictionary<string, double> Memo { get; set; } = new();
    public List<RuleRunReport> Rules { get; set; } = new();
    public List<string> LevelUps { get; set; } = new();
}

public class RunReport
{
    public string Shift { get; set; } = "";
    public int Day { get; set; }
    public List<GroupRunReport> Groups { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<ReportEntry> Extra { get; set; } = new();
}

public static class PreResult
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string NotRun = "notrun";
    public const string Disabled = "disabled";
    public const string Broken = "broken";

    public static string Name(string code) => code switch
    {
        Pass => L.T("report.pre.pass"),
        Fail => L.T("report.pre.fail"),
        NotRun => L.T("report.pre.notrun"),
        Disabled => L.T("report.pre.disabled"),
        Broken => L.T("report.pre.broken"),
        _ => code,
    };
}

/// <summary>
/// 排班解释器。游戏、模拟器、单元测试共用。
/// </summary>
public class Interpreter
{
    public const double Walk = 1;

    private readonly IWorld _w;
    private readonly RulesData _d;

    /// <summary>结构错误检查（为空则不检查）。返回错误文字列表。</summary>
    public Func<Schedule, WorkRule, List<string>> Errors;


    public Interpreter(IWorld w, RulesData d)
    {
        _w = w;
        _d = d;
        Errors = (s, r) => Check.RuleErrors(d, s, r, w);
    }

    // ================= 班次调度 =================

    /// <summary>
    /// 执行一个班次：按学徒列表的顺序，每名学徒依次执行自己在这个班次的流程（串行，用自己当天剩下的 AP）。
    /// workers：当班学徒（按学徒列表顺序）。applyXp：是否把经验写回学徒（模拟器传副本时也可以写）。
    /// </summary>
    public RunReport RunShift(string shift, IList<Apprentice> workers, bool applyXp = true)
    {
        var report = new RunReport { Shift = shift, Day = _w.Day };
        foreach (var a in workers)
        {
            var training = Agency.ApplyTraining(a, !applyXp);
            if (training != null) report.Extra.Add(training);
        }
        if (workers.Count == 0) report.Notes.Add(L.T("report.note.no_apprentices"));
        foreach (var a in workers)
            foreach (var duty in a.DutiesIn(shift).ToList())
            {
                var s = _d.Schedules.FirstOrDefault(x => x.Id == duty.ScheduleId);
                if (s != null) report.Groups.Add(RunSchedule(s, a, shift, applyXp));
            }
        return report;
    }

    /// <summary>真实执行时设为 true：班后更新排班的工作构成（模拟器、预估不改真实排班）。</summary>
    public bool UpdateProfiles { get; set; }

    // ================= 执行一个排班 =================

    /// <summary>一名学徒执行一个流程：AP 从他当天剩下的里扣（小数累计，显示的整数是累计值向上取整之差）。</summary>
    public GroupRunReport RunSchedule(Schedule s, Apprentice a, string shift, bool applyXp = true)
    {
        var gr = new GroupRunReport { ScheduleId = s.Id, ScheduleName = s.Name, Workers = { a.Name } };
        double startUsed = a.ApUsed;
        int Spent() => ApMath.Ceil(a.ApUsed) - ApMath.Ceil(startUsed);
        gr.ApTotal = a.ApLeft;
        var profile = Proficiency.ProfileOf(_d, s, _w);
        int effective = Proficiency.Effective(a, profile);
        gr.Proficiency[a.Name] = effective;
        gr.Mult = Proficiency.CostMult(effective);
        var work = new Dictionary<string, double>();

        var memo = s.Notepad.ToDictionary(n => n.Id, n => n.Init);
        double xp = 0;
        bool stopped = false;

        foreach (var rule in s.Rules)
        {
            var rr = new RuleRunReport { RuleId = rule.Id, Name = rule.Name };
            gr.Rules.Add(rr);
            if (!rule.Enabled) { rr.Pre = PreResult.Disabled; continue; }
            var errs = Errors?.Invoke(s, rule) ?? new List<string>();
            if (errs.Count > 0)
            {
                rr.Pre = PreResult.Broken;
                rr.Issue("broken", L.T("report.error.broken", L.Join("semi", errs)));
                continue;
            }
            var ctx = new EvalCtx(_w, _d) { Memo = memo, Sys = v => v switch
            {
                SysVar.ApLeft => a.ApLeft, SysVar.Shift => shift == Shift.Morning ? 1 : 2, SysVar.Day => _w.Day, _ => null,
            } };
            if (stopped)
            {
                // AP 已在前面的任务里用完：只挑不做，把会处理的物品记成“留到下次”，让界面能写出原因
                if (!PreHolds(ctx, rule)) { rr.Pre = PreResult.Fail; rr.Why = PreFailText(ctx, s, rule); continue; }
                rr.Pre = PreResult.NotRun;
                rr.Why = L.T("report.why.no_ap", Text.Ap(Spent(), gr.ApTotal));
                var seen = new HashSet<int>();
                foreach (var act in rule.Actions.Where(a => a.Verb != ActVerb.Note))
                    foreach (var x in Eval.List(ctx, act.List))
                        if (seen.Add(_w.Uid(x)))
                            rr.Items.Add(new ItemHandled { Op = act.Verb, Uid = _w.Uid(x), Item = _w.Describe(x), Outcome = Outcomes.Deferred, Text = rr.Why });
                rr.Deferred = seen.Count;
                gr.Deferred += seen.Count;
                continue;
            }
            if (!PreHolds(ctx, rule)) { rr.Pre = PreResult.Fail; rr.Why = PreFailText(ctx, s, rule); continue; }
            rr.Pre = PreResult.Pass;

            bool walked = false;
            int ruleStart = ApMath.Ceil(a.ApUsed);
            bool Pay(double cost)
            {
                double need = cost + (walked ? 0 : Walk);
                if (a.ApUsed + need > a.DailyAp + 1e-9) return false;
                if (!walked)
                {
                    walked = true;
                    a.ApUsed += Walk;
                    rr.Details.Add(L.T("report.details.walk_cost", Walk));
                }
                a.ApUsed += cost;
                return true;
            }

            foreach (var act in rule.Actions)
            {
                if (act.Verb == ActVerb.Note)
                {
                    double v = Eval.Expr(ctx, act.Value) ?? 0;
                    if (act.Note != null)
                    {
                        memo[act.Note] = act.NoteMode == "set" ? v : memo.GetValueOrDefault(act.Note) + v;
                        rr.Details.Add(L.T("report.details.record", s.Notepad.FirstOrDefault(n => n.Id == act.Note)?.Name, memo[act.Note].ToString("0.##")));
                    }
                    continue;
                }
                var op = Catalog.Op(act.Verb);
                var list = Eval.List(ctx, act.List);
                bool use = op.Mechanism == Mechanism.Apply;
                bool trigger = op.Mechanism == Mechanism.Trigger;
                bool toggle = op.Id == ActVerb.Toggle;
                string how = trigger ? Triggers.Of(act) : null;
                // 使用：工具就是 Tool 这个集合（按顺序试）；放入：目标容器按顺序试；交换：和 To 按顺序一一配对
                var tools = use ? ToolPool(ctx, act.Tool).ToList() : null;
                var targets = op.Mechanism == Mechanism.Place ? Eval.Boxes(ctx, act.To) : null;
                var partners = op.Mechanism == Mechanism.Swap ? Eval.List(ctx, act.To) : null;
                int times = toggle || !(use || trigger) ? 1 : Math.Max(1, act.Times);
                var paired = new HashSet<object>();
                int full = 0, notAllowed = 0, noEffect = 0, notOk = 0, noPartner = 0, doneHere = 0;
                bool toolMissing = use && tools.Count == 0;
                bool Ok(object x) => Applicable(op, act, x, targets);

                for (int k = 0; k < list.Count && !toolMissing; k++)
                {
                    var item = list[k];
                    if (!Ok(item)) { notOk++; continue; }

                    object tool = null, other = null;
                    switch (op.Mechanism)
                    {
                        case Mechanism.Apply:
                            tool = tools.FirstOrDefault(t => !Equals(_w.Key(t), _w.Key(item)) && _w.Exists(t) && CanUse(t, item));
                            if (tool == null) { notAllowed++; continue; }
                            break;
                        case Mechanism.Trigger:
                            if (!_w.CanTrigger(item, how)) { notAllowed++; continue; }
                            break;
                        case Mechanism.Place:
                            if (!targets.Any(b => _w.Fits(b, item))) { full++; continue; }
                            break;
                        case Mechanism.Swap:
                            // 按顺序取下一件还没配对的；同一处的两件互换没有意义，跳过
                            var parent = _w.Parent(item);
                            other = partners.FirstOrDefault(x => !paired.Contains(_w.Key(x)) && !Equals(_w.Key(x), _w.Key(item)) && _w.Exists(x)
                                                                 && !Equals(_w.Key(_w.Parent(x) ?? x), _w.Key(parent ?? item)));
                            if (other == null) { noPartner++; continue; }
                            break;
                    }

                    string cat = op.Skill ?? Catalog.SkillOf(_w, item);
                    double cost = op.Cost * times * gr.Mult;
                    bool firstPaid = !walked;
                    int before = ApMath.Ceil(a.ApUsed);
                    if (!Pay(cost))
                    {
                        var rest = list.Skip(k).Where(Ok).ToList();
                        int left = rest.Count;
                        foreach (var x in rest)
                            rr.Items.Add(new ItemHandled { Op = op.Id, Uid = _w.Uid(x), Item = _w.Describe(x), Outcome = Outcomes.Deferred, Text = L.T("report.text.no_ap") });
                        rr.Deferred += left;
                        gr.Deferred += left;
                        stopped = true;
                        break;
                    }

                    string desc = _w.Describe(item);
                    int uid = _w.Uid(item);
                    StepResult res = op.Mechanism switch
                    {
                        Mechanism.Place => PlaceInto(item, targets, act.To),
                        Mechanism.Swap => _w.Swap(item, other),
                        Mechanism.Apply => UseTool(tool, item, tools, times),
                        Mechanism.Trigger => TriggerTimes(item, how, times),
                        Mechanism.Annotate => _w.Annotate(item, act.Text ?? "", op.Id == ActVerb.Mark),
                        _ => StepResult.Fail(L.T("report.fail.unknown_action"), FailCode.Error),
                    };
                    if (!res.Ok)
                    {
                        // 没做成不消耗 AP：退回刚扣的；这是这条任务第一次动手的话，启动开销也退回
                        a.ApUsed -= cost;
                        if (firstPaid)
                        {
                            walked = false;
                            a.ApUsed -= Walk;
                            rr.Details.Remove(L.T("report.details.walk_cost", Walk));
                        }
                        if (res.Code == FailCode.DestFull) full++;
                        else if (res.Code == FailCode.NoEffect) noEffect++;
                        else
                        {
                            rr.Issue(res.Code ?? FailCode.NotAllowed, L.T("report.issue.op_failed", op.Name, res.Reason));
                            rr.Items.Add(new ItemHandled { Op = op.Id, Uid = uid, Item = desc, Outcome = Outcomes.Issue, Text = res.Reason });
                        }
                        continue;
                    }
                    if (op.Mechanism == Mechanism.Swap) paired.Add(_w.Key(other));
                    doneHere++;
                    rr.Done[act.Verb] = rr.Done.GetValueOrDefault(act.Verb) + 1;
                    int itemAp = ApMath.Ceil(a.ApUsed) - before; // 这一件让整数 AP 多了几点（含启动开销）
                    rr.Details.Add($"{op.Name} {desc}：{res.Text} · {itemAp} AP");
                    rr.Items.Add(new ItemHandled { Op = op.Id, Uid = uid, Item = desc, Text = res.Text, Ap = itemAp, Effects = res.Effects });
                    Proficiency.Add(work, op.Dim ?? op.Id, cat, cost);
                    xp += SkillMath.XpGain(op.Cost * times, effective);
                }

                // 工具集合此刻是空的：有对象要处理就报缺少工具（不想报就在执行条件里写“工具 不为空”）
                if (toolMissing)
                {
                    int n = list.Count(Ok);
                    if (n > 0) rr.Issue(FailCode.NoTool, L.N("report.issue.no_tool", n, NameOf(act.Tool), n, op.Name), 1);
                }
                if (notAllowed > 0) rr.Issue(FailCode.NotAllowed, L.N("report.issue.not_allowed", notAllowed, op.Name, notAllowed));
                if (noEffect > 0) rr.Issue(FailCode.NoEffect, L.N("report.issue.no_effect", noEffect, op.Name, noEffect));
                if (full > 0) rr.Issue(FailCode.DestFull, L.N("report.issue.dest_full", full, NameOf(act.To), full));
                if (doneHere > ManyThreshold)
                    rr.Issue(FailCode.Many, L.T("report.issue.many", op.Name, doneHere, NameOf(act.List)));
                // 一件也没做、也没有别的问题：记下原因（报告、策略模拟器显示）
                if (doneHere == 0 && !stopped && rr.Idle == null)
                    rr.Idle = list.Count == 0 ? Eval.Explain(ctx, act.List)
                        : notOk == list.Count ? (op.Mechanism switch
                        {
                            Mechanism.Place => L.T("report.idle.all_in_dest", NameOf(act.List)),
                            Mechanism.Annotate => op.Id == ActVerb.Mark ? L.T("report.idle.all_marked", NameOf(act.List)) : L.T("report.idle.none_marked", NameOf(act.List)),
                            _ => L.T("report.idle.no_items", NameOf(act.List)),
                        })
                        : noPartner > 0 && partners != null ? (partners.Count == 0 ? Eval.Explain(ctx, act.To) : L.T("report.idle.same_place", NameOf(act.List), NameOf(act.To)))
                        : null;
                if (stopped) break;
            }
            rr.Ap = ApMath.Ceil(a.ApUsed) - ruleStart;
        }

        gr.ApUsed = Spent();
        gr.Memo = memo;
        if (applyXp)
        {
            if (SkillMath.AddXp(a, Scales.Xp(xp)) > 0) gr.LevelUps.Add(L.T("report.growth.levelup", a.Name, a.Level, a.DailyAp));
            // 熟练度：学徒往这一班的实际工作靠近
            if (work.Count > 0)
            {
                int was = Proficiency.Score(a.Proficiency, profile);
                Proficiency.Learn(a, work, gr.ApUsed);
                int now = Proficiency.Score(a.Proficiency, profile);
                if (now != was) gr.Learned.Add(L.T("report.growth.learned", a.Name, was, now));
            }
        }
        if (UpdateProfiles && work.Count > 0) s.Profile = Proficiency.Blend(profile, work);
        return gr;
    }

    private string NameOf(string id) => Text.Name(_d, id);

    /// <summary>一个任务一次处理超过这么多件时，在报告里提示（多半是筛选器选得太宽）。</summary>
    public const int ManyThreshold = 30;

    /// <summary>执行条件里第一条不成立的写法：写出实际情况（“种子槽中的种子 不为空”）。</summary>
    private static string PreFailText(EvalCtx c, Schedule s, WorkRule rule)
    {
        foreach (var p in rule.Pre)
            if (!PreHolds(c, new WorkRule { Pre = { p } })) return Text.PreNot(c.D, s, p);
        return null;
    }

    /// <summary>放进目标里第一个放得下的（目标的顺序就是优先顺序）。结果文字写排班里的名字：目标是设备名写设备名（“移至 满电架”），是筛选器写“移至 充能器#3（空闲充能器）”。</summary>
    private StepResult PlaceInto(object item, IReadOnlyList<object> targets, string toId)
    {
        foreach (var b in targets)
            if (_w.Fits(b, item))
            {
                var res = _w.Place(item, b);
                if (res.Ok && toId != null)
                {
                    if (Places.IsName(toId)) res.Text = L.T("report.step.moved_to", NameOf(toId));
                    else if (_d.Selectors.Any(x => x.Id == toId)) res.Text += L.T("text.paren", NameOf(toId));
                }
                return res;
            }
        return new StepResult { Ok = false, Code = FailCode.DestFull, Reason = L.T("report.fail.dest_full") };
    }

    /// <summary>
    /// 工具集合：设备名 = 起了这个名字的东西本身和它们里面的；全店 = 店里所有的；筛选器 = 它选出的；
    /// 水龙头这类固定设施 = 它自己。
    /// </summary>
    public static IEnumerable<object> ToolPool(EvalCtx ctx, string source)
    {
        if (string.IsNullOrEmpty(source)) return Array.Empty<object>();
        if (Fixtures.Is(source)) return ctx.W.Fixture(source) is { } f ? new[] { f } : Array.Empty<object>();
        if (Places.IsShop(source)) return ctx.W.Shop();
        if (Places.IsName(source))
        {
            var named = Eval.Boxes(ctx, source);
            return named.Concat(named.SelectMany(b => ctx.W.Contents(b, null)));
        }
        return Eval.List(ctx, source);
    }

    /// <summary>触发 times 次（开关只 1 次）；第一次失败原样返回，之后的失败（例如用完了）只是提前结束。</summary>
    private StepResult TriggerTimes(object item, string how, int times)
    {
        StepResult first = null;
        var results = new List<StepResult>();
        for (int n = 0; n < times; n++)
        {
            if (n > 0 && (!_w.Exists(item) || !_w.CanTrigger(item, how))) break;
            var r = _w.Trigger(item, how, null);
            if (!r.Ok) { if (n == 0) return r; break; }
            if (first == null) first = r; else first.Effects.AddRange(r.Effects);
            results.Add(r);
        }
        if (results.Count > 1) first = MergeResults(results);
        return first;
    }

    /// <summary>
    /// 方向由游戏判断：工具能作用于对象就这样做；不能、但对象能作用于工具（空桶接水龙头、鼠放进屠宰机）就反过来。
    /// 两个方向都行时（两个液体容器）按句子的方向：工具倒进对象。
    /// </summary>
    private bool CanUse(object tool, object item) => _w.CanApply(tool, item) || _w.CanApply(item, tool);

    private StepResult ApplyEither(object tool, object item) =>
        _w.CanApply(tool, item) ? _w.Apply(tool, item, null) : _w.Apply(item, tool, null);

    /// <summary>
    /// 用工具作用于对象，作用 times 次。每次前重新问游戏能否作用；这件工具不能再用了（用掉了、坏了）就换下一件能用的。
    /// 第一次就失败（没有效果、需要玩家操作……）原样返回；之后的失败只是提前结束。
    /// </summary>
    private StepResult UseTool(object tool, object item, List<object> tools, int times)
    {
        var all = new StepResult { Ok = true };
        var results = new List<StepResult>();
        var spent = new HashSet<object>(); // 这一件上已经“没有效果”的工具（例如倒空了的桶）
        StepResult first = null;
        object Next() => tools.FirstOrDefault(t => !spent.Contains(_w.Key(t)) && !Equals(_w.Key(t), _w.Key(item)) && _w.Exists(t) && CanUse(t, item));
        for (int i = 0; i < times; i++)
        {
            if (i > 0 && !_w.Exists(item)) break;
            if (tool == null || !_w.Exists(tool) || spent.Contains(_w.Key(tool)) || !CanUse(tool, item)) tool = Next();
            if (tool == null) break;
            var res = ApplyEither(tool, item);
            if (!res.Ok)
            {
                // 没有效果：换一件工具再试（这件次数不算）；别的失败就停
                if (res.Code == FailCode.NoEffect) { first ??= res; spent.Add(_w.Key(tool)); tool = null; i--; continue; }
                if (results.Count == 0) return res;
                break;
            }
            results.Add(res);
            all.Effects.AddRange(res.Effects);
        }
        if (results.Count == 0) return first ?? StepResult.Fail(L.T("report.fail.no_tool"), FailCode.NotAllowed);
        var merged = MergeResults(results);
        all.Text = merged.Text;
        all.Label = merged.Label;
        all.Changes = merged.Changes;
        all.OtherParts = merged.OtherParts;
        return all;
    }

    /// <summary>结构化合并多次作用结果。</summary>
    private static StepResult MergeResults(IReadOnlyList<StepResult> list)
    {
        if (list.Count == 0) return new StepResult { Ok = true };
        if (list.Count == 1) return list[0];
        bool hasStructured = list.Any(r => r.Changes.Count > 0 || !string.IsNullOrEmpty(r.Label));
        if (hasStructured)
        {
            var first = list[0];
            string label = list.FirstOrDefault(r => !string.IsNullOrEmpty(r.Label))?.Label;
            var changes = new Dictionary<string, (string old, string cur)>();
            var order = new List<string>();
            var other = new List<string>();
            foreach (var r in list)
            {
                foreach (var c in r.Changes)
                {
                    if (changes.TryGetValue(c.Name, out var prev)) changes[c.Name] = (prev.old, c.New);
                    else { changes[c.Name] = (c.Old, c.New); order.Add(c.Name); }
                }
                foreach (var o in r.OtherParts) if (!other.Contains(o)) other.Add(o);
            }
            var mergedChanges = order.Select(n => new StepChange(n, changes[n].old, changes[n].cur)).ToList();
            first.Label = label;
            first.Changes = mergedChanges;
            first.OtherParts = other;
            first.Text = Text.FormatMerged(label, mergedChanges, other);
            return first;
        }
        var plain = list[0];
        plain.Text = Text.MergeSteps(list.Select(r => r.Text));
        return plain;
    }

    /// <summary>适用条件：不满足的对象静默跳过。</summary>
    private bool Applicable(OpDef op, ActionDef act, object item, IReadOnlyList<object> targets)
    {
        if (!_w.Exists(item)) return false;
        switch (op.Mechanism)
        {
            case Mechanism.Place:
                var parent = _w.Parent(item);
                return parent == null || targets.All(b => !Equals(_w.Key(b), _w.Key(parent)));
            case Mechanism.Annotate:
                bool marked = _w.Num(item, "mark:" + act.Text) == 1;
                return op.Id == ActVerb.Mark ? !marked : marked;
        }
        return true;
    }

    // ================= 执行前提 =================

    public static bool PreHolds(EvalCtx c, WorkRule rule)
    {
        if (rule.Pre == null || rule.Pre.Count == 0) return true;
        bool One(PreCond p)
        {
            double? rhs = Eval.Expr(c, p.Right);
            switch (p.RefKind)
            {
                case PreRef.Note: return Vocab.Compare(c.Memo != null && c.Memo.TryGetValue(p.Ref ?? "", out var m) ? m : null, p.Test, rhs);
                case PreRef.Sys: return Vocab.Compare(c.Sys?.Invoke(p.Ref), p.Test, rhs);
            }
            var v = Eval.Selector(c, p.Ref);
            if (v.IsNumber) return Vocab.Compare(v.Num, p.Test, rhs);
            int n = p.Where == null ? v.Items.Count : v.Items.Count(x => Eval.Cond(c, p.Where, x));
            return p.Test switch
            {
                PreTest.NotEmpty => n > 0,
                PreTest.Empty => n == 0,
                PreTest.CountGt => Vocab.Compare(n, ">", rhs),
                PreTest.CountLt => Vocab.Compare(n, "<", rhs),
                _ => Vocab.Compare(n, p.Test, rhs),
            };
        }
        return rule.Join == "any" ? rule.Pre.Any(One) : rule.Pre.All(One);
    }
}
