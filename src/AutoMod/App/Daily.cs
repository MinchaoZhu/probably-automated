using System.Collections.Generic;
using System.Linq;
using AutoMod.Adapter;
using AutoMod.Domain;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Workforce;

namespace AutoMod.App;

/// <summary>
/// 工作日报：早班有学徒当班时，早班做完后发一份报纸物品（代替以前弹出的报告窗口），双击阅读。
/// 要不要发在入睡结算后定下（知道谁上岗了），夜间报告里写明；定下要发时，店里旧的日报在入睡时先回收，
/// 所以店里至多一份，是最新的。放哪沿用游戏自己早报的做法：选项“早报放进垃圾桶”开则放垃圾桶，否则放前柜台（放不下退回后柜台）。
/// </summary>
public static class Daily
{
    /// <summary>明早早班有学徒当班（已上岗并排了早班流程）。</summary>
    public static bool WillIssue() => Automation.State.Apprentices.Any(a => a.OnDuty && a.DutiesIn(Shift.Morning).Any());

    private static List<GameItem> InStore() => GameApi.CollectAll().Where(i => i.identifier == ModItems.Daily).ToList();

    /// <summary>入睡结算（发薪）之后：定下明早发不发，回收旧的，在夜间报告里写一行。还没雇学徒的不提。</summary>
    public static void PlanTonight()
    {
        var st = Automation.State;
        st.DailyPlanned = false;
        var store = GameApi.Store;
        if (store == null || !Settings.AutomationEnabled || !Unlock.IsUnlocked || st.Apprentices.Count == 0) return;
        if (!WillIssue()) { store.AddNightLog(Domain.L.T("daily.log_none")); return; }
        st.DailyPlanned = true;
        var old = InStore();
        foreach (var item in old) item.Destroy();
        store.AddNightLog(Domain.L.T(old.Count > 0 ? "daily.log_replaced" : "daily.log_issued"));
        Log.Info($"[Daily] 回收旧日报 {old.Count} 份，明早发新的");
    }

    /// <summary>早班执行完（醒来后）：入睡时定下要发的话，发一份。</summary>
    public static void OnShiftCompleted(ShiftReport r)
    {
        if (r.Shift != Shift.Morning || r.DryRun) return;
        var st = Automation.State;
        if (!st.DailyPlanned) return;
        st.DailyPlanned = false;
        var item = ModHelper.SpawnItem(ModItems.Daily);
        if (item == null) { Log.Warn("[Daily] 生成工作日报失败"); return; }
        var night = st.Reports.LastOrDefault(x => !x.DryRun && x.Shift == Shift.Night && x.Day == r.Day - 1);
        int wage = 0, strikers = 0;
        if (night != null)
        {
            var w = st.Wages.FirstOrDefault(x => x.Day == night.Day);
            if (w != null) { wage = w.Amount; strikers = w.Strikers; }
        }
        st.Daily = new DailyEdition
        {
            Day = r.Day,
            Morning = r,
            Night = night,
            Wage = wage,
            Strikers = strikers,
        };
        ModHelper.SetItemOwnerShip(item, true);
        // 和游戏自己的早报一样放进垃圾桶物品里面的容器（ModHelper.MoveItemToTrash 是往垃圾桶所在的槽里放，放不进去）
        if (EmporiumOption.NewspaperToTrash && EmporiumEntry.Instance.trashcan?.contentWindow?.child is GameInventory trash)
            GraphUtils.TryAcceptAll(trash, item);
        // 不进垃圾桶时和游戏自己的早报一样放前柜台（橱窗这一侧，晚上游戏会清空它）；放不下时退回后柜台等处
        if (item.parentInventory == null) GraphUtils.TryAcceptAll(EmporiumEntry.Instance.frontInvinvElement, item);
        if (item.parentInventory == null) GraphUtils.EmporiumTryAdd(EmporiumEntry.Instance.backInvinvElement, item);
        if (item.parentInventory == null) Log.Warn("[Daily] 店里放不下工作日报（垃圾桶、柜台、物品栏、展示柜都满了）");
        else Log.Info($"[Daily] 第 {r.Day} 天的工作日报已放在 {item.parentInventory.identifier}（垃圾桶选项：{EmporiumOption.NewspaperToTrash}）");
    }
}
