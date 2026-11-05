using System;
using System.Collections.Generic;
using AutoMod.Domain;
using AutoMod.Domain.Rules;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace AutoMod.Adapter;

/// <summary>
/// 游戏侧本地化接入。
/// 将游戏 / Unity 的当前语言和 ModLoader 读取的本地化表传递给领域的 L。
/// 不读文件、不用反射，全部使用公开接口。
/// </summary>
public static class LocBridge
{
    public const string ModId = "probably_automated";

    /// <summary>游戏当前语言代码（"en" "zh" "ru" "de" "ja" "fr" "es" "pl"）；未初始化时返回空串。</summary>
    public static string CurrentCode()
    {
        Locale a = ModHelper.GetCurrentLocale();
        string b = LocHelper.GetCurrentLocaleCode();
        string c = LocalizationSettings.SelectedLocale?.Identifier.Code;
        return a != null ? a.Identifier.Code : b ?? c ?? "";
    }

    /// <summary>本 mod 的翻译表：locale → (键 → 值)。ModLoader.Mods 是公开的静态列表。</summary>
    public static Dictionary<string, Dictionary<string, string>> Tables()
    {
        LoadedMod mod = ModLoader.Mods.Find(m => m != null && m.Manifest != null && m.Manifest.ID == ModId);
        return mod?.Localization;
    }

    /// <summary>将游戏中的语言和表应用到 L。</summary>
    public static void Apply()
    {
        string code = CurrentCode();
        if (string.IsNullOrEmpty(code)) code = "zh";
        var t = Tables();
        Dictionary<string, string> cur = null, en = null;
        t?.TryGetValue(code, out cur);
        t?.TryGetValue("en", out en);
        if (code != L.Locale) Names.ClearCache(); // 记住的物品名是上一种语言的
        L.Use(code, cur, en);
    }

    /// <summary>语言切换事件（UI 层可订阅以重建窗口或重置手册）。</summary>
    public static event Action LocaleChanged;

    /// <summary>语言切换时清理所有缓存并重新应用。</summary>
    public static void OnLocaleChanged()
    {
        Apply();
        GameName.ClearCache();
        Names.ClearCache();
        SlotNames.ClearLearned();
        MachineButtons.ClearCache();
        LocaleChanged?.Invoke();
        Log.Info($"[LocBridge] 语言切换为 {L.Locale}，已清理名称与手册缓存");
    }

    /// <summary>覆盖 mod 使用的语言（用于快速预览，不碰游戏的语言设置）。</summary>
    public static void OverrideLocale(string code)
    {
        if (string.IsNullOrEmpty(code)) return;
        var t = Tables();
        Dictionary<string, string> cur = null, en = null;
        t?.TryGetValue(code, out cur);
        t?.TryGetValue("en", out en);
        L.Use(code, cur, en);
        GameName.ClearCache();
        Names.ClearCache();
        SlotNames.ClearLearned();
        MachineButtons.ClearCache();
        LocaleChanged?.Invoke();
        Log.Info($"[LocBridge] mod 语言临时覆盖为 {code}，已清理名称与手册缓存");
    }

    private static bool _subscribed;

    /// <summary>初始化并订阅语言变化事件。</summary>
    public static void Init()
    {
        Apply();
        if (!_subscribed)
        {
            LocalizationSettings.SelectedLocaleChanged += _ => OnLocaleChanged();
            _subscribed = true;
        }
    }

    /// <summary>游戏自带的 mod 文字接口（当前语言 → en → 键本身，string.Format 参数）。</summary>
    public static string ViaGame(string key, params object[] args) => ModHelper.GetLocalized(ModId, key, args);

    /// <summary>复用游戏自己的词（物品按钮名、属性标签）：游戏表里的键，跟着游戏语言。查不到或错误退回键名。</summary>
    public static string GameWord(string key) =>
        TryGetGameString("Mechanic", key, out var text) ? text : key;

    /// <summary>判断游戏 StringTable 是否含某个键。</summary>
    public static bool HasGameEntry(string table, string key)
    {
        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key)) return false;
        try
        {
            return LocalizationSettings.StringDatabase.GetTable(table)?.GetEntry(key) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>判断游戏 Mechanic 表是否有某个键。</summary>
    public static bool HasMechanic(string key) => HasGameEntry("Mechanic", key);

    /// <summary>安全获取游戏本地化条目：键必须真正存在于表里，且绝不返回 Translation Error / 键名。</summary>
    public static bool TryGetGameString(string table, string key, out string text)
    {
        text = null;
        if (!HasGameEntry(table, key)) return false;
        try
        {
            string t = LocHelper.GetLocalized(table, key);
            if (!IsValidGameLoc(t, key)) return false;
            text = t;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>检查游戏返回的本地化字符串是否合法（绝不接受 Translation Error 或键名本身）。</summary>
    public static bool IsValidGameLoc(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text == key) return false;
        if (text.StartsWith("Translation Error", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.StartsWith("No translation found", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
