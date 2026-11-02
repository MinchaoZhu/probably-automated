using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;

namespace AutoMod.Adapter;

/// <summary>
/// 游戏的品质特征（ItemFeature + ItemConditionList.conditionLookup）：水质、器械状况、化学纯度、酒品质、真伪……
/// 每个品质类别有一份从好到差排好的等级表；物品上的特征记着实际等级、标签上的等级、是否验过。
/// 按类别自动生成属性：q:（实际等级序号，0 最好）、qt:（实际等级名）、ql:（标签上的等级序号）、qok:（标签属实）、qd:（已验过）。
/// 游戏以后加的品质类别也自动可用（名字没收录的显示原始键名）。
/// </summary>
public static class Quality
{
    private static readonly Dictionary<string, (string table, string key)> CategoryLocKeys = new()
    {
        ["CATEGORY_OPEN_BOX"] = ("Mechanic", "item_feature_category_open_box"),
        ["CATEGORY_RUSTED"] = ("Mechanic", "item_feature_category_rusted"),
        ["CATEGORY_EXPIRED"] = ("Mechanic", "item_feature_category_expired"),
        ["CATEGORY_DENTED"] = ("Mechanic", "item_feature_category_dented"),
        ["CATEGORY_INGOT_PURITY"] = ("Mechanic", "item_feature_category_ingot_purity"),
        ["CATEGORY_WATER_PURITY"] = ("Mechanic", "item_feature_category_water_purity"),
        ["CATEGORY_FRESHNESS"] = ("Mechanic", "item_feature_category_freshness"),
        ["CATEGORY_GENUINE_CIGARETTE"] = ("Mechanic", "item_feature_category_cigarette"),
        ["CATEGORY_GENUINE_STAMP"] = ("Mechanic", "item_feature_category_stamp"),
        ["CATEGORY_GENUINE_INJECTOR"] = ("Mechanic", "item_feature_category_injector"),
        ["CATEGORY_MODULE_STUCK"] = ("Mechanic", "item_feature_category_module_stuck"),
        ["CATEGORY_FREE"] = ("Mechanic", "item_feature_category_free"),
        ["CATEGORY_CHEMICAL_PURITY"] = ("Mechanic", "item_feature_category_chemical_purity"),
        ["CATEGORY_SHABBY"] = ("Mechanic", "item_feature_category_shabby"),
        ["CATEGORY_SECOND_HAND"] = ("Mechanic", "mech_condition_second_hand"),
        ["CATEGORY_EQUIPMENT_CONDITION"] = ("UI", "ui_nego_condition"),
        ["CATEGORY_REPLICA"] = ("Mechanic", "mech_condition_replica"),
    };

    private static readonly Dictionary<string, string> ConditionLocKeys = new()
    {
        ["freeFree"] = "mech_condition_free",
        ["freeNormal"] = "mech_condition_normal",
        ["expired"] = "mech_condition_expired",
        ["non_expired"] = "mech_condition_non_expired",
        ["dented"] = "mech_condition_dented",
        ["non_dented"] = "mech_condition_hidden",
        ["open_box"] = "mech_condition_open_box",
        ["factory_sealed"] = "mech_condition_factory_sealed",
        ["rust_free"] = "mech_condition_hidden",
        ["rusted"] = "mech_condition_rusted",
        ["INGOT_PURITY_PERFECT"] = "mech_condition_perfect_purity",
        ["INGOT_PURITY_VERY_HIGH"] = "mech_condition_very_high_purity",
        ["INGOT_PURITY_HIGH"] = "mech_condition_high_purity",
        ["INGOT_PURITY_FAIR"] = "mech_condition_fair_purity",
        ["INGOT_PURITY_LOW"] = "mech_condition_low_purity",
        ["INGOT_PURITY_VERY_LOW"] = "mech_condition_very_low_purity",
        ["pure_water"] = "mech_condition_pure_water",
        ["high_quality_water"] = "mech_condition_high_quality_water",
        ["basewater"] = "mech_condition_basewater",
        ["ghostwater"] = "mech_condition_ghostwater",
        ["rustwater"] = "mech_condition_rustwater",
        ["gutterflow"] = "mech_condition_gutterflow",
        ["very_fresh"] = "mech_condition_very_fresh",
        ["fresh"] = "mech_condition_fresh",
        ["stale"] = "mech_condition_stale",
        ["spoiled"] = "mech_condition_spoiled",
        ["counterfeitCigarette"] = "mech_condition_counterfeit",
        ["genuineCigarette"] = "mech_condition_genuine",
        ["counterfeitStamp"] = "mech_condition_counterfeit",
        ["genuineStamp"] = "mech_condition_genuine",
        ["counterfeitInjector"] = "mech_condition_counterfeit",
        ["genuineInjector"] = "mech_condition_genuine",
        ["unusableInjector"] = "mech_condition_unusable",
        ["expiredInjector"] = "mech_condition_expired",
        ["module_normale"] = "mech_condition_normal",
        ["shabby"] = "mech_condition_shabby",
        ["second_hand"] = "mech_condition_second_hand",
        ["module_stuck"] = "mech_condition_stuck",
        ["equipment_condition_new"] = "mech_condition_equip_new",
        ["equipment_condition_excellent"] = "mech_condition_equip_excellent",
        ["equipment_condition_good"] = "mech_condition_equip_good",
        ["equipment_condition_fair"] = "mech_condition_equip_fair",
        ["equipment_condition_poor"] = "mech_condition_equip_poor",
        ["replica"] = "mech_condition_replica",
        ["non_replica"] = "mech_condition_functional",
        ["lab_pure"] = "mech_condition_lab_pure",
        ["pharma_grade"] = "mech_condition_pharma_grade",
        ["near_pharma"] = "mech_condition_near_pharma",
        ["street_standard"] = "mech_condition_street_standard",
        ["cut_product"] = "mech_condition_cut_product",
        ["junk_grade"] = "mech_condition_junk_grade",
        ["starch_grade"] = "mech_condition_starch_grade",
        ["wine_quality_master_vintage"] = "mech_condition_wine_master_vintage",
        ["wine_quality_reserve"] = "mech_condition_wine_reserve",
        ["wine_quality_production"] = "mech_condition_wine_production",
        ["wine_quality_homebrew"] = "mech_condition_wine_homebrew",
        ["wine_quality_bucket_brew"] = "mech_condition_wine_bucket_brew",
        ["wine_quality_undrinkable"] = "mech_condition_wine_undrinkable",
    };

