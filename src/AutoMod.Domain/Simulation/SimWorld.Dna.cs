using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;

namespace AutoMod.Domain.Simulation;

/// <summary>植物特性的规则数据（从游戏的特性表、植物表导入；单元测试自己填）。</summary>
public class SimTrait
{
    public string Id = "";
    public int Complexity;

    /// <summary>每级额外加的复杂度（简单基因组 −4、复杂基因组 +4）。</summary>
    public int OffsetPerRank;

    /// <summary>最高等级；不限 = int.MaxValue。</summary>
    public int MaxRank = 1;

    /// <summary>锁定的特性（基因组）：不能提取、不能写入。</summary>
    public bool Locked;

    public bool Negative;

    /// <summary>互斥组：种子上已有同组的别的特性时写不进去。</summary>
    public string Exclusive;

    public HashSet<string> Incompatible = new();

    /// <summary>对这些作物没有效果（写不进去）。</summary>
    public HashSet<string> NoEffectOn = new();

    /// <summary>要种子已经产出某种液体才有效（效力强 / 效力弱）。</summary>
    public bool NeedsLiquid;

    /// <summary>这个特性让作物产出液体（产出类）。</summary>
    public bool Produces;
}

public class SimGenetics
{
    /// <summary>游戏特性表的顺序（随机提取按这个顺序列候选，和游戏一样）。</summary>
    public readonly List<SimTrait> Traits = new();

    /// <summary>植物 id → 先天特性等级。</summary>
    public readonly Dictionary<string, Dictionary<string, int>> Innate = new();

    public SimTrait Get(string id) => id == null ? null : Traits.FirstOrDefault(t => t.Id == id);

    public int InnateRank(string plant, string trait) =>
        plant != null && Innate.TryGetValue(plant, out var d) && d.TryGetValue(trait, out var r) ? r : 0;
}

/// <summary>
/// DNA 编辑器的夜间模拟：照游戏 MachineDnaManipulator 每晚一个周期的规则——就绪时按格子里的东西计划操作
/// （特性盘 → 写入；空白盘 → 提取；写过的盘 → 补提取），有电就开工；每晚进度加机器速度，够了按成功率掷一次：
/// 写入成功等级 +盘的等级、盘的次数 −1（用完消失）；提取成功写盘或加次数；无论成败热度 +2+|复杂度|；
/// 没有稳定模组时提取会销毁原料种子。成功率 = 95 − 5 ×（复杂度 + 热度）+ 机器品质加成，限制在 5–100。
/// 用到的游戏数据：特性规则（Genetics，导入时从游戏读）、种子的特性等级和热度、盘的特性和次数、
/// 机器速度和品质加成、模组（精准 / 稳定 / 正面 / 负面筛选）、自动弹出开关。
/// </summary>
public partial class SimWorld
{
    public SimGenetics Genetics = new();

    public const string TraitTagPrefix = "tag:PLANT_TRAIT_";
    public const string HeatTag = "tag:GENETIC_HEAT_INT";
    public const string DiskTraitTag = "str:DNA_DISK_TRAIT_STRING";
    public const string DiskUsesTag = "tag:DNA_DISK_USES_INT";
    public const string DiskRankTag = "tag:DNA_DISK_RANK_INT";
    public const string ModifiedSeedType = Catalog.ItemTypePrefix + "MODIFIED_SEED";
    private const string Slot = SlotNames.Prefix;

    public static bool IsDnaEditor(SimItem it) => it != null && (it.N("has:DNA_MANIPULATOR_TAG") == 1 || it.Kind == "plant_dna_manipulator");
    public static bool IsDisk(SimItem it) => it != null && (it.N("has:DNA_DISK_TAG") == 1 || it.Kind == "plant_dna_disk");
    private static string DiskTrait(SimItem disk) => IsDisk(disk) && disk.Str.TryGetValue(DiskTraitTag, out var t) && !string.IsNullOrEmpty(t) ? t : null;
    private static bool Blank(SimItem disk) => IsDisk(disk) && DiskTrait(disk) == null;
    private static bool Written(SimItem disk) => DiskTrait(disk) != null;

