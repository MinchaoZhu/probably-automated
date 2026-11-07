using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;

namespace AutoMod.Adapter;

/// <summary>
/// 机器窗口的格子：窗口由带角色名的面板组成（水培机：power / module / water / seed / output），每个面板里有自己的库存。
/// 挑选器“取自”某个格子时只看这个面板里的物品（例如只看产出格，不碰种子格）。
/// 储藏区面板里的标签页各算一个格子，排在面板前面；面板本身（全部标签页）隐藏，只留给旧规则。
/// </summary>
public static class MachineSlots
{
    public sealed class Slot
    {
        public string Role, Label;
        public List<GameInventory> Inventories = new();

        /// <summary>有标签页的面板本身：不列出来，已经选了它的规则还能用。</summary>
        public bool Hidden;
    }

    public static List<Slot> Of(GameItem item)
    {
        var list = new List<Slot>();
        if (item?.contentWindow?.childElement is not MachineryPanelRow row) return list;
        foreach (var panel in row.Panels)
        {
            if (panel == null || string.IsNullOrEmpty(panel.role) || panel.handler == null) continue;
            var slot = new Slot { Role = panel.role };
            slot.Hidden = AddTabs(item, panel, list);
            foreach (var node in panel.handler.GetComponentsInChildren<TreeNodeRender>(includeInactive: true))
                if (node.element is GameInventory inv && !slot.Inventories.Contains(inv)) slot.Inventories.Add(inv);
            if (slot.Inventories.Count == 0) continue;
            // 面板标题是私有字段，取面板下第一个文字（标题最先建）；读不到用角色名
            var label = slot.Hidden ? null : panel.handler.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault()?.text;
            slot.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            SlotNames.Learn(item.identifier, slot.Role, slot.Label);
            list.Add(slot);
        }
        return list;
    }

    /// <summary>面板里的标签页各加一个格子，同一个储藏区里同名的合成一个。页面不全是库存的（屠宰机的控制页）不拆。</summary>
    private static bool AddTabs(GameItem item, MachineryPanel panel, List<Slot> list)
    {
        if (panel.content is not MachineryTabs tabs || tabs.count == 0) return false;
        for (int i = 0; i < tabs.count; i++)
            if (tabs.GetTabInventory(i) == null) return false;
        for (int i = 0; i < tabs.count; i++)
        {
            string role = SlotNames.TabRole(panel.role, i, StorageExpansionHelper.GetTabName(item, i));
            var slot = list.FirstOrDefault(s => s.Role == role);
            if (slot == null)
            {
                list.Add(slot = new Slot { Role = role, Label = tabs.GetCaption(i) });
                SlotNames.Learn(item.identifier, role, slot.Label);
            }
            slot.Inventories.Add(tabs.GetTabInventory(i));
        }
        return true;
    }

    public static Slot Find(GameItem item, string role) => Of(item).FirstOrDefault(s => s.Role == role);
}
