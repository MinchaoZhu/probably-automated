using AutoMod.Domain;
using AutoMod.Domain.Workforce;

namespace AutoMod.App;

/// <summary>
/// mod 配置（游戏自带的模组配置：主菜单“改装件”里选中 mod 后点齿轮）。文件在
/// persistentDataPath/ModConfigs/probably_automated.json，全局生效，不随存档。
/// </summary>
public static class Settings
{
    private const string Section = ModInfo.Name;

    private static ConfigEntry<bool> _enabled, _allTags;
    private static ConfigEntry<float> _scale;
    private static ConfigEntry<string> _cost, _ap, _growth;

    /// <summary>学徒自动化总开关：关掉后不执行任何班次。</summary>
    public static bool AutomationEnabled => _enabled?.Value ?? true;

    /// <summary>界面大小倍数，在“按屏幕高度自动缩放”的基础上再乘。</summary>
    public static float UiScale => _scale?.Value ?? 1f;

    /// <summary>条件的属性下拉默认列出全部游戏标签（含内部标签）。</summary>
    public static bool ShowAllTags => _allTags?.Value ?? false;

    public static void Define(ModConfig config)
    {
        if (config == null) { Log.Warn("[Settings] 拿不到模组配置，用默认值"); return; }
        _enabled = config.Define("automation_enabled", true, Section, () => Domain.L.T("settings.automation_enabled"));
        _scale = config.Define("ui_scale", 1f, Section, () => Domain.L.T("settings.ui_scale"), 0.6f, 2f,
            () => Domain.L.T("settings.ui_scale_desc"));
        _allTags = config.Define("show_all_tags", false, Section, () => Domain.L.T("settings.show_all_tags"));

        // 学徒数值的三个倍率：下拉框显示和存的都是档位文字（“×1”），读到 Scales 里
        _cost = config.Define("cost_scale", Scales.Default, Section, () => Domain.L.T("settings.cost_scale"), Scales.CostChoices,
            () => Domain.L.T("settings.cost_scale_desc"));
        _ap = config.Define("ap_scale", Scales.Default, Section, () => Domain.L.T("settings.ap_scale"), Scales.Choices,
            () => Domain.L.T("settings.ap_scale_desc"));
        _growth = config.Define("growth_scale", Scales.Default, Section, () => Domain.L.T("settings.growth_scale"), Scales.Choices,
            () => Domain.L.T("settings.growth_scale_desc"));
        _cost.OnChanged += _ => ApplyScales();
        _ap.OnChanged += _ => ApplyScales();
        _growth.OnChanged += _ => ApplyScales();
        ApplyScales();
    }

    private static void ApplyScales()
    {
        Scales.Cost = Scales.Parse(_cost.Value, Scales.CostChoices);
        Scales.Ap = Scales.Parse(_ap.Value, Scales.Choices);
        Scales.Growth = Scales.Parse(_growth.Value, Scales.Choices);
        Log.Info($"[Settings] 倍率：费用 ×{Scales.Cost}，AP ×{Scales.Ap}，成长 ×{Scales.Growth}");
    }
}
