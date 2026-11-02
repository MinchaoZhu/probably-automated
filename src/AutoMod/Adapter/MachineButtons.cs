using System.Collections.Generic;
using System.Linq;
using UnityEngine.Localization.Settings;

namespace AutoMod.Adapter;

/// <summary>
/// 机器窗口里的按钮：普通按钮（MachineryButton）和单选按钮（MachineryRadioButton）。
/// 0.9 起优先存游戏 Mechanic 本地化表的键（如 mech_vending_btn_transfer、mech_bottle_printer_btn_print_small），
/// 执行与界面显示时按当前语言解析。无对应键的按钮保留显示文字或图标名。
/// </summary>
public static class MachineButtons
{
    private static readonly Dictionary<string, string> KnownKeys = new()
    {
        // 游戏常见机器按钮（中/英对照，跨语言无缝识别）
        { "打印小号水瓶", "mech_bottle_printer_btn_print_small" },
        { "Print Small Bottle", "mech_bottle_printer_btn_print_small" },
        { "打印水瓶", "mech_bottle_printer_btn_print_normal" },
        { "Print Bottle", "mech_bottle_printer_btn_print_normal" },
        { "打印大号水瓶", "mech_bottle_printer_btn_print_large" },
        { "Print Large Bottle", "mech_bottle_printer_btn_print_large" },
        { "开始打印流程", "mech_bottle_printer_btn_start" },
        { "Start Print Process", "mech_bottle_printer_btn_start" },
        { "全部转移", "mech_vending_btn_transfer" },
        { "Transfer All", "mech_vending_btn_transfer" },
        { "启动", "mech_blender_btn_start" },
        { "Start", "mech_blender_btn_start" },
        { "配置", "mech_chem_finisher_btn_configure" },
        { "Configure", "mech_chem_finisher_btn_configure" },
        { "输出", "mech_chem_finisher_btn_output" },
        { "Output", "mech_chem_finisher_btn_output" },
        { "混合", "mech_ras_btn_blend" },
        { "Blend", "mech_ras_btn_blend" },
        { "立即屠宰", "mech_auto_butcher_btn_butcher_now" },
        { "Butcher now", "mech_auto_butcher_btn_butcher_now" },
        { "屠宰托盘内容", "mech_auto_butcher_btn_butcher_content" },
        { "Butcher content", "mech_auto_butcher_btn_butcher_content" },
        { "立即搬运", "mech_hopper_btn_transfer_now" },
        { "Transfer now", "mech_hopper_btn_transfer_now" },
    };

    private static Dictionary<string, string> _textToKey;

    private static void EnsureMechanicMap()
    {
        if (_textToKey != null) return;
        _textToKey = new Dictionary<string, string>(KnownKeys);
        try
        {
            var table = LocalizationSettings.StringDatabase?.GetTable("Mechanic");
            if (table != null)
            {
                foreach (var entry in table.Values)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Key) && !string.IsNullOrEmpty(entry.Value))
                    {
                        if (entry.Key.Contains("_btn_") || !_textToKey.ContainsKey(entry.Value))
                            _textToKey[entry.Value] = entry.Key;
                    }
                }
            }
        }
        catch { }
    }

    public static void ClearCache() => _textToKey = null;

    public static List<SelectableElement> Of(GameItem item)
    {
        var list = new List<SelectableElement>();
        var handler = item?.contentWindow?.handler;
        if (handler == null) return list;
        foreach (var node in handler.GetComponentsInChildren<TreeNodeRender>(includeInactive: true))
            if (node.element is MachineryButton or MachineryRadioButton && !list.Contains((SelectableElement)node.element)) list.Add((SelectableElement)node.element);
        return list;
    }

    /// <summary>按钮的稳定唯一 ID：Mechanic 键 > 文字 > 图标名。</summary>
    public static string Id(SelectableElement e)
    {
        EnsureMechanicMap();
        return e switch
        {
            MachineryButton b when b.hasText => _textToKey != null && _textToKey.TryGetValue(b.text, out var k) ? k : b.text,
            MachineryButton b when b.icon != null => b.icon.name,
            MachineryRadioButton r when !string.IsNullOrEmpty(r.text) => _textToKey != null && _textToKey.TryGetValue(r.text, out var k) ? k : r.text,
            _ => "?",
        };
    }

    /// <summary>按钮的当前显示文字；只有图标的用图标名。</summary>
    public static string Label(SelectableElement e) => e switch
    {
        MachineryButton b => b.hasText ? b.text : b.icon != null ? b.icon.name : "?",
        MachineryRadioButton r => string.IsNullOrEmpty(r.text) ? "?" : r.text,
        _ => "?",
    };

    /// <summary>按钮 ID 的当前显示文字（Mechanic 键通过游戏本地化反查，否则按 L 或原样显示）。</summary>
    public static string DisplayName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if ((id.StartsWith("mech_") || id.Contains("_btn_")) && LocBridge.TryGetGameString("Mechanic", id, out var loc))
            return loc;
        return Domain.Rules.Text.ButtonName(id);
    }

    public static SelectableElement Find(GameItem item, string idOrLabel) =>
        Of(item).FirstOrDefault(b => Id(b) == idOrLabel || Label(b) == idOrLabel || DisplayName(idOrLabel) == Label(b) || (KnownKeys.TryGetValue(Label(b), out var k) && k == idOrLabel));

    /// <summary>游戏自己的点击处理会拒绝禁用、隐藏的按钮；Select() 不查，这里查。没有名字的（只有图标且认不出）不按。</summary>
    public static bool Pressable(SelectableElement b) => !b.isDisabled && !b.isHidden && Label(b) != "?";
}
