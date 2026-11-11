using System.Collections.Generic;

namespace AutoMod.Adapter;

/// <summary>
/// 属性名用游戏自己的文字（跟着游戏语言变）：游戏提示里用的本地化条目，只取纯标签（不是带占位符的句子）；
/// 游戏标签的名字见 TagNames（Label 给它读条目）。取不到返回 null（用目录里的中文名）。
/// </summary>
public static class GameName
{
    /// <summary>目录属性 → 游戏提示里的本地化条目（mechanic 表），在游戏里核对过是纯标签的才收。</summary>
    private static readonly Dictionary<string, string> PropKeys = new()
    {
        ["health"] = "husbandry_tooltip_health",       // 健康值
        ["age"] = "husbandry_tooltip_age",             // 年龄：{0}天（{1}克）→ 年龄
        ["gene.fertility"] = "husbandry_tooltip_fertility",
        ["durability"] = "mech_durability_display",    // 耐久度：{0}/{1} → 耐久度
        ["waterUse"] = "mech_seed_water_use_rate",     // 耗水率：{0}毫升/天 → 耗水率
    };

    private static readonly Dictionary<string, string> Cache = new();

    public static void ClearCache() => Cache.Clear();

    public static string Prop(string id) => PropKeys.TryGetValue(id, out var key) ? Mechanic(key) : null;

    /// <summary>
    /// 本地化条目的文字当属性名：纯标签直接用（“健康值：”去掉冒号）；带占位符的句子取冒号前的标签（“耗水率：{0}毫升/天”→“耗水率”）；
    /// 没有、等于键名、冒号前也带占位符的返回 null。
    /// </summary>
    public static string Mechanic(string key)
    {
        if (Cache.TryGetValue(key, out var c)) return c;
        string t = null;
        if (LocBridge.TryGetGameString("Mechanic", key, out var raw))
        {
            t = raw;
            if (t.Contains("{"))
            {
                int colon = t.IndexOfAny(new[] { '：', ':' });
                t = colon > 0 && !t[..colon].Contains("{") ? t[..colon] : null;
            }
            if (!string.IsNullOrWhiteSpace(t))
                t = t.Trim().TrimEnd(':', '：').Trim();
            else
                t = null;
        }
        return Cache[key] = t;
    }

    /// <summary>
    /// 本地化条目的文字当标签名（TagNames 用）：只收纯标签，不收说明句和带占位符的句子；末尾的冒号去掉（“剩余使用次数：”）。
    /// </summary>
    public static string Label(string key)
    {
        if (!LocBridge.TryGetGameString("Mechanic", key, out var raw)) return null;
        string t = raw.Trim();
        if (t.Length == 0 || t.Length > 40 || t.Contains("{") || t.Contains("\n")) return null;
        if (t.EndsWith(".") || t.EndsWith("。") || t.EndsWith("!") || t.EndsWith("！") || t.EndsWith("?") || t.EndsWith("？")) return null;
        t = t.TrimEnd(':', '：').Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>本地化条目的原文（带占位符的也原样给）。</summary>
    public static string Format(string key) => LocBridge.TryGetGameString("Mechanic", key, out var raw) ? raw : null;
}