    public static int Rank(SimItem seed, string trait) => seed != null && seed.Num.TryGetValue(TraitTagPrefix + trait, out var r) ? (int)r : 0;

    private static string PlantOf(SimItem seed) =>
        seed.Str.TryGetValue("plantId", out var p) ? p : seed.Str.TryGetValue("str:PLANT_ID_STRING", out var q) ? q : seed.Kind;

    private void SetRank(SimItem seed, string trait, int rank)
    {
        if (rank <= 0) seed.Num.Remove(TraitTagPrefix + trait);
        else seed.Num[TraitTagPrefix + trait] = rank;
        // 和游戏一样：特性和先天不同的种子算“改良种子”
        string plant = PlantOf(seed);
        bool modified = Genetics.Traits.Any(t => Rank(seed, t.Id) != Genetics.InnateRank(plant, t.Id));
        seed.TraitsOverride ??= new HashSet<string>(TraitsOf(seed));
        if (modified) seed.TraitsOverride.Add(ModifiedSeedType); else seed.TraitsOverride.Remove(ModifiedSeedType);
    }

    private IEnumerable<SimTrait> TraitsOn(SimItem seed) => Genetics.Traits.Where(t => Rank(seed, t.Id) > 0);

    public int Complexity(SimItem seed)
    {
        string plant = PlantOf(seed);
        int c = 0;
        foreach (var t in Genetics.Traits)
        {
            int rank = Rank(seed, t.Id), diff = rank - Genetics.InnateRank(plant, t.Id);
            c += diff < 0 ? Math.Abs(t.Complexity) * -diff : t.Complexity * diff;
            c += t.OffsetPerRank * rank;
        }
        return c;
    }

    private static int Heat(SimItem seed) => (int)seed.N(HeatTag);

    private static void AddHeat(SimItem seed, SimTrait t) => seed.Num[HeatTag] = Heat(seed) + 2 + (t == null ? 0 : Math.Abs(t.Complexity));

    private bool CanApply(SimItem seed, SimTrait t)
    {
        if (seed == null || t == null || t.Locked) return false;
        if (t.NoEffectOn.Contains(PlantOf(seed))) return false;
        if (t.NeedsLiquid && !TraitsOn(seed).Any(x => x.Produces)) return false;
        if (TraitsOn(seed).Any(x => x.Id != t.Id && (x.Incompatible.Contains(t.Id) || t.Incompatible.Contains(x.Id)))) return false;
        if (Rank(seed, t.Id) >= t.MaxRank) return false;
        return !TraitsOn(seed).Any(x => x.Id != t.Id && !string.IsNullOrEmpty(t.Exclusive) && t.Exclusive == x.Exclusive);
    }

    private static SimItem InSlot(SimItem m, string role) => m.Children.FirstOrDefault(c => !c.InBatterySlot && c.InSlot == Slot + role);
    private static IEnumerable<SimItem> Modules(SimItem m) => m.Children.Where(c => c.InSlot == Slot + "module");
    private static bool HasModule(SimItem m, string kind) => Modules(m).Any(x => x.Kind == kind);

    private static string Precision(SimItem m) =>
        Modules(m).Where(x => x.Kind == "dna_manipulator_module_precision")
            .Select(x => x.Str.TryGetValue("str:DNA_PRECISION_TRAIT_STRING", out var t) ? t : null).FirstOrDefault(t => !string.IsNullOrEmpty(t));

    private Func<SimTrait, bool> ExtractionFilter(SimItem m)
    {
        if (Precision(m) is { } p) return t => t.Id == p;
        bool pos = HasModule(m, "dna_manipulator_module_positive_filter"), neg = HasModule(m, "dna_manipulator_module_negative_filter");
        if (pos && neg) return _ => false;
        if (pos) return t => !t.Negative;
        if (neg) return t => t.Negative;
        return _ => true;
    }