    public static string ConditionDisplayName(ItemCondition cond)
    {
        if (cond == null) return "";
        if (cond.identifier != null && ConditionLocKeys.TryGetValue(cond.identifier, out var key))
        {
            if (LocBridge.TryGetGameString("Mechanic", key, out var text)) return text;
        }
        return cond.display ?? cond.identifier ?? "";
    }

    public static string CategoryName(string cat)
    {
        string modKey = "quality.cat." + cat.ToLowerInvariant();
        if (Domain.L.Has(modKey)) return Domain.L.T(modKey);

        if (CategoryLocKeys.TryGetValue(cat, out var loc))
        {
            if (LocBridge.TryGetGameString(loc.table, loc.key, out var text)) return text;
        }
        try
        {
            if (ItemFeatureList.CategoryNames.TryGetValue(cat, out var n) && !string.IsNullOrEmpty(n))
                return n;
        }
        catch { }
        return cat;
    }

    public static string GradeShortName(string prop)
    {
        if (string.IsNullOrEmpty(prop)) return null;
        if (prop.StartsWith("q:") || prop.StartsWith("ql:"))
        {
            string cat = prop[(prop.IndexOf(':') + 1)..];
            return CategoryName(cat);
        }
        return null;
    }

    /// <summary>某类别的等级表（好 → 差）；不是品质类别返回 null。</summary>
    public static List<ItemCondition> Levels(string cat) =>
        ItemConditionList.conditionLookup.TryGetValue(cat, out var l) ? l : null;

    public static ItemFeature Feature(GameItem item, string cat) =>
        item?.itemFeatures?.FirstOrDefault(f => f != null && f.category == cat);

    /// <summary>物品身上的品质类别。</summary>
    public static IEnumerable<string> Categories(GameItem item) =>
        (item?.itemFeatures ?? new List<ItemFeature>()).Where(f => f?.category != null && Levels(f.category) != null).Select(f => f.category);

    private static int Index(string cat, ItemCondition c)
    {
        var levels = Levels(cat);
        if (levels == null || c == null) return -1;
        int i = levels.FindIndex(x => x.identifier == c.identifier);
        return i;
    }

    /// <summary>品质属性的值；不是品质属性返回 null。</summary>
    public static PropVal? Read(GameItem item, string prop)
    {
        int colon = prop.IndexOf(':');
        if (colon < 0) return null;
        string kind = prop[..colon], cat = prop[(colon + 1)..];
        if (kind is not ("q" or "qt" or "ql" or "qok" or "qd")) return null;
        var f = Feature(item, cat);
        if (f == null) return PropVal.None;
        switch (kind)
        {
            case "q": { int i = Index(cat, f.realCondition); return i < 0 ? PropVal.None : PropVal.Of(i); }
            case "ql": { int i = Index(cat, f.fakeCondition); return i < 0 ? PropVal.None : PropVal.Of(i); }
            case "qt": return f.realCondition != null ? PropVal.Of(ConditionDisplayName(f.realCondition)) : PropVal.None;
            case "qok": return PropVal.Of(f.realCondition?.identifier == f.fakeCondition?.identifier ? 1 : 0);
            default: return PropVal.Of(f.IsFeatureDiscovered() ? 1 : 0);
        }
    }

    /// <summary>这些物品身上品质类别的属性（按类别：等级、等级名、标签等级、标签属实、已验过）。</summary>
    public static List<string> Props(IEnumerable<GameItem> items)
    {
        var cats = new SortedSet<string>(items.SelectMany(Categories));
        return cats.SelectMany(c => new[] { "q:" + c, "qt:" + c, "ql:" + c, "qok:" + c, "qd:" + c }).ToList();
    }

    /// <summary>等级属性（q: / ql:）的等级名，从好到差；不是等级属性返回 null。</summary>
    public static List<string> Grades(string prop)
    {
        if (prop == null || !(prop.StartsWith("q:") || prop.StartsWith("ql:"))) return null;
        var levels = Levels(prop[(prop.IndexOf(':') + 1)..]);
        return levels?.Select(ConditionDisplayName).ToList();
    }

    /// <summary>品质属性的显示名，例如“水质等级”“水质标签属实”。</summary>
    public static string PropName(string prop)
    {
        int colon = prop.IndexOf(':');
        string kind = prop[..colon], cat = prop[(colon + 1)..], n = CategoryName(cat);
        return kind switch
        {
            "q" => Domain.L.T("quality.prop.grade", n),
            "qt" => Domain.L.T("quality.prop.grade_name", n),
            "ql" => Domain.L.T("quality.prop.label_grade", n),
            "qok" => Domain.L.T("quality.prop.label_ok", n),
            _ => Domain.L.T("quality.prop.verified", n),
        };
    }
}
