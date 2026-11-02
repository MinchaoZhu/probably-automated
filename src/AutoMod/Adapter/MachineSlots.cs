using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;

namespace AutoMod.Adapter;

/// <summary>
/// 机器窗口的格子：窗口由带角色名的面板组成（水培机：power / module / water / seed / output），每个面板里有自己的库存。
/// 挑选器“取自”某个格子时只看这个面板里的物品（例如只看产出格，不碰种子格）。
/// </summary>
public static class MachineSlots
{
    public sealed class Slot
    {
        public string Role, Label;
        public List<GameInventory> Inventories = new();
    }

    public static List<Slot> Of(GameItem item)
    {
        var list = new List<Slot>();
        if (item?.contentWindow?.childElement is not MachineryPanelRow row) return list;
        foreach (var panel in row.Panels)
        {
            if (panel == null || string.IsNullOrEmpty(panel.role) || panel.handler == null) continue;
            var slot = new Slot { Role = panel.role };
            foreach (var node in panel.handler.GetComponentsInChildren<TreeNodeRender>(includeInactive: true))
                if (node.element is GameInventory inv && !slot.Inventories.Contains(inv)) slot.Inventories.Add(inv);
            if (slot.Inventories.Count == 0) continue;
            // 面板标题是私有字段，取面板下第一个文字（标题最先建）；读不到用角色名
            var label = panel.handler.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault()?.text;
            slot.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            SlotNames.Learn(item.identifier, slot.Role, slot.Label);
            list.Add(slot);
        }
        return list;
    }

    public static Slot Find(GameItem item, string role) => Of(item).FirstOrDefault(s => s.Role == role);
}
