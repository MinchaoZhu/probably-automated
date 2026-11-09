using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;

namespace AutoMod.Adapter;

/// <summary>类别绑定表：从游戏事实（标签、商品类型、原型 id、隔间）推导领域类别，并记录种类档案。</summary>
public static class TraitBindings
{
    private static bool Tag(GameItem it, string key) => GameApi.Tag(it, key) != null;
    private static bool Type(GameItem it, string type) { try { return it.IsGameItemType(type); } catch { return false; } }
    private static bool HasCompartment(GameItem it) => GameApi.BatterySlot(it) != null || ItemFacts.Inventories(it).Count > 0;

    /// <summary>每行：类别 ← 判断。只用游戏的标签、商品类型、隔间，不按原型 id 列名单。</summary>
    private static readonly (string trait, Func<GameItem, bool> test)[] Bindings =
    {
        (Catalog.Container, HasCompartment),
        ("storage", it => Type(it, "STORAGE")),
        ("cage", it => Tag(it, "CAGE_TAG")),
        ("machine", it => Type(it, "MACHINE") && HasCompartment(it)),
        ("animal", it => Tag(it, "ANIMAL_ITEM_TAG")),
        ("fish", it => Tag(it, "FISH_ITEM_TAG") || Tag(it, "FISH_GROUP_TAG")), // 鱼苗、鱼卵是成组的
        ("durable", it => Tag(it, "DURABILITY_TAG") && Tag(it, "MAX_DURABILITY_TAG")),
        ("food", it => Tag(it, "CALORIE_VALUE_TAG")),
        ("seed", it => Tag(it, "SEED_ITEM_TAG")),
        ("gun", it => Tag(it, "GUN_FIREPOWER_TAG")),
        ("meat", it => Tag(it, "MEAT_TYPE_TAG")),
        ("battery", it => Tag(it, "power_source_item")),
        ("selfCharging", it => Tag(it, "BREEDER_POWER_SOURCE_ITEM_TAG")),
        ("liquid", it => Tag(it, "LIQUID_CONTAINER_TAG")),
        ("filterHolder", it => Tag(it, "FILTER_CONTAINER_TAG")),
        ("filter", it => Type(it, "FILTER")),
        ("blade", it => Tag(it, "BLADE_ITEM_TAG")),
    };

    /// <summary>已经绑定成具名类别的家族标记（动物、鱼、种子、刀具、自充电电池）。</summary>
    private static readonly HashSet<string> BoundFamilies = new() { "ANIMAL", "FISH", "SEED", "BLADE", "BREEDER_POWER_SOURCE" };

    public static string FamilyName(string family)
    {
        string key = "trait.family." + family.ToLowerInvariant();
        if (Domain.L.Has(key)) return Domain.L.T(key);
        if (family == "CONTRABAND")
        {
            if (LocBridge.TryGetGameString("Mechanic", "type_contraband", out var text)) return text;
        }
        return family + "_ITEM_TAG";
    }

    /// <summary>种类档案：见过的种类 → 类别（本次游戏会话内）。</summary>
    private static readonly Dictionary<string, HashSet<string>> Profiles = new();

    /// <summary>物品的直接类别：绑定表 + 游戏商品类型（itemType:*）。</summary>
    public static HashSet<string> TraitsOf(GameItem it)
    {
        var set = new HashSet<string>();
        try
        {
            foreach (var (trait, test) in Bindings)
                try { if (test(it)) set.Add(trait); } catch { }
            // 游戏的物品家族标记（X_ITEM_TAG）自动成类别 family:X；上面已经单独绑定的家族不重复
            var tags = it.state?.dict;
            if (tags != null)
                foreach (var kv in tags)
                {
                    var m = kv.Key.EndsWith("_ITEM_TAG", StringComparison.Ordinal) && kv.Value.valueEnabled ? kv.Key[..^"_ITEM_TAG".Length] : null;
                    if (m != null && !BoundFamilies.Contains(m)) set.Add(Catalog.FamilyPrefix + m);
                }
            var types = it.itemTypes;
            for (int i = 0; types != null && i < types.Count; i++) set.Add(Catalog.ItemTypePrefix + types[i]);
            if (it.identifier != null) Profiles[it.identifier] = set;
        }
        catch { }
        return set;
    }

    /// <summary>物品的商品类型和生效的标签名，大类按它判定。</summary>
    public static HashSet<string> Facts(GameItem it)
    {
        var set = new HashSet<string>();
        if (it.itemTypes != null) set.UnionWith(it.itemTypes);
        if (it.state?.dict != null)
            foreach (var kv in it.state.dict)
                if (kv.Value.valueEnabled) set.Add(kv.Key);
        return set;
    }

    /// <summary>显示用主类别（cage / charger / storage……），不是容器为空。</summary>
    public static string PrimaryTrait(GameItem it)
    {
        var all = Catalog.Closure(TraitsOf(it));
        return Catalog.Traits.FirstOrDefault(t => all.Contains(t.Id))?.Id;
    }

    public static bool IsContainer(GameItem it) => Catalog.Closure(TraitsOf(it)).Contains(Catalog.Container);

    /// <summary>种类的类别：见过就用档案；没见过就看店里一件这种物品，店里没有再看原型（只在店里能建原型）。</summary>
    public static IReadOnlyCollection<string> KindTraits(string kind)
    {
        if (kind == null) return Array.Empty<string>();
        if (Profiles.TryGetValue(kind, out var t)) return t;
        if (!GameApi.InStore) return Array.Empty<string>();
        try
        {
            var real = GameApi.CollectAll().FirstOrDefault(i => i.identifier == kind);
            if (real != null) return TraitsOf(real);
            if (!ItemFacts.DirectoryOf.ContainsKey(kind)) return Profiles[kind] = new HashSet<string>();
            var proto = ItemFacts.CreatePrototype(kind);
            if (proto == null) return Profiles[kind] = new HashSet<string>();
            try { return TraitsOf(proto); }
            finally { try { proto.Destroy(); } catch { } }
        }
        catch { return Array.Empty<string>(); }
    }
}
