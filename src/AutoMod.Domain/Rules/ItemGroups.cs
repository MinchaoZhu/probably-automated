using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 下拉列表里的大类。只决定列表显示哪些行，不写进筛选器，不影响筛选结果。
/// 按游戏的商品类型和标签判定，不列物品 id：其他模组和以后新增的物品用已有类型或标签时自动归类，其余归“其他”。
/// </summary>
public static class ItemGroups
{
    public const string Other = "other";

    /// <summary>从上到下取第一个符合的大类，顺序就是列表上按钮的顺序。</summary>
    private static readonly (string id, HashSet<string> facts)[] Rules =
    {
        ("machine", Facts("MACHINE")),
        ("storage", Facts("STORAGE", "SUPPLY_CRATE", "CONTAINER_TAG", "BACKPACK_TAG")),
        ("water", Facts("LIQUID_CONTAINER_TAG", "FILTER_CONTAINER_TAG", "FILTER", "WATER_PURIFICATION_SUPPLY", "WATER", "WATER_RATION")),
        ("power", Facts("power_source_item", "ENERGY_CELL")),
        ("husbandry", Facts("ANIMAL_ITEM_TAG", "FISH_ITEM_TAG", "FISH_GROUP_TAG", "MEAT_TYPE_TAG", "CAGE_TAG", "ANIMAL_FECES_TAG")),
        ("farming", Facts("SEED_ITEM_TAG", "SEED", "FARMING_SUPPLIES", "HYDROPONIC_MODULE")),
        ("weapon", Facts("WEAPON", "FIREARM", "MELEE_WEAPON", "GUN_MOD", "AMMUNITION", "MUNITION", "ARMOR", "GUN_FIREPOWER_TAG", "BLADE_ITEM_TAG", "MODDABLE")),
        ("module", Facts("MODULE", "PRINTER_MODULE", "ALARM_MODULE", "EVAPORATOR_MODULE", "PRINTER_CHIP_TAG", "NODE")),
        ("chem", Facts("MEDICAL", "MEDICAL_TOOL", "SUBSTANCE", "NARCOTIC", "POISON", "CHEMICAL_SUPPLIES", "CHEM_ITEM_TAG")),
        ("food", Facts("FOOD", "BREVAGE", "PROCESSED_FOOD", "TREAT", "ALCOHOL", "CALORIE_VALUE_TAG")),
        ("document", Facts("DOCUMENT", "ACCESS_CARD", "LOTTERY")),
        ("material", Facts("MATERIAL", "BASIC_MATERIAL", "JUNK", "MAKESHIFT")),
        ("tool", Facts("TOOL", "TOOLBOX_ITEM_TAG", "LABELER_TAG", "MAGNIFYING_GLASS", "HOPPER_INTAKE_TAG", "HOPPER_OUTLET_TAG")),
        ("goods", Facts("HOUSEHOLD_GOOD", "LUXURY_ITEM", "ART", "CONTRABAND", "MIDLINE", "LOWTECH", "SHOWCASE_SPECIAL_ITEM_TAG")),
    };

    /// <summary>类别行归到哪个大类；“耐用品”“容器”这类跨大类的类别不在表里，只在“全部”下显示。</summary>
    private static readonly Dictionary<string, string> TraitGroups = new()
    {
        ["machine"] = "machine",
        ["storage"] = "storage",
        ["liquid"] = "water",
        ["filter"] = "water",
        ["filterHolder"] = "water",
        ["battery"] = "power",
        ["selfCharging"] = "power",
        ["animal"] = "husbandry",
        ["fish"] = "husbandry",
        ["livestock"] = "husbandry",
        ["meat"] = "husbandry",
        ["cage"] = "husbandry",
        ["seed"] = "farming",
        ["gun"] = "weapon",
        ["blade"] = "weapon",
        ["food"] = "food",
    };

    private static HashSet<string> Facts(params string[] names) => new(names, StringComparer.Ordinal);

    /// <summary>显示顺序：表里的大类，最后是“其他”。</summary>
    public static IEnumerable<string> Order => Rules.Select(r => r.id).Append(Other);

    public static string Name(string group) => L.T("item_group." + group);

    /// <summary>物品的大类。</summary>
    /// <param name="facts">物品的商品类型和生效的标签。</param>
    public static string Of(ICollection<string> facts)
    {
        foreach (var (id, known) in Rules)
            foreach (var f in facts)
                if (known.Contains(f)) return id;
        return Other;
    }

    /// <summary>类型选项的大类：类别、商品类型或家族；选项是具体种类时返回 null，由调用方按物品判定。</summary>
    public static string OfTraitOption(string id)
    {
        if (id == null) return null;
        if (id.StartsWith(Catalog.ItemTypePrefix, StringComparison.Ordinal)) return Of(new[] { id[Catalog.ItemTypePrefix.Length..] });
        if (id.StartsWith(Catalog.FamilyPrefix, StringComparison.Ordinal)) return Of(new[] { id[Catalog.FamilyPrefix.Length..] + "_ITEM_TAG" });
        return TraitGroups.TryGetValue(id, out var g) ? g : null;
    }

    /// <summary>列表搜索：文字包含关键词，不区分大小写。关键词为空时全部匹配。</summary>
    public static bool Matches(string text, string query) =>
        string.IsNullOrEmpty(query) || (text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
}
