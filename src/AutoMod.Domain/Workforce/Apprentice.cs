using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Workforce;

/// <summary>物品领域（熟练度向量的一组维度）。不再是学徒的技能类别。</summary>
public static class Category
{
    public const string Husbandry = "husbandry";
    public const string Water = "water";
    public const string Power = "power";
    public const string Maintenance = "maintenance";
    public const string Misc = "misc";

    public static readonly string[] All = { Husbandry, Water, Power, Maintenance, Misc };

    public static string DisplayName(string c) => L.T("skill." + c);
}

/// <summary>学徒（日结雇员）。</summary>
public class Apprentice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public string Name { get; set; } = "";
    /// <summary>综合等级（1–10）与当前等级的经验。</summary>
    public int Level { get; set; } = 1;
    public int Xp { get; set; }

    /// <summary>经验构成：12 维，分操作、物品领域两组（Proficiency）。招进来是白板。</summary>
    public Dictionary<string, double> Proficiency { get; set; } = AutoMod.Domain.Workforce.Proficiency.Blank();


    /// <summary>每天的 AP：发薪后的晚班和第二天的早班共用这一份（整数）。</summary>
    public int MaxAp { get; set; } = 25;
    public int Wage { get; set; } = 20;

    /// <summary>执行的流程和时间，按顺序串行执行；为空 = 未指派。</summary>
    public List<Duty> Duties { get; set; } = new();

    /// <summary>当天（晚班 + 第二天早班）已用的 AP。按熟练度系数累计成小数，显示时向上取整；发薪时清零。</summary>
    public double ApUsed { get; set; }

    /// <summary>当天还剩的 AP（整数）。</summary>
    [Newtonsoft.Json.JsonIgnore] public int ApLeft => Math.Max(0, MaxAp - ApMath.Ceil(ApUsed));

    /// <summary>对讲机里是否勾选上班（= 下一个班次上班并在入睡时付当天工资）。玩家的选择会一直保留。</summary>
    public bool Paid { get; set; } = true;

    /// <summary>本班次是否上班：在“结束营业”时根据勾选和现金决定，班次结束后清除。</summary>
    public bool OnDuty { get; set; }

    /// <summary>上次因为付不起而罢工（只用于显示）。</summary>
    public bool Striking { get; set; }

    public int HiredDay { get; set; }

    /// <summary>已付费、等着在下一个上岗班次开始时阅读的培训材料（不为空 = 有一份）。一次只能有一份。</summary>
    public string Training { get; set; }

    public string ServiceId => "AUTOMOD_APP_" + Id;

    /// <summary>某个班次要执行的流程（按顺序）。</summary>
    public IEnumerable<Duty> DutiesIn(string shift) => Duties.Where(d => d.Shift == shift);

    /// <summary>“＋ 添加流程”新加的一项：默认选还没排给这名学徒的第一个流程（都排过了就选第一个），早班。</summary>
    public Duty NewDuty(IReadOnlyList<string> scheduleIds)
    {
        string id = scheduleIds.FirstOrDefault(s => Duties.All(d => d.ScheduleId != s)) ?? scheduleIds.FirstOrDefault();
        return new Duty { ScheduleId = id, Shift = Workforce.Shift.Morning };
    }
}

/// <summary>学徒的一项工作：执行哪个流程、在哪个班次。</summary>
public class Duty
{
    public string ScheduleId { get; set; }
    public string Shift { get; set; } = AutoMod.Domain.Workforce.Shift.Morning;
}

/// <summary>AP 显示为整数：累计的小数 AP 向上取整（不累计误差）。</summary>
public static class ApMath
{
    public static int Ceil(double used) => (int)Math.Ceiling(used - 1e-9);
}

/// <summary>等级与经验。</summary>
public static class SkillMath
{
    public const int MaxLevel = 10;

    /// <summary>新学徒每天的 AP（固定）。</summary>
    public const int StartAp = 25;

    /// <summary>每升一级，每天的 AP 上限加这么多：Lv5 45，Lv10 70。</summary>
    public const int ApPerLevel = 5;

    /// <summary>升级所需经验：满负荷约 6 周到 Lv5，Lv10 要大半年。</summary>
    public static int XpToNext(int level) => 80 + level * level * 40;

    /// <summary>处理一件物品得的经验：单价 × (0.5 + 0.5 × 有效熟练度/100)，熟练的活长得快。</summary>
    public static double XpGain(double baseCost, int effective) => baseCost <= 0 ? 0 : baseCost * (0.5 + 0.5 * effective / 100.0);

    /// <returns>升了几级</returns>
    public static int AddXp(Apprentice a, int xp)
    {
        a.Xp += xp;
        int gained = 0;
        while (a.Level < MaxLevel && a.Xp >= XpToNext(a.Level))
        {
            a.Xp -= XpToNext(a.Level);
            a.Level++;
            gained++;
        }
        if (a.Level >= MaxLevel) a.Xp = 0;
        if (gained > 0)
        {
            a.MaxAp += ApPerLevel * gained;        // 升级：每天的 AP 多 5
            a.Wage = ApprenticeFactory.WageOf(a); // 日薪和能力挂钩：升级就涨薪
        }
        return gained;
    }
}

public static class ApprenticeFactory
{
    private static readonly string[] Surnames = { "王", "李", "张", "刘", "陈", "杨", "赵", "黄", "周", "吴", "林", "罗" };
    private static readonly string[] Given = { "小米", "阿洛", "铁头", "二狗", "阿珍", "胖虎", "小灰", "老六", "阿飞", "豆子", "石头", "阿梅" };

    /// <summary>新学徒：白板通用学徒，固定 Lv1、每天 25 AP，只有名字不同。</summary>
    public static Apprentice Generate(Random rng, int day = 0)
    {
        string name;
        if (L.Has("names.apprentice"))
        {
            var pool = L.T("names.apprentice").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            name = pool.Length > 0 ? pool[rng.Next(pool.Length)] : Surnames[rng.Next(Surnames.Length)] + Given[rng.Next(Given.Length)];
        }
        else
        {
            name = Surnames[rng.Next(Surnames.Length)] + Given[rng.Next(Given.Length)];
        }
        var a = new Apprentice
        {
            Name = name,
            Level = 1,
            MaxAp = SkillMath.StartAp,
            HiredDay = day,
        };
        a.Wage = WageOf(a);
        return a;
    }

    /// <summary>
    /// 日薪：只看等级，30 + 10 × 等级，升级时重算；熟练度不影响工资。
    /// Lv1 40，Lv5 80，Lv10 130。
    /// </summary>
    public static int WageOf(Apprentice a) => 30 + a.Level * 10;
}
