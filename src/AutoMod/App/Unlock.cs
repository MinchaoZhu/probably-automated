using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Adapter;
using AutoMod.Domain;

namespace AutoMod.App;

/// <summary>
/// 解锁“学徒派遣”：王尔德人脉网络里的一项升级（游戏的 mod 人脉升级 API）。
/// 价格、扣款、购买状态都由游戏管（按升级 id 存进本局存档）；买下时送学徒对讲机、学徒手册和一台
/// 蓝色标签打印机（给设备起名用），和游戏自己的职业解锁一样放在柜台上，不另弹提示。
/// </summary>
public static class Unlock
{
    public const string Id = "probably_automated.APPRENTICES";
    public const int Price = 650;
    public static string Title => Domain.L.T("unlock.title");

    /// <summary>人脉网络里的卡片：原版的“服务”卡片。</summary>
    private const string Card = "Service";

    public static string Description => Domain.L.T("unlock.desc");

    /// <summary>游戏的蓝色标签打印机（改自定义名，设备名就是它起的名字）。</summary>
    public const string BlueLabeler = "labeler_blue";


    /// <summary>这一局买下了学徒派遣。不在店里时为假。</summary>
    public static bool IsUnlocked => GameApi.InStore && ModHelper.IsNetworkUpgradeUnlocked(Id);

    /// <summary>OnEnable 时登记（新游戏创建这项升级，读档时游戏把它补进这一局并恢复购买状态）。</summary>
    public static void Register()
    {
        try
        {
            ModHelper.AddNetworkUpgrade(ModInfo.Id, Id, Price, Card,
                () => Title, () => Description, OnPurchased,
                iconKey: ModInfo.Id + ":" + ModItems.IntercomSprite);
        }
        catch (NullReferenceException)
        {
            // 游戏 0.46D：登记完定义后它还会访问 PlayerStore.Instance，主菜单里构造 PlayerStore 会抛异常。
            // 定义和解锁回调在那之前已经登记好了，读档时游戏会把升级补进这一局，所以忽略这个异常。
            Log.Info("[Unlock] 人脉升级已登记（主菜单阶段游戏内部访问 PlayerStore 失败，已忽略）");
        }
    }

    /// <summary>
    /// 读档后：存档里记着这项升级当时的价格，游戏不拿新定义覆盖。还没买的改成现在的价格
    /// （游戏自己对 PLANT_DNA 也是这样改价）。
    /// </summary>
    public static void SyncPrice()
    {
        try
        {
            var all = PlayerStore.Instance?.networkUpgrade;
            if (all != null && all.TryGetValue(Id, out var u) && !u.IsUnlocked() && u.cost != Price) u.cost = Price;
        }
        catch (Exception ex) { Log.Warn($"[Unlock] 同步价格失败: {ex.Message}"); }
    }

    /// <summary>
    /// 读档后：早期版本的对讲机在存档里记着 3×3 的占格，游戏读档时用存档里的占格，不看新图。
    /// 换成新的一件，放到后柜台（和买下时一样）；店里放不下时留着旧的。
    /// </summary>
    public static void ReplaceOldIntercoms()
    {
        foreach (var old in GameApi.CollectAll().Where(x => x.identifier == ModItems.Intercom && (x.shape?.width != 2 || x.shape?.height != 2)).ToList())
        {
            var item = ModHelper.SpawnItem(ModItems.Intercom);
            Give(item, ModItems.Intercom);
            if (item?.parentInventory != null) old.Destroy();
        }
    }

    /// <summary>玩家在人脉网络里买下升级（游戏已扣款）。</summary>
    private static void OnPurchased()
    {
        foreach (var id in new[] { ModItems.Intercom, ModItems.Handbook, BlueLabeler }) Give(ModHelper.SpawnItem(id), id);
        Log.Info($"[Unlock] 已解锁{Title}");
    }

    /// <summary>
    /// 和游戏自己的职业解锁（NetworkUpgradeList）一样用 EmporiumTryAdd 放进后柜台：
    /// 柜台放不下时游戏依次试物品栏、展示柜、前柜台，都放不下就丢弃。
    /// </summary>
    private static void Give(GameItem item, string id)
    {
        if (item == null) { Log.Warn($"[Unlock] 生成 {id} 失败"); return; }
        ModHelper.SetItemOwnerShip(item, true);
        GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, item);
        if (item.parentInventory == null) Log.Warn($"[Unlock] 店里放不下 {id}（柜台、物品栏、展示柜都满了）");
        else Log.Info($"[Unlock] {id} 已放在 {item.parentInventory.identifier}");
    }
}
