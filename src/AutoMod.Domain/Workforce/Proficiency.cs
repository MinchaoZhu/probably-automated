using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;

namespace AutoMod.Domain.Workforce;

/// <summary>
/// 工作熟练度：排班有一个“工作构成”向量，学徒有一个“经验构成”向量，两者越像熟练度越高。
/// 向量分两组，各自归一化：操作（移动、更换、使用）+ 物品领域（处理哪类东西）。向量用字典存，缺的维度按 0。
/// </summary>
public static class Proficiency
{
    public static readonly string[] OpDims = { ActVerb.Move, ActVerb.Swap, ActVerb.Use };
    public static readonly string[] DomainDims = Category.All;

    /// <summary>数值；balance 工具会换着试。</summary>
    public sealed class Tuning
    {
        /// <summary>等级保底 = FloorBase + FloorPerLevel × 等级（Lv1 54，Lv10 90）。</summary>
        public int FloorBase = 50, FloorPerLevel = 4;

        /// <summary>每件 AP = 单价 × Scale × 100 / 有效熟练度（等级的作用是熟练度保底和每天的 AP，不另打折）。</summary>
        public double Scale = 0.75;

        /// <summary>每上一班（干了活）向这一班的工作靠近的比例：白板做熟一个排班约两周（操作 3 维时 0.12）。</summary>
        public double Rate = 0.12;

        /// <summary>学习量至少按这么多个满负荷班算：1 = 只要干了活就按一整班学（按次数学，不按 AP）。</summary>
        public double MinShare = 1;
    }

    public static Tuning T { get; set; } = new();

    /// <summary>排班工作构成的滑动平均：保留旧值的比例。</summary>
    public const double ProfileKeep = 0.7;

    // ================= 向量 =================

    /// <summary>白板：每组平均分配。</summary>
    public static Dictionary<string, double> Blank() =>
        OpDims.ToDictionary(k => k, _ => 1.0 / OpDims.Length)
            .Concat(DomainDims.ToDictionary(k => k, _ => 1.0 / DomainDims.Length)).ToDictionary(kv => kv.Key, kv => kv.Value);

    private static double Get(IReadOnlyDictionary<string, double> v, string k) => v != null && v.TryGetValue(k, out var x) ? x : 0;

    /// <summary>两组各自归一化；某一组全是 0 时那一组按白板。</summary>
    public static Dictionary<string, double> Normalize(IReadOnlyDictionary<string, double> raw)
    {
        var r = new Dictionary<string, double>();
        foreach (var dims in new[] { OpDims, DomainDims })
        {
            double sum = dims.Sum(k => Math.Max(0, Get(raw, k)));
            foreach (var k in dims) r[k] = sum > 0 ? Math.Max(0, Get(raw, k)) / sum : 1.0 / dims.Length;
        }
        return r;
    }

    /// <summary>熟练度 0–100：100 × (1 − 两组 L1 距离之和 / 4)。</summary>
    public static int Score(IReadOnlyDictionary<string, double> p, IReadOnlyDictionary<string, double> w)
    {
        var a = Normalize(p);
        var b = Normalize(w);
        double gap = OpDims.Concat(DomainDims).Sum(k => Math.Abs(a[k] - b[k])) / 4;
        return (int)Math.Round(100 * (1 - Math.Clamp(gap, 0, 1)));
    }

    /// <summary>等级给熟练度的保底（通用能力）：Lv1 = 54，Lv10 = 90。</summary>
    public static int Floor(int level) => Math.Min(100, T.FloorBase + T.FloorPerLevel * Math.Max(1, level));

    /// <summary>有效熟练度：max(熟练度, 保底)。</summary>
    public static int Effective(Apprentice a, IReadOnlyDictionary<string, double> profile) => Math.Max(Score(a.Proficiency, profile), Floor(a.Level));

    /// <summary>AP 系数：Scale × 100 / 有效熟练度（新人 Lv1 约 1.39，做熟约 0.8，熟练度 100 时 0.75）。</summary>
    public static double CostMult(int effective) => T.Scale * 100.0 / Math.Max(1, effective);

