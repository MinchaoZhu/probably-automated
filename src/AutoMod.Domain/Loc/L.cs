using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoMod.Domain;

/// <summary>
/// 多语言核心。
/// - 英语是主语言（en.csv 是源头和后备），中文和其他语言是其译文；
/// - 键是语义键（[a-z0-9_.]），不含英文逗号；
/// - 查表顺序：当前语言 → en → 键本身；
/// - 纯领域层，不引用游戏和 Unity。
/// </summary>
public static class L
{
    /// <summary>当前语言代码（游戏的 Locale.Identifier.Code：en zh ru de ja fr es pl）。默认 zh。</summary>
    public static string Locale { get; private set; } = "zh";

    private static IReadOnlyDictionary<string, string> _cur = new Dictionary<string, string>();
    private static IReadOnlyDictionary<string, string> _en = new Dictionary<string, string>();

    /// <summary>当前语言下查不到的键（测试断言为空；游戏里每个键只记一次日志）。</summary>
    public static readonly HashSet<string> Missing = new();

    /// <summary>切换语言：current 为当前语言的表，english 为后备英文表。</summary>
    public static void Use(string locale, IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> english)
    {
        Locale = string.IsNullOrEmpty(locale) ? "zh" : locale;
        _cur = current ?? new Dictionary<string, string>();
        _en = english ?? new Dictionary<string, string>();
        Missing.Clear();
    }

    public static bool IsZh => Locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    public static bool IsJa => Locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

    // ================= 查表 =================

