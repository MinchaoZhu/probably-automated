using System;
using System.Globalization;

namespace AutoMod.Domain.Workforce;

/// <summary>
/// 模组配置里的三个倍率：学徒费用、学徒体力、成长速度。用到时才乘，存档里只有基础值，改回 ×1 就恢复原样。
/// 解锁价不在这里：它是游戏的网络升级，价格已经按游戏难度算。
/// </summary>
public static class Scales
{
    /// <summary>下拉框的档位（显示和存进配置文件的都是这串文字）。×0 只用于费用（免费）。</summary>
    public static readonly string[] CostChoices = { "×0", "×0.25", "×0.5", "×0.75", "×1", "×1.5", "×2", "×3", "×4" };
    public static readonly string[] Choices = { "×0.25", "×0.5", "×0.75", "×1", "×1.5", "×2", "×3", "×4" };
    public const string Default = "×1";

    /// <summary>日薪、雇用费、扩编费、培训费。</summary>
    public static double Cost { get; set; } = 1;

    /// <summary>每天的 AP。</summary>
    public static double Ap { get; set; } = 1;

    /// <summary>升级经验、熟练度学习、培训经验。</summary>
    public static double Growth { get; set; } = 1;

    /// <summary>档位文字 → 倍率；不在档位里的值（手改配置文件）按 ×1。</summary>
    public static double Parse(string choice, string[] choices) =>
        Array.IndexOf(choices, choice) >= 0 && double.TryParse(choice.TrimStart('×'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 1;

    /// <summary>费用：四舍五入到整数；免费档为 0，其余至少 1。</summary>
    public static int Fee(int baseFee) => Cost <= 0 || baseFee <= 0 ? 0 : Math.Max(1, (int)Math.Round(baseFee * Cost, MidpointRounding.AwayFromZero));

    /// <summary>每天的 AP：向下取整，至少 1。</summary>
    public static int DailyAp(int baseAp) => Math.Max(1, (int)Math.Floor(baseAp * Ap + 1e-9));

    /// <summary>经验：乘倍率后取整（和不乘时一样用 Math.Round）。</summary>
    public static int Xp(double baseXp) => baseXp <= 0 ? 0 : (int)Math.Round(baseXp * Growth);
}
