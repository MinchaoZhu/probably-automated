using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 游戏标签的通用规则（只看键名，不认识游戏）：哪些是内部标签、哪些是液体成分、哪个标签有对应的上限。
/// 适配器用它把物品身上的标签变成可选的属性："tag:键" / "str:键" / "has:键"（原始），"liq:液体"（成分 ml），"pct:键"（相对上限的 %）。
/// </summary>
public static class GameTags
{
    public const string LiquidPrefix = "CURRENT_PART_";

    /// <summary>
    /// 游戏侧动态生成的属性：原始标签 tag: / str: / has:，液体 liq:，相对上限 pct:，
    /// 品质 q:（实际等级序号，0 最好）/ qt:（实际等级名）/ ql:（标签上的等级序号）/ qok:（标签属实）/ qd:（已验过）。
    /// </summary>
    public static readonly string[] DynamicPrefixes = { "tag:", "str:", "has:", "liq:", "pct:", "q:", "qt:", "ql:", "qok:", "qd:" };

    public static bool IsDynamicProp(string id) => id != null && DynamicPrefixes.Any(id.StartsWith);

    /// <summary>值是文字的动态属性（条件里用等于 / 不等于）。</summary>
    public static bool IsTextProp(string id) => id != null && (id.StartsWith("str:") || id.StartsWith("qt:"));

    private static readonly string[] Suffixes = { "_TAG", "_INT", "_BOOL", "_STRING", "_FLOAT" };

    /// <summary>键名去掉类型后缀（_TAG / _INT……）。</summary>
    public static string Stem(string key)
    {
        foreach (var s in Suffixes)
            if (key.EndsWith(s, StringComparison.OrdinalIgnoreCase) && key.Length > s.Length) return key[..^s.Length];
        return key;
    }

    /// <summary>液体成分标签（CURRENT_PART_WATER）→ 液体 id（water）；不是返回 null。</summary>
    public static string LiquidOf(string key)
    {
        if (key == null || !key.StartsWith(LiquidPrefix, StringComparison.Ordinal) || key.Length <= LiquidPrefix.Length) return null;
        if (!Liquids.TryGetValue(key, out var liquid)) Liquids[key] = liquid = key[LiquidPrefix.Length..].ToLowerInvariant();
        return liquid;
    }

    private static readonly Dictionary<string, string> Liquids = new();

    public static string LiquidTag(string liquid) => LiquidPrefix + liquid.ToUpperInvariant();

    /// <summary>
    /// 这个标签的上限 / 目标标签（同一物品上存在的）：在键名某处插入 MAX / TARGET / REQUIRED，或把 CURRENT 换成它们。例如
    /// ANIMAL_HUNGER_TAG → ANIMAL_MAX_HUNGER_TAG、DURABILITY_TAG → MAX_DURABILITY_TAG、power_source_item_energy → power_source_item_max_energy、
    /// MACHINE_PROGRESS_CURRENT_TAG → MACHINE_PROGRESS_TARGET_TAG、GROWING_PROGRESS → REQUIRED_PROGRESS（替换首个词）。没有返回 null。
    /// </summary>
    public static string MaxOf(string key, ICollection<string> keys)
    {
        if (string.IsNullOrEmpty(key) || keys == null) return null;
        if (!MaxCandidates.TryGetValue(key, out var cands)) MaxCandidates[key] = cands = MaxCandidatesOf(key);
        foreach (var c in cands)
            if (keys.Contains(c)) return c;
        return null;
    }

    /// <summary>键名 → 可能的上限 / 目标标签名，按试的顺序（只取决于键名：复制全店时每件物品的每个标签都要问，记下来不重复拼）。</summary>
    private static readonly Dictionary<string, string[]> MaxCandidates = new();

    private static string[] MaxCandidatesOf(string key)
    {
        var parts = key.Split('_');
        bool upper = key.Any(char.IsUpper);
        var words = new[] { "MAX", "TARGET", "REQUIRED" }.Select(w => upper ? w : w.ToLowerInvariant()).ToList();
        if (parts.Any(p => words.Contains(p))) return Array.Empty<string>(); // 本身就是上限 / 目标
        string current = upper ? "CURRENT" : "current";
        var cands = new List<string>();
        foreach (var w in words)
        {
            for (int i = 0; i < parts.Length; i++) cands.Add(string.Join("_", parts.Take(i).Append(w).Concat(parts.Skip(i))));
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] == current || i == 0) cands.Add(string.Join("_", parts.Take(i).Append(w).Concat(parts.Skip(i + 1))));
        }
        return cands.Where(c => c != key).ToArray();
    }

    /// <summary>
    /// 内部标签（默认不列在条件里）：物品类别标记（*_ITEM_TAG、MACHINE……）、归属和重要标记、编号、贴图和外观、显示用的假值。
    /// </summary>
    public static bool IsInternal(string key)
    {
        if (string.IsNullOrEmpty(key)) return true;
        if (!Internal.TryGetValue(key, out bool yes)) Internal[key] = yes = IsInternalKey(key);
        return yes;
    }

    /// <summary>键名 → 是不是内部标签（只取决于键名，复制全店时每件物品的每个标签都要问）。</summary>
    private static readonly Dictionary<string, bool> Internal = new();

    private static bool IsInternalKey(string key)
    {
        string k = key.ToUpperInvariant();
        if (k is "IS_OWNED_TAG" or "IMPORTANT_TAG" or "ITEM_TYPE_STRING" or "MACHINE" or "CONTAINER_TAG" or "MANUFACTURE") return true;
        if (k.EndsWith("_ITEM_TAG") || k.EndsWith("_ID") || k.Contains("_ID_") || k.EndsWith("_ID_TAG") || k.EndsWith("_IDS")) return true;
        if (k.Contains("SPRITE") || k.Contains("APPEARANCE") || k.Contains("ICON") || k.Contains("FAKE") || k.Contains("UNIQUE") || k.Contains("SEED_HASH")) return true;
        return false;
    }
}