    /// <summary>学习：p ← (1 − η)·p + η·w班。η = Rate（只要这一班干了活；MinShare 小于 1 时按 AP 比例，balance 工具对比用）。</summary>
    public static void Learn(Apprentice a, IReadOnlyDictionary<string, double> shiftWork, double apSpent)
    {
        if (apSpent <= 0 || a.MaxAp <= 0) return;
        double eta = Math.Min(1, T.Rate * Math.Max(T.MinShare, apSpent / a.MaxAp));
        var p = Normalize(a.Proficiency);
        var w = Normalize(shiftWork);
        a.Proficiency = p.Keys.ToDictionary(k => k, k => (1 - eta) * p[k] + eta * w[k]);
    }

    /// <summary>排班工作构成的滑动平均：w ← 0.7·w + 0.3·w班。</summary>
    public static Dictionary<string, double> Blend(IReadOnlyDictionary<string, double> profile, IReadOnlyDictionary<string, double> shiftWork)
    {
        if (profile == null || profile.Count == 0) return Normalize(shiftWork);
        var a = Normalize(profile);
        var b = Normalize(shiftWork);
        return a.Keys.ToDictionary(k => k, k => ProfileKeep * a[k] + (1 - ProfileKeep) * b[k]);
    }

    /// <summary>一个对 w 熟练度正好约为 x 的向量（白板和 w 按比例混合）：模拟器的虚拟学徒用。</summary>
    public static Dictionary<string, double> ForScore(IReadOnlyDictionary<string, double> w, int x)
    {
        var target = Normalize(w);
        var blank = Blank();
        double gapBlank = 1 - Score(blank, target) / 100.0;
        double lambda = gapBlank <= 0 ? 1 : Math.Clamp(1 - (1 - x / 100.0) / gapBlank, 0, 1);
        return target.Keys.ToDictionary(k => k, k => lambda * target[k] + (1 - lambda) * blank[k]);
    }

    // ================= 排班的工作构成 =================

    /// <summary>记一件物品花的 AP：操作那一维 + 物品领域那一维。</summary>
    public static void Add(Dictionary<string, double> work, string op, string domain, double ap)
    {
        if (ap <= 0) return;
        if (OpDims.Contains(op)) work[op] = work.GetValueOrDefault(op) + ap;
        if (DomainDims.Contains(domain)) work[domain] = work.GetValueOrDefault(domain) + ap;
    }

    /// <summary>排班的工作构成：执行过就用记下的；没执行过按定义估一个（每个动作按单价记到操作维，按对象列表的类型记到领域维）。</summary>
    public static Dictionary<string, double> ProfileOf(RulesData d, Schedule s, IWorld w = null)
    {
        if (s.Profile != null && s.Profile.Count > 0) return Normalize(s.Profile);
        var work = new Dictionary<string, double>();
        foreach (var r in s.Rules.Where(r => r.Enabled))
            foreach (var a in r.Actions)
            {
                var op = Catalog.Op(a.Verb);
                if (op.Cost <= 0) continue;
                string kind = Defs.Selector(d, a.List)?.Kind;
                Add(work, op.Dim ?? op.Id, op.Skill ?? DomainOfKind(kind, w), op.Cost);
            }
        return Normalize(work);
    }

    /// <summary>挑选器类型对应的物品领域（类别的技能类别；种类按见过的物品；认不出是杂务）。</summary>
    public static string DomainOfKind(string kind, IWorld w = null)
    {
        if (string.IsNullOrEmpty(kind) || kind == Catalog.Any || kind == Catalog.Self) return Category.Misc;
        foreach (var part in kind.Split('|'))
        {
            var traits = Catalog.IsTrait(part) ? new[] { part } : (w?.KindTraits(part) ?? Array.Empty<string>()).ToArray();
            var all = Catalog.Closure(traits);
            var skill = Catalog.Traits.FirstOrDefault(t => t.Skill != null && all.Contains(t.Id))?.Skill;
            if (skill != null) return skill;
        }
        return Category.Misc;
    }
}