    private List<SimTrait> Extractable(SimItem seed, SimItem m) => TraitsOn(seed).Where(t => !t.Locked && ExtractionFilter(m)(t)).ToList();

    /// <summary>就绪的编辑器此刻会做什么：(操作, 特性, 是否被挡住)；什么都不做为 null。</summary>
    private (string op, SimTrait trait, bool blocked)? Plan(SimItem m)
    {
        var seed = InSlot(m, "seed");
        if (seed == null) return null;
        var inDisk = InSlot(m, "input_disk");
        var outDisk = InSlot(m, "output_disk");
        if (Written(inDisk))
        {
            var t = Genetics.Get(DiskTrait(inDisk));
            return ("apply", t, !CanApply(seed, t));
        }
        if (Blank(outDisk))
            return ("extract", Precision(m) is { } p ? Genetics.Get(p) : null, Extractable(seed, m).Count == 0);
        if (Written(outDisk))
        {
            var t = Genetics.Get(DiskTrait(outDisk));
            return ("extract", t, t == null || t.Locked || Rank(seed, t.Id) <= 0);
        }
        return null;
    }

    private static void Lock(SimItem m, bool on)
    {
        foreach (var role in new[] { "seed", "input_disk", "output_disk" })
            if (InSlot(m, role) is { } x) { if (on) x.Num["locked"] = 1; else x.Num.Remove("locked"); }
    }

    private void DnaNight(SimItem m)
    {
        if (m.N("working") != 1)
        {
            var plan = Plan(m);
            if (plan is not { blocked: false } p || !PowerOk(m)) return;
            var seed = InSlot(m, "seed");
            int eff = Math.Max(0, Complexity(seed) + Heat(seed));
            m.Num["working"] = 1;
            m.Num["progress"] = 0;
            m.Num["progressMax"] = 20 + 4 * eff;
            m.Num["tag:DNA_OPERATION_SUCCESS_INT"] = Math.Clamp(95 - 5 * eff + (int)m.N("dnaQuality"), 5, 100);
            m.Str["str:DNA_OPERATION_STRING"] = p.op;
            m.Str["str:DNA_OPERATION_TRAIT_STRING"] = p.trait?.Id ?? "";
            Lock(m, true);
        }
        else if (!PowerOk(m)) return;
        m.Num["progress"] = m.N("progress") + m.N("speed", 10);
        if (m.N("progress") >= m.N("progressMax")) DnaFinish(m);
    }

