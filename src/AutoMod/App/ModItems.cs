using System;
using AutoMod.Domain;

namespace AutoMod.App;

/// <summary>
/// mod 自己的物品（游戏的 mod 物品目录 OnModItemDirectoryInit）：学徒对讲机、学徒手册、工作日报（每天早上由 Daily 发放）。解锁学徒派遣时送给玩家，
/// 放在店里任何地方，“使用”（双击或右键菜单）时打开对讲机窗口 / 学徒手册。存档里按物品 id 保存，读档时游戏重新调用工厂；
/// mod 停用后它们变成打不开的占位物品，再启用就恢复。
/// </summary>
public static class ModItems
{
    public const string Intercom = "probably_automated_intercom";
    public const string Handbook = "probably_automated_handbook";
    public const string Daily = "probably_automated_daily";

    /// <summary>对讲机的图（ModFiles/Textures/Items/intercom.png，32×32 = 2×2 格）。人脉升级的图标也用它。</summary>
    public const string IntercomSprite = "intercom";

    /// <summary>“使用”时打开的界面：界面层在启用时接上（App 不引用界面）。</summary>
    public static Action OpenIntercom, OpenHandbook, OpenDaily;

    public static void Subscribe() => ModHook.OnModItemDirectoryInit += Register;

    public static void Unsubscribe() => ModHook.OnModItemDirectoryInit -= Register;

    private static void Register(ModItemDirectory dir)
    {
        dir.Add(Intercom, () => Create(Intercom, Domain.L.T("item.intercom.name"),
            Domain.L.T("item.intercom.desc"),
            item => item.SetSpriteAndShapeFromMod(ModInfo.Id, IntercomSprite), () => OpenIntercom?.Invoke()));
        dir.Add(Handbook, () => Create(Handbook, Domain.L.T("item.handbook.name"),
            Domain.L.T("item.handbook.desc"),
            item =>
            {
                item.SetSpriteAndShape("Items/items_misc", "manual1"); // 游戏自己的手册图
                item.EnableTag("DOCUMENT");
                item.SetGameItemType("DOCUMENT");                      // 右键菜单里显示“阅读”
                BookHelper.InitPaperGuide(item);                       // 翻纸的声音
            },
            () => OpenHandbook?.Invoke()));
        dir.Add(Daily, () => Create(Daily, Domain.L.T("item.daily.name"),
            Domain.L.T("item.daily.desc"),
            item =>
            {
                item.SetSpriteAndShape("Items/itematlas", "newspaper"); // 游戏自己的报纸图
                item.EnableTag("DOCUMENT");
                item.SetGameItemType("DOCUMENT");
                BookHelper.InitPaperGuide(item);
            },
            () => OpenDaily?.Invoke()));
    }

    /// <summary>重要物品、不出售：顾客不买，也不会被收走。</summary>
    private static GameItem Create(string id, string name, string description, Action<GameItem> look, Action use)
    {
        var item = ItemDirectory.CreateEmptyItem(id);
        look(item);
        item.name = name;
        item.shortDescription = description;
        item.unitValue = 0;
        item.EnableTag(GeneralHelper.IMPORTANT_TAG);
        item.EnableTag(TypeHelper.NOT_FOR_RESALE);
        // 使用需要 may 和 can 都为真
        item.mayActivateSlotItemFunc = (_, _, _) => true;
        item.canActivateSlotItemFunc = (_, _, _) => true;
        item.onActivateSlotItemFunc = (_, _, _) =>
        {
            try { use(); }
            catch (Exception ex) { Log.Error($"[ModItems] {name}：{ex}"); }
        };
        ModHelper.SetSource(item, ModInfo.Id, ModInfo.Name);
        return item;
    }
}
