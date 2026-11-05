using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Adapter;

/// <summary>游戏物品的通用操作：父容器、子物品、电池槽、收集全部物品、标签读写、显示名。</summary>
public static class GameApi
{
    /// <summary>店铺场景已载入（存档已读入）。主菜单里为假。</summary>
    public static bool InStore => EmporiumEntry.Instance != null;

    /// <summary>
    /// 当前这局的 PlayerStore；不在店里时为空。不能直接判断 PlayerStore.Instance：它是懒加载单例，
    /// 主菜单里一访问就会新建一个（构造函数在那时还会抛异常）。
    /// </summary>
    public static PlayerStore Store => InStore ? PlayerStore.Instance : null;

    public static GameItem ParentContainer(GameItem item)
    {
        try { return item.parentInventory?.GetParentItem(); }
        catch { return null; }
    }

    /// <summary>直接装在容器里的物品（不含它们里面的），按游戏图节点的层序。</summary>
    public static List<GameItem> DirectChildren(GameItem container)
    {
        var list = new List<GameItem>();
        ItemChildren(container, list);
        return list;
    }

    private static readonly Queue<GraphNodeStorage> NodeQueue = new();

    /// <summary>
    /// 节点（物品、窗口）下面最近一层的物品：沿图节点往下按层序找，遇到物品就收下、不再往它里面走。
    /// 游戏的 FindAllChildItems 每次都遍历整棵子树，这里每个节点只看一次。
    /// </summary>
    private static void ItemChildren(GraphNodeStorage node, List<GameItem> into)
    {
        var queue = NodeQueue;
        queue.Clear();
        try
        {
            queue.Enqueue(node);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 100000)
            {
                var kids = queue.Dequeue().children;
                if (kids == null) continue;
                for (int i = 0; i < kids.Count; i++)
                {
                    var k = kids[i];
                    if (k == null) continue;
                    if (k is GameItem item) into.Add(item);
                    else queue.Enqueue(k);
                }
            }
        }
        catch { }
        finally { queue.Clear(); }
    }

    public static GameSlotInventory BatterySlot(GameItem machine)
    {
        try { return MachineHelper.GetBatterySlot(machine); }
        catch { return null; }
    }

    /// <summary>店里的全部物品（含嵌套）。</summary>
    public static List<GameItem> CollectAll()
    {
        using var _p = Domain.Perf.Begin("scan");
        var seen = new HashSet<GameItem>();
        var result = new List<GameItem>();
        var store = Store;
        if (store == null) return result;
        var roots = store.FindAllItem(false);
        for (int i = 0; i < roots.Count; i++) Walk(roots[i], seen, result, 0);
        return result;
    }

    /// <summary>物品和它里面的全部物品（先自己，再逐个往里），每件只收一次。</summary>
    private static void Walk(GameItem item, HashSet<GameItem> seen, List<GameItem> result, int depth)
    {
        if (item == null || depth > 12 || !seen.Add(item)) return;
        result.Add(item);
        var kids = new List<GameItem>();
        ItemChildren(item, kids);
        for (int i = 0; i < kids.Count; i++) Walk(kids[i], seen, result, depth + 1);
    }

    public static HashSet<GameItem> ItemSet(IEnumerable<GameItem> items) => new(items);

    /// <summary>
    /// 全店：店里归玩家的东西（任意层）。不含柜台（顾客的东西）、陈列待售的货、垃圾箱、文件夹里的；
    /// 店面上装的机器算；售货机、售水机里的货不算（它们的电池槽算）。
    /// </summary>
    public static List<GameItem> ShopItems()
    {
        var result = new List<GameItem>();
        if (Store == null || EmporiumEntry.Instance == null) return result;
        var e = EmporiumEntry.Instance;
        var seen = new HashSet<GameItem>();
        var tops = new List<GameItem>();
        foreach (var w in new GraphNodeStorage[] { e.invWindow, e.backInvWindow, e.backInvinvElementCounter, e.afterhourWindow, e.vendingFountainWindow, e.vendingMachineWindow })
        {
            if (w == null) continue;
            tops.Clear();
            ItemChildren(w, tops);
            foreach (var it in tops) WalkShop(it, seen, result, 0, false);
        }
        // 店面（陈列柜）：装在那里的机器和机器里的东西算，陈列待售的货不算
        if (e.showcaseWindow != null)
        {
            try { foreach (var it in GraphUtils.FindAllChildrenType<GameItem>(e.showcaseWindow)) if (IsMachine(it)) WalkShop(it, seen, result, 0, InSaleStock(it)); }
            catch { }
        }
        return result;
    }

    /// <summary>
    /// 物品和它里面的全部物品里算全店的（归玩家、不在售货设备的货架上）。在不在货架上从外往里带下来：
    /// 逐件往上找父容器的话，物品多的店每次要找几千回。
    /// </summary>
    private static void WalkShop(GameItem item, HashSet<GameItem> seen, List<GameItem> result, int depth, bool onSale)
    {
        if (item == null || depth > 12 || !seen.Add(item)) return;
        try { if (!onSale && item.IsTag("IS_OWNED_TAG")) result.Add(item); }
        catch { }
        var kids = new List<GameItem>();
        ItemChildren(item, kids);
        if (kids.Count == 0) return;
        bool vending = Tag(item, "VENDING_CASH") != null;
        var battery = vending ? BatterySlot(item) : null;
        for (int i = 0; i < kids.Count; i++)
            WalkShop(kids[i], seen, result, depth + 1, onSale || (vending && !ReferenceEquals(kids[i].parentInventory, battery)));
    }

    private static bool IsMachine(GameItem it)
    {
        try { return BatterySlot(it) != null || it.IsGameItemType("MACHINE"); }
        catch { return false; }
    }

    /// <summary>在售货机 / 售水机的货架上（不是它们的电池槽）。售货设备按游戏标签 VENDING_CASH 认。</summary>
    private static bool InSaleStock(GameItem it)
    {
        var child = it;
        for (var p = ParentContainer(child); p != null; child = p, p = ParentContainer(p))
            if (Tag(p, "VENDING_CASH") != null && !ReferenceEquals(child.parentInventory, BatterySlot(p))) return true;
        return false;
    }

    // ---------- 设备名 ----------

    /// <summary>
    /// 设备名就是游戏的自定义名（蓝色标签打印机改的那个，CUSTOM_NAME_TAG）：窗口标题、物品提示里游戏自己会显示。
    /// </summary>
    public static string DeviceName(GameItem it)
    {
        try { return GeneralHelper.GetCustomName(it) is { Length: > 0 } n ? n : null; }
        catch { return null; }
    }

    /// <summary>改名（为空 = 去掉名字），开着的窗口标题跟着改（和游戏的改名框一样）。</summary>
    public static void SetDeviceName(GameItem it, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) GeneralHelper.RemoveCustomName(it);
        else GeneralHelper.SetCustomName(it, name.Trim());
        try
        {
            if (it.contentWindow != null && it.contentWindow.IsVisible()) it.contentWindow.SetTitle(it.GetDisplayName(preferCustomName: true));
        }
        catch { }
    }

    /// <summary>打开游戏自己的改名框（蓝色标签打印机用的那个）；改完调用 done。</summary>
    public static bool OpenRename(GameItem it, string initial, Action<string> done)
    {
        var ui = LabelerBlueUIManager.Instance;
        if (ui == null) return false;
        ui.OpenUI(initial ?? DeviceName(it) ?? "", text => done(text), () => done(null));
        return true;
    }

    // ---------- 标签读写 ----------

    public static TagState Tag(GameItem item, string key)
    {
        var dict = item.state?.dict;
        if (dict == null || !dict.ContainsKey(key)) return null;
        return dict[key];
    }

    public static string GetString(GameItem item, string key)
    {
        try { return Tag(item, key)?.valueString; }
        catch { return null; }
    }

    public static void SetString(GameItem item, string key, string value)
    {
        item.ModifyTag(key, t => { t.SetString(value); t.Enable(); }, true);
    }

    public static void SetInt(GameItem item, string key, int value)
    {
        item.ModifyTag(key, t => { t.SetInt(value); t.Enable(); }, true);
    }

    public static void RemoveTag(GameItem item, string key)
    {
        var dict = item.state?.dict;
        if (dict != null && dict.ContainsKey(key)) dict.Remove(key);
    }

    // ---------- 显示 ----------

    /// <summary>玩家起的名字，没有就用物品名。</summary>
    public static string DefaultName(GameItem item)
    {
        if (item == null) return "";
        string n = null;
        try { n = GeneralHelper.GetCustomName(item); } catch { }
        if (!string.IsNullOrEmpty(n)) return n;
        if (!string.IsNullOrEmpty(item.identifier))
        {
            string kn = Names.Kind(item.identifier);
            if (!string.IsNullOrEmpty(kn) && kn != item.identifier) return kn;
        }
        return item.name;
    }

    /// <summary>名字#uid。</summary>
    public static string Describe(GameItem item) => $"{DefaultName(item)}#{item.uniqueId}";
}