    private void DnaFinish(SimItem m)
    {
        string op = m.Str.TryGetValue("str:DNA_OPERATION_STRING", out var o) ? o : "";
        bool success = _rng.Next(100) < m.N("tag:DNA_OPERATION_SUCCESS_INT", 95);
        var seed = InSlot(m, "seed");
        var inDisk = InSlot(m, "input_disk");
        var outDisk = InSlot(m, "output_disk");
        Lock(m, false);
        if (seed != null && op == "apply")
        {
            var t = Genetics.Get(m.Str.TryGetValue("str:DNA_OPERATION_TRAIT_STRING", out var id) ? id : null);
            if (t != null)
            {
                if (success && CanApply(seed, t))
                {
                    int rank = Rank(seed, t.Id) + Math.Max(1, (int)(inDisk?.N(DiskRankTag, 1) ?? 1));
                    SetRank(seed, t.Id, Math.Min(rank, t.MaxRank));
                }
                AddHeat(seed, t);
                if (success && inDisk != null)
                {
                    double left = Math.Max(1, inDisk.N(DiskUsesTag, 1)) - 1;
                    if (left <= 0) { Remove(inDisk); inDisk = null; }
                    else inDisk.Num[DiskUsesTag] = left;
                }
            }
        }
        if (seed != null && op == "extract" && outDisk != null)
        {
            bool refill = Written(outDisk);
            SimTrait t;
            if (refill) t = Genetics.Get(DiskTrait(outDisk)) is { } d && Rank(seed, d.Id) > 0 ? d : null;
            else { var c = Extractable(seed, m); t = c.Count == 0 ? null : c[_rng.Next(c.Count)]; }
            if (success && t != null)
            {
                int uses = Genetics.InnateRank(PlantOf(seed), t.Id) > 0 ? Math.Max(1, Rank(seed, t.Id)) : 1;
                if (refill) outDisk.Num[DiskUsesTag] = Math.Max(1, outDisk.N(DiskUsesTag, 1)) + uses;
                else
                {
                    outDisk.Str[DiskTraitTag] = t.Id;
                    outDisk.Num[DiskRankTag] = 1;
                    outDisk.Num[DiskUsesTag] = uses;
                }
            }
            if (HasModule(m, "dna_manipulator_module_stabilizer"))
            {
                if (success && t != null) SetRank(seed, t.Id, Rank(seed, t.Id) - 1);
                AddHeat(seed, t);
            }
            else Remove(seed);
        }
        // 自动弹出：用过的盘挪到输出格（输出格 2×2，满了就留在原处）
        if (m.N("tag:DNA_MANIPULATOR_KEEP_DISK_BOOL") != 1)
        {
            var disk = op == "apply" ? inDisk : outDisk;
            if (disk != null && disk.Parent == m && m.Children.Count(c => c.InSlot == Slot + "eject") < 4) disk.InSlot = Slot + "eject";
        }
        m.Num["working"] = 0;
        m.Num["progress"] = 0;
        m.Str["str:DNA_OPERATION_STRING"] = "";
        m.Str["str:DNA_OPERATION_TRAIT_STRING"] = "";
    }

    /// <summary>种子的特性等级和热度、数据盘的剩余次数（模拟器列店铺副本时附在名字后面）；都没有为空。名字取游戏的（TagNames）。</summary>
    public string GeneSummary(SimItem it)
    {
        var parts = new List<string>();
        foreach (var t in Genetics.Traits)
            if (Rank(it, t.Id) is var r && r > 0) parts.Add(L.T("sim.dna.rank", TagNames.Name("PLANT_TRAIT_" + t.Id) ?? t.Id, r));
        if (parts.Count > 0 && it.Num.ContainsKey(HeatTag)) parts.Add(L.T("sim.dna.rank", TagNames.Name("GENETIC_HEAT_INT") ?? "heat", Heat(it)));
        if (Written(it))
        {
            parts.Add(TagNames.Name("PLANT_TRAIT_" + DiskTrait(it)) ?? DiskTrait(it));
            parts.Add(L.T("sim.dna.rank", TagNames.Name("DNA_DISK_USES_INT") ?? "uses", Math.Max(1, (int)it.N(DiskUsesTag, 1))));
        }
        return parts.Count == 0 ? "" : L.T("text.paren", string.Join(" · ", parts));
    }

    /// <summary>水培收获时的子代种子：继承母株的全部特性，热度清零，生长从头开始。</summary>
    private SimItem Offspring(SimItem seed)
    {
        var child = new SimItem
        {
            Id = NewId(), GameUid = seed.GameUid, Kind = seed.Kind, Name = seed.Name, DeviceName = null,
            Num = new Dictionary<string, double>(seed.Num), Str = new Dictionary<string, string>(seed.Str),
            TraitsOverride = seed.TraitsOverride == null ? null : new HashSet<string>(seed.TraitsOverride),
            Buttons = seed.Buttons.ToList(), Accepts = seed.Accepts, InSlot = Slot + "output",
        };
        child.Num[HeatTag] = 0;
        child.Num["growProgress"] = 0;
        child.Num["growPct"] = 0;
        child.Num.Remove("locked");
        return child;
    }
}
