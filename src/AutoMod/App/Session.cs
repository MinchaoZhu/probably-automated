using System.Collections.Generic;
using System.Linq;
using AutoMod.Adapter;
using AutoMod.Domain.Rules;

namespace AutoMod.App;

/// <summary>当前游戏会话：排班数据、游戏世界（短时缓存）、结构检查缓存、容器名称。界面各功能共用。</summary>
public static class Session
{
    public static RulesData D => Automation.State.Rules;

    private static GameWorld _world;
    private static float _worldAt = -10;

    /// <summary>游戏世界（2 秒内复用），用于容器名称、绑定检查。</summary>
    public static GameWorld World
    {
        get
        {
            float now = UnityEngine.Time.unscaledTime;
            if (_world == null || now - _worldAt > 2f) { _world = new GameWorld(); _worldAt = now; }
            return _world;
        }
    }

    public static void InvalidateWorld() => _worldAt = -10;

    public static string ContainerName(int uid)
    {
        var it = World.ByUid(uid);
        return it == null ? Domain.L.T("session.container_missing", uid) : World.DistinctName(it);
    }

    /// <summary>把适配器的名称来源接到领域的文字生成上（插件启动时调用）。</summary>
    public static void Init()
    {
        AutoMod.Domain.Rules.Text.ExtraPropName = p => Names.Prop(p);
        Catalog.KindName = k => Names.Kind(k);
        Catalog.FamilyName = TraitBindings.FamilyName;
        Catalog.PropNameHook = GameName.Prop;
        Catalog.GradeShortNameHook = Quality.GradeShortName;
        Catalog.GradeNames = prop => Quality.Grades(prop) is { Count: > 0 } g ? g : null;
        SlotNames.NamedKinds = name => World.Named(name).Select(x => ((GameItem)x).identifier);
        Catalog.ItemTypeName = t => { try { return TypeHelper.GetTypeDisplayName(t); } catch { return t; } };
    }

    // ---------- 结构检查 ----------

    public static List<string> RuleErrors(Schedule s, WorkRule r) => Check.RuleErrors(D, s, r, World);

    public static int ScheduleErrors(Schedule s) => s.Rules.Count(r => r.Enabled && RuleErrors(s, r).Count > 0);

    public static List<string> RuleWarnings(WorkRule r) => Check.RuleWarnings(D, r, World);

    /// <summary>
    /// 排班此刻能不能真正做事（卡片状态用）：提醒、店里还没有的设备名、此刻为空的机器 / 容器筛选器（例如店里没有养殖系统）。
    /// </summary>
    public static List<string> ScheduleNotes(Schedule s)
    {
        var notes = new List<string>();
        var rules = s.Rules.Where(r => r.Enabled).ToList();
        notes.AddRange(rules.SelectMany(RuleWarnings));
        notes.AddRange(Check.MissingNames(D, rules.SelectMany(Check.RuleRefs), World).Select(n => Domain.L.T("session.no_device", n)));
        try
        {
            var ctx = new EvalCtx(World, D);
            foreach (var id in Check.Closure(D, rules.SelectMany(Check.RuleRefs)))
                if (D.Selectors.FirstOrDefault(x => x.Id == id) is { } sel && sel.From.Contains(Places.Shop) && Eval.YieldsBoxes(sel, World) && Eval.List(ctx, id).Count == 0)
                    notes.Add(Domain.L.T("session.no_selector_items", sel.Name));
        }
        catch { }
        return notes.Distinct().ToList();
    }

    /// <summary>被指派的排班里有没有结构错误（状态灯用，5 秒缓存）。</summary>
    public static bool AnyAssignedErrors()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now - _errAt < 5f) return _errCached;
        _errAt = now;
        try
        {
            var ids = Automation.State.Apprentices.Where(a => a.Paid).SelectMany(a => a.Duties).Select(d => d.ScheduleId).ToHashSet();
            _errCached = D.Schedules.Where(s => ids.Contains(s.Id)).Any(s => ScheduleErrors(s) > 0);
        }
        catch { _errCached = false; }
        return _errCached;
    }

    private static float _errAt = -10;
    private static bool _errCached;

    public static void Changed() => _errAt = -10;


    /// <summary>学徒对某个流程的熟练度：熟练度、有效熟练度（含等级保底）、AP 系数；流程不存在时为空。</summary>
    public static (int score, int effective, double mult)? ProficiencyOf(AutoMod.Domain.Workforce.Apprentice a, string scheduleId)
    {
        var s = D.Schedules.FirstOrDefault(x => x.Id == scheduleId);
        if (s == null) return null;
        var profile = AutoMod.Domain.Workforce.Proficiency.ProfileOf(D, s, World);
        int score = AutoMod.Domain.Workforce.Proficiency.Score(a.Proficiency, profile);
        int eff = AutoMod.Domain.Workforce.Proficiency.Effective(a, profile);
        return (score, eff, AutoMod.Domain.Workforce.Proficiency.CostMult(eff));
    }

    /// <summary>“自动换电 熟练度 82/100 · AP ×1.22”；有效熟练度来自等级保底时注明。</summary>
    public static string ProficiencyText(AutoMod.Domain.Workforce.Apprentice a, string scheduleId)
    {
        var p = ProficiencyOf(a, scheduleId);
        if (p == null) return Domain.L.T("session.unassigned_schedule");
        var (score, eff, mult) = p.Value;
        string name = D.Schedules.First(x => x.Id == scheduleId).Name;
        string floor = eff > score ? Domain.L.T("session.prof_floor", eff) : "";
        return Domain.L.T("session.prof_text", name, score, floor, mult.ToString("0.00"));
    }
}
