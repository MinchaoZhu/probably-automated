using System;
using AutoMod.Domain;
using AutoMod.Domain.Workforce;

namespace AutoMod.App;

/// <summary>
/// 时钟：订阅游戏的 mod 钩子——读档、新游戏、晚班、存档前、早班。
/// 夜间顺序（EndOfDayUIManager.OnEndDayButtonClick）：入睡 Early → 付夜间服务费 → 夜间结算 → 入睡 Late
/// → 新的一天 → 存档 → 醒来。游戏只在这里存档，所以 mod 数据在入睡 Late 写回。
/// </summary>
internal static class Clock
{
    public static void Subscribe()
    {
        ModHook.OnGameLoadedLate += GameLoaded;
        ModHook.OnNewGameStarted += NewGame;
        ModHook.OnGoingSleepEarly += GoingSleep;
        ModHook.OnGoingSleepLate += BeforeSave;
        ModHook.OnWakingUpLate += WakingUp;
    }

    public static void Unsubscribe()
    {
        ModHook.OnGameLoadedLate -= GameLoaded;
        ModHook.OnNewGameStarted -= NewGame;
        ModHook.OnGoingSleepEarly -= GoingSleep;
        ModHook.OnGoingSleepLate -= BeforeSave;
        ModHook.OnWakingUpLate -= WakingUp;
    }

    private static void GameLoaded()
    {
        AutoMod.Adapter.LocBridge.Apply();
        Safe("读档", Automation.OnGameLoaded);
    }

    /// <summary>新游戏不触发读档钩子，mod 数据从空开始。</summary>
    private static void NewGame(string scenario)
    {
        AutoMod.Adapter.LocBridge.Apply();
        Safe("新游戏", Automation.OnNewGame);
    }

    /// <summary>晚班：点“结束营业”后、付夜间服务费和夜间充电 / 产水 / 动物结算之前。先发薪，决定谁上岗。</summary>
    private static void GoingSleep() => Safe("晚班", () =>
    {
        AutoMod.App.Payroll.Settle();
        Daily.PlanTonight();
        Automation.RunShift(Shift.Night, false);
    });

    /// <summary>入睡 Late 之后紧接着存档：写回 mod 数据。</summary>
    private static void BeforeSave() => Safe("存档", Automation.BeforeSave);

    /// <summary>早班：醒来后，夜间结算和存档都已完成。</summary>
    private static void WakingUp() => Safe("早班", () =>
    {
        Automation.RunShift(Shift.Morning, false);
    });

    private static void Safe(string what, Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"[Automation] {what}出错: {ex}"); }
    }
}
