using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 游戏标签的显示名。游戏没有“标签 → 名字”的表，各功能在显示时自己取本地化条目；这里按游戏起条目名的惯例去找：
///   1. 对照表：mod 自己给的名字（游戏只在整句里提到的，如热度）、读这个标签的内置属性的名字；
///   2. 同名条目：键名去掉 _TAG / _INT 等后缀、转小写，在游戏常用的命名空间里找（mech_、type_、plant_stat_……，
///      本身或加 _name），条目要是纯标签（不是说明句）；
///   3. 生成的特性：PLANT_TRAIT_production_液体 → 游戏的“产出{0}”套液体名（游戏自己就是这样拼的）；
///   4. 变体：键名里有 CURRENT / BASE / DEFAULT / MAX / MIN，去掉后的标签按 1–3 有名字时，名字 = 修饰词模板套那个名字
///      （GUN_CURRENT_FIREPOWER_TAG → 当前火力；DEFAULT_DURABILITY_TAG → 默认耐久）。
/// 都找不到返回 null（显示原始键名）。适配器接上游戏的本地化（GameText、LiquidName、BuiltinName）。
/// </summary>
public static class TagNames
{
    /// <summary>游戏本地化条目（Mechanic 表）的纯标签文字；没有、或是句子时为 null。</summary>
    public static Func<string, string> GameText = _ => null;

    /// <summary>游戏本地化里的原句（不截取），给需要套占位符的条目用（“产出{0}”）。</summary>
    public static Func<string, string> GameFormat = _ => null;

    /// <summary>液体 id 的游戏名。</summary>
    public static Func<string, string> LiquidName = _ => null;

    /// <summary>读这个标签的内置属性的名字（DURABILITY_TAG → 耐久）。</summary>
    public static Func<string, string> BuiltinName = _ => null;

    /// <summary>游戏常用的命名空间（条目名前缀）。</summary>
    private static readonly string[] Namespaces = { "", "mech_", "type_", "item_feature_category_", "plant_stat_", "plant_trait_" };

    /// <summary>这些结尾的条目是说明句，不当名字。</summary>
    private static readonly string[] NotNames = { "_tooltip", "_desc", "_hint", "_flavor" };

    private static readonly string[] Qualifiers = { "CURRENT", "BASE", "DEFAULT", "MAX", "MIN" };

    private const string ProductionPrefix = "PLANT_TRAIT_production_";

    private static readonly Dictionary<string, string> Cache = new();

    public static void ClearCache() => Cache.Clear();

    /// <summary>标签的显示名；找不到为 null。</summary>
    public static string Name(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (Cache.TryGetValue(key, out var hit)) return hit;
        return Cache[key] = Direct(key) ?? Variant(key);
    }

    private static string Direct(string key) => Own(key) ?? BuiltinName(key) ?? Game(key) ?? Production(key);

    private static string Own(string key)
    {
        string loc = "tagname." + key.ToLowerInvariant();
        return L.Has(loc) ? L.T(loc) : null;
    }

    private static string Game(string key)
    {
        string b = GameTags.Stem(key).ToLowerInvariant();
        foreach (var ns in Namespaces)
            foreach (var suffix in new[] { "", "_name" })
            {
                string entry = ns + b + suffix;
                if (NotNames.Any(entry.EndsWith)) continue;
                if (GameText(entry) is { Length: > 0 } t) return t;
            }
        return null;
    }

    private static string Production(string key)
    {
        if (!key.StartsWith(ProductionPrefix, StringComparison.Ordinal)) return null;
        string liquid = LiquidName(key[ProductionPrefix.Length..]);
        string format = GameFormat("mech_dna_production_name");
        return liquid != null && format != null && format.Contains("{0}") ? format.Replace("{0}", liquid) : null;
    }

    private static string Variant(string key)
    {
        var parts = key.Split('_');
        foreach (var q in Qualifiers)
        {
            if (!parts.Any(p => p.Equals(q, StringComparison.OrdinalIgnoreCase))) continue;
            string plain = string.Join("_", parts.Where(p => !p.Equals(q, StringComparison.OrdinalIgnoreCase)));
            if (plain.Length == 0 || plain == key) continue;
            if (Direct(plain) is { } name) return L.T("tagname.qualifier." + q.ToLowerInvariant(), name);
        }
        return null;
    }
}
