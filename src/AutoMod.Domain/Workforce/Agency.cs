using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Persistence;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;

namespace AutoMod.Domain.Workforce;

/// <summary>
/// 呼叫中介：雇用学徒、扩编，以及培训。
/// 这里只有规则和数值；扣款由界面层先调用游戏的付款接口，成功后再调用这里。
/// </summary>
public static class Agency
{
    /// <summary>雇用一名学徒的中介费（×1 时 60）。</summary>
    public static int HireFee => Scales.Fee(60);

    /// <summary>扩编费用（×1 时）：一次比一次贵 100：1→2 500、2→3 600……9→10 1300，扩满共 8100。到上限返回 0。</summary>
    public static int ExpandFee(int slots) => slots < 1 || slots >= Roster.MaxSlots ? 0 : Scales.Fee(500 + 100 * (slots - 1));

    public static bool CanHire(Roster st) => st.Apprentices.Count < st.Slots;

    /// <summary>雇用一名学徒（固定 Lv1、每天 25 AP，名字不和现有学徒重复）。名额已满时返回 null。</summary>
    public static Apprentice Hire(Roster st, Random rng, int today)
    {
        if (!CanHire(st)) return null;
        var used = new HashSet<string>(st.Apprentices.Select(a => a.Name));
        Apprentice c;
        int guard = 0;
        do c = ApprenticeFactory.Generate(rng, today);
        while (!used.Add(c.Name) && ++guard < 20);
        c.Paid = true;
        c.OnDuty = false;
        st.Apprentices.Add(c);
        return c;
    }

    // ---------- 培训 ----------

    /// <summary>培训每次占用的 AP（在当天第一个上班的班次开始时扣除）。</summary>
    public const int TrainingAp = 8;

    /// <summary>培训只能把等级练到这个等级，再往上要靠实际干活。培训不影响熟练度（熟练度只能靠干活）。</summary>
    public const int TrainingMaxLevel = 5;

    public static int TrainingFee(int level) => Scales.Fee(30 + level * 20);

    public static int TrainingXp(int level) => Math.Max(1, Scales.Xp(Math.Max(16, SkillMath.XpToNext(level) / 2)));

    public static bool CanTrain(Apprentice a) => a.Training == null && a.Level < TrainingMaxLevel;

    /// <summary>付费后登记培训（班次开始时阅读）。</summary>
    public static void BookTraining(Apprentice a) => a.Training = "level";

    /// <summary>
    /// 班次开始时阅读培训材料：扣 AP，真实执行时加经验并清掉待培训。返回报告条目（没有培训时返回 null）。
    /// </summary>
    public static ReportEntry ApplyTraining(Apprentice a, bool dryRun)
    {
        if (a.Training == null) return null;
        int xp = TrainingXp(a.Level);
        a.ApUsed += TrainingAp;
        var e = new ReportEntry
        {
            Ws = L.T("agency.training"), Rule = L.T("agency.materials"), Item = a.Name, Ok = true,
            Cost = TrainingAp, Worker = a.Name, Xp = xp,
            Text = L.T("agency.training_result", xp),
        };
        if (!dryRun)
        {
            e.LevelUps = SkillMath.AddXp(a, xp);
            a.Training = null;
        }
        return e;
    }
}
