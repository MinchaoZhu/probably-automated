using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Adapter;

/// <summary>
/// 游戏物品的事实：库存、标签、行为函数槽、物品原型目录。
/// 只读，不带 mod 的解释；类别、属性的推导在 TraitBindings / PropBindings。
/// </summary>
public static class ItemFacts
{
    // ---------- 库存 ----------

    /// <summary>物品自己的库存：图节点里窗口 → 网格元素 → 库存，逐层找，遇到物品或库存就停（机器的槽位挂在窗口下面）。</summary>
    public static List<GameInventory> Inventories(GameItem box)
    {
        var list = new List<GameInventory>();
        void Walk(GraphNodeStorage node, int depth)
        {
            if (depth > 4) return;
            List<GraphNodeStorage> kids;
            try { kids = node.children; } catch { return; }
            if (kids == null) return;
            foreach (var k in kids)
            {
                if (k == null || k is GameItem) continue;
                if (k is GameInventory inv) { list.Add(inv); continue; }
                Walk(k, depth + 1);
            }
        }
        try { Walk(box, 0); } catch { }
        return list;
    }

    // ---------- 标签 ----------

    /// <summary>标签（键名排序）。物品和库存都有标签系统。</summary>
    public static List<KeyValuePair<string, TagState>> Tags(TagSystem tags)
    {
        if (tags?.dict == null) return new List<KeyValuePair<string, TagState>>();
        return tags.dict.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>标签的数值：整数 / 浮点 / 长整 / 双精度里非零的那个；都为零时为 0（开关和文字标签也是 0）。</summary>
    public static double Number(TagState t) =>
        t.valueInt != 0 ? t.valueInt : t.valueFloat != 0 ? t.valueFloat : t.valueLong != 0 ? t.valueLong : t.valueDouble;

    // ---------- 行为 ----------

    /// <summary>
    /// 物品上设置了的行为函数槽。游戏的行为是匿名的函数，这里只能知道“有没有”：
    /// thisTarget = 能作用于别的物品（工具），itemTargetThis = 别的物品能作用于它，activate / toggle = 使用 / 开关，
    /// cycleEnd = 每夜结算。
    /// </summary>
    public static List<string> Behaviors(GameItem i)
    {
        var list = new List<string>();
        void Has(string name, object f) { if (f != null) list.Add(name); }
        Has("thisTarget", i.onThisTargetItemFunc);
        Has("itemTargetThis", i.onItemTargetThisFunc);
        Has("itemTargetItem", i.onItemTargetItemFunc);
        Has("slotItemTargetItem", i.onSlotItemTargetItemFunc);
        Has("itemTargetSlotItem", i.onItemTargetSlotItemFunc);
        Has("activate", i.onActivateSlotItemFunc);
        Has("toggle", i.onToggleSlotItemFunc);
        Has("cycleEndEarly", i.onCycleEndEarlySlotItemFunc);
        Has("cycleEnd", i.onCycleEndSlotItemFunc);
        Has("cycleEndLate", i.onCycleEndLateSlotItemFunc);
        Has("oneSecond", i.onOneSecondItemFunc);
        Has("slotAdd", i.onSlotAddItemFunc);
        Has("slotRemove", i.onSlotRemoveItemFunc);
        return list;
    }

    // ---------- 物品原型目录 ----------

    private static Dictionary<string, string> _directoryOf;

    /// <summary>原型 id → 注册它的物品目录（Tool、Husbandry、Food……，游戏里最接近“大类”的东西）。</summary>
    public static IReadOnlyDictionary<string, string> DirectoryOf
    {
        get
        {
            if (_directoryOf != null && _directoryOf.Count > 0) return _directoryOf;
            var map = new Dictionary<string, string>();
            foreach (var dir in DirectoryMaster.GetDictionaryClassNameList<GameItem>())
                foreach (var id in DirectoryMaster.GetIdentifierList<GameItem>(dir))
                    if (!map.ContainsKey(id)) map[id] = Short(dir);
            return _directoryOf = map;
        }
    }

    /// <summary>“HusbandryDirectory” → “Husbandry”，“ToolItemDirectory” → “Tool”。</summary>
    private static string Short(string dir)
    {
        foreach (var suffix in new[] { "ItemDirectory", "Directory" })
            if (dir.EndsWith(suffix) && dir.Length > suffix.Length) return dir.Substring(0, dir.Length - suffix.Length);
        return dir;
    }

    /// <summary>
    /// 创建一件原型实例用来读取它的事实（不放进店里）。只能在店里调用（库存要建界面节点）。用完必须 Destroy。
    /// 和游戏自己创建物品（DirectoryMaster.Item）一样，创建后立刻分配 uniqueId：游戏里不存在 uid 为 0 的普通物品，
    /// 别的 mod 也按这个前提写代码（例如按 uid 登记界面状态），uid 为 0 的原型可能让它们反复重建、卡死游戏。
    /// 代价是每件原型用掉存档 uid 计数器的一个号，计数器是 int，不影响存档。
    /// </summary>
    public static GameItem CreatePrototype(string id)
    {
        try
        {
            var item = DirectoryMaster.Create<GameItem>(id);
            var store = GameApi.Store;
            if (item != null && item.uniqueId == 0 && store != null) item.uniqueId = store.GetUniqueID();
            return item;
        }
        catch { return null; }
    }
}