    public static string Raw(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (_cur.TryGetValue(key, out var v)) return v;
        if (_en.TryGetValue(key, out v))
        {
            if (!Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)) Missing.Add(key);
            return v;
        }
        Missing.Add(key);
        return key;
    }

    /// <summary>读取浮点数配置（如布局间距），查不到或解析失败返回后备值。</summary>
    public static float Float(string key, float fallback = 0f) =>
        float.TryParse(Raw(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>读取整数配置（如列宽），查不到或解析失败返回后备值。</summary>
    public static int Int(string key, int fallback = 0) =>
        int.TryParse(Raw(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>翻译一句；args 填进 {0} {1}…（格式化失败时返回未格式化的文字，不抛异常）。</summary>
    public static string T(string key, params object[] args) => Format(Raw(key), args);

    /// <summary>带数量的句子：n 决定复数形式（译文用“|”分隔）。n 也作为 {0}，其余参数依次是 {1} {2}…。</summary>
    public static string N(string key, double n, params object[] args)
    {
        var raw = Raw(key);
        var forms = raw.Split('|');
        string form = forms[Math.Min(PluralIndex(Locale, n), forms.Length - 1)];
        return Format(form, new object[] { Num(n) }.Concat(args ?? Array.Empty<object>()).ToArray());
    }

    /// <summary>同 N，但 {0} 显示 shown（数量是表达式时：复数形式按 n 选，显示表达式文字）。</summary>
    public static string NAs(string key, double n, string shown, params object[] args)
    {
        var raw = Raw(key);
        var forms = raw.Split('|');
        string form = forms[Math.Min(PluralIndex(Locale, n), forms.Length - 1)];
        return Format(form, new object[] { shown }.Concat(args ?? Array.Empty<object>()).ToArray());
    }

    /// <summary>当前语言或英文后备表里有没有这个键（可选的键用它判断，不记进 Missing）。</summary>
    public static bool Has(string key) =>
        (!string.IsNullOrEmpty(key)) && ((_cur != null && _cur.ContainsKey(key)) || (_en != null && _en.ContainsKey(key)));

    private static string Format(string s, object[] args)
    {
        if (args == null || args.Length == 0) return s;
        try { return string.Format(CultureInfo.InvariantCulture, s, args); }
        catch (FormatException) { return s; }
    }

    public static string Num(double n) => n.ToString("0.##", CultureInfo.InvariantCulture);

    // ================= 语言规则（复数规则与列表格式） =================

    /// <summary>复数形式的序号：0 = one，1 = few/other……（只列游戏支持的 8 种语言）。</summary>
    public static int PluralIndex(string locale, double n)
    {
        bool integer = Math.Abs(n - Math.Round(n)) < 1e-9;
        long i = (long)Math.Abs(Math.Round(n));
        switch (locale)
        {
            case "zh": case "ja": return 0;
            case "fr": return n >= 0 && n < 2 ? 0 : 1;
            case "ru":
                if (!integer) return 2;
                if (i % 10 == 1 && i % 100 != 11) return 0;
                if (i % 10 is >= 2 and <= 4 && !(i % 100 is >= 12 and <= 14)) return 1;
                return 2;
            case "pl":
                if (!integer) return 2;
                if (i == 1) return 0;
                if (i % 10 is >= 2 and <= 4 && !(i % 100 is >= 12 and <= 14)) return 1;
                return 2;
            default: return integer && i == 1 ? 0 : 1;
        }
    }

    /// <summary>列表连接：and = 条件“且”，or = “或”，list = 并列（顿号 / 逗号），seq = 动作序列（分号）。</summary>
    public static string Join(string kind, IEnumerable<string> items)
    {
        var xs = items.Where(x => !string.IsNullOrEmpty(x)).ToList();
        string sep = (IsZh, kind) switch
        {
            (true, "and") => " 且 ",
            (true, "or") => " 或 ",
            (true, "list") => "、",
            (true, "seq") => "；",
            (true, "semi") => "；",
            (true, "cond") => "、",
            (true, "list2") => "，",
            (true, "comma") => "，",
            (true, "dot") => " · ",
            (false, "and") => " " + (T("conj.and") is var a && a != "conj.and" ? a : "and") + " ",
            (false, "or") => " " + (T("conj.or") is var o && o != "conj.or" ? o : "or") + " ",
            (false, "list") => IsJa ? "、" : ", ",
            (false, "cond") => IsJa ? "、" : ", ",
            (false, "seq") => "; ",
            (false, "semi") => "; ",
            (false, "dot") => " · ",
            _ => ", ",
        };
        return string.Join(sep, xs);
    }

    // ================= 带可点词的句子模板（卡片） =================

    private static readonly Regex Slot = new(@"\{(\w+)\}");

    /// <summary>
    /// 把翻译后的模板拆成片段：“{op} {list} into {to}” → [slot op][text " "][slot list][text " into "][slot to]。
    /// 可点的词按名字放进模板，所以各语言可以有不同语序；模板里漏了某个槽位时补在句尾（不丢可点的词）。
    /// </summary>
    public static List<(string text, string slot)> Template(string key)
    {
        string t = Raw(key);
        var parts = new List<(string, string)>();
        int at = 0;
        var seen = new HashSet<string>();
        foreach (Match m in Slot.Matches(t))
        {
            if (m.Index > at) parts.Add((t.Substring(at, m.Index - at), null));
            parts.Add((null, m.Groups[1].Value));
            seen.Add(m.Groups[1].Value);
            at = m.Index + m.Length;
        }
        if (at < t.Length) parts.Add((t.Substring(at), null));

        // 如果英文后备模板里有当前模板缺失的槽位，补在末尾
        string enT = _en != null && _en.TryGetValue(key, out var ev) ? ev : key;
        foreach (Match m in Slot.Matches(enT))
        {
            if (!seen.Contains(m.Groups[1].Value))
            {
                parts.Add((" ", null));
                parts.Add((null, m.Groups[1].Value));
                seen.Add(m.Groups[1].Value);
            }
        }
        return parts;
    }

    // ================= CSV 解析（与游戏 ModLoader.LoadLocalization / ParseCsvValue 一致） =================

    /// <summary>
    /// 游戏的格式：每行“键,值”，按第一个英文逗号分开；空行和 # 开头的行跳过；键和值去掉首尾空白；
    /// 值两端有双引号时去掉、"" 还原成 "；值里的 \n 换成换行。键不能含英文逗号、不能有首尾空格、不能以 # 开头。
    /// </summary>
    public static Dictionary<string, string> ParseCsv(IEnumerable<string> lines)
    {
        var d = new Dictionary<string, string>();
        if (lines == null) return d;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
            int comma = line.IndexOf(',');
            if (comma < 0) continue;
            string key = line.Substring(0, comma).Trim();
            if (key.Length == 0) continue;
            string v = line.Substring(comma + 1).Trim();
            if (v.Length >= 2 && v.StartsWith("\"") && v.EndsWith("\""))
                v = v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
            d[key] = v.Replace("\\n", "\n");
        }
        return d;
    }
}
