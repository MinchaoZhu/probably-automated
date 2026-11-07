using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Persistence;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;
using AutoMod;
using AutoMod.Adapter;
using AutoMod.Domain;

namespace AutoMod.App;

/// <summary>
/// 自动化总控：读档时载入 mod 数据、存档前写回、班次触发。
/// </summary>
public static class Automation
{
    public static ModState State { get; private set; } = new();
    public static ShiftReport LastReport { get; private set; }

    /// <summary>真实执行完一个班次后触发（界面用来弹出报告）。</summary>
    public static event Action<ShiftReport> ShiftCompleted;

    /// <summary>班次报告的文字记录：mod 数据文件夹（persistentDataPath/ModData/probably_automated/）里，不需要文件能力。</summary>
    private const string ReportFile = "reports.log";
    private const int ReportFileMax = 256 * 1024;

    /// <summary>mod 数据在存档里的 key（游戏存成 "probably_automated:state"）。</summary>
    private const string DataKey = "state";

    /// <summary>上一次升级前的数据原文：升级出了问题时还能找回来。</summary>
    private const string BackupKey = "state.backup";

    // ---------- 存读档 ----------

    public static void OnGameLoaded()
    {
        var store = GameApi.Store;
        State = ModState.FromJson(ModHelper.GetModData(ModInfo.Id, DataKey));
        if (State.Backup != null) ModHelper.SetModData(ModInfo.Id, BackupKey, State.Backup);
        Unlock.SyncPrice();

        // 学徒做不了的（要玩家在界面里操作的）记录：存在 mod 数据里，读档恢复
        GameWorld.LoadNeedsPlayer(State.NeedsPlayer);
        GameWorld.NeedsPlayerAdded = k => { if (!State.NeedsPlayer.Contains(k)) State.NeedsPlayer.Add(k); };
        // 机器格子的标题（“基础水箱”“种子输入”……）：卡片和挑选器说明里要用
        try { foreach (var it in GameApi.CollectAll()) if (it.contentWindow?.childElement is MachineryPanelRow) MachineSlots.Of(it); } catch { }
        var r = State.Rules;
        Log.Info($"[Automation] 读档：对象 {r.Objects.Count} 个，挑选器 {r.Selectors.Count} 个，流程 {r.Schedules.Count} 个，报告 {State.Reports.Count} 份");

        // 游戏在“醒来”之前自动存档，所以早上读档时早班一定还没执行：有已上岗的早班学徒就补跑
        try
        {
            if (store != null && store.storeState == PlayerStore.StoreState.MORNING
                && State.Apprentices.Any(a => a.OnDuty && a.DutiesIn(Shift.Morning).Any())
                && State.LastMorningDay != StoreStation.GetDayCounter())
            {
                Log.Info("[Automation] 读档时早班尚未执行，补跑早班");
                RunShift(Shift.Morning, false);
            }
        }
        catch (Exception ex) { Log.Warn($"[Automation] 补跑早班失败: {ex.Message}"); }
    }

    /// <summary>新游戏：mod 数据从空开始（没解锁，什么都不出现）。</summary>
    public static void OnNewGame()
    {
        State = new ModState();
        LastReport = null;
        LastRun = null;
    }

    public static void BeforeSave()
    {
        var store = GameApi.Store;
        if (store == null) return;
        State.ModVersion = ModInfo.Version;
        ModHelper.SetModData(ModInfo.Id, DataKey, State.ToJson());
    }

    // ---------- 班次 ----------

    /// <summary>
    /// 当班学徒（这个班次有流程要执行的）：真实执行用“已上岗”（勾选了且入睡时付得起工资）的；
    /// 试运行用“已勾选”的副本，AP 按当天刚开始算。
    /// </summary>
    public static List<Apprentice> WorkersFor(string shift, bool dryRun) =>
        dryRun
            ? State.Apprentices.Where(a => a.Paid && a.Duties.Count > 0).Select(Fresh).ToList()
            : State.Apprentices.Where(a => a.OnDuty && a.DutiesIn(shift).Any()).ToList();

    private static Apprentice Fresh(Apprentice a)
    {
        var c = JsonCodec.Clone(a);
        c.ApUsed = 0;
        return c;
    }

    /// <summary>最近一次真实执行的完整报告（界面显示规则级结果用，不存档）。</summary>
    public static RunReport LastRun { get; private set; }

    /// <summary>清除“要玩家操作”的记录：下个班次这些动作会再试一次。</summary>
    public static void ClearNeedsPlayer()
    {
        State.NeedsPlayer.Clear();
        GameWorld.LoadNeedsPlayer(null);
    }

    /// <summary>
    /// 预估下一个班次：在店铺副本（模拟世界）上，用已勾选学徒的副本执行一次，不影响真实店铺和学徒。
    /// 当天的 AP 从晚班开始算：预估早班时先在副本上跑一遍晚班，早班用剩下的 AP。
    /// </summary>
    public static RunReport Preview(string shift)
    {
        try
        {
            var sim = SimImport.FromGame(State.Rules);
            var workers = WorkersFor(shift, dryRun: true);
            var interp = new Interpreter(sim, State.Rules);
            if (shift == Shift.Morning) interp.RunShift(Shift.Night, workers.Where(a => a.DutiesIn(Shift.Night).Any()).ToList(), applyXp: false);
            return interp.RunShift(shift, workers.Where(a => a.DutiesIn(shift).Any()).ToList(), applyXp: false);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Automation] 预估出错: {ex.Message}");
            return new RunReport { Shift = shift };
        }
    }

    public static ShiftReport RunShift(string shift, bool dryRun)
    {
        if (State.Frozen != null || (!Settings.AutomationEnabled || !Unlock.IsUnlocked) && !dryRun) return null;
        ShiftReport report;
        var workers = WorkersFor(shift, dryRun);
        if (dryRun) workers = workers.Where(a => a.DutiesIn(shift).Any()).ToList();
        try
        {
            // 班次统计：早班开始时（夜间结算之后）记一份，晚班结束时（夜间结算之前）记一份
            IWorld world = dryRun ? SimImport.FromGame(State.Rules) : new GameWorld();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var run = new Interpreter(world, State.Rules) { UpdateProfiles = !dryRun }.RunShift(shift, workers, applyXp: !dryRun);
            // 用时和店里的物品数写进日志：玩家反馈卡顿时，从 Player.log 能看出店有多大、班次用了多久
            string items = world is GameWorld gw ? $"，店里 {gw.ItemCount} 件物品" : "";
            Log.Info($"[Automation] {(dryRun ? "试运行" : "执行")}{shift} 用时 {sw.ElapsedMilliseconds} ms，完成 {run.Groups.Sum(g => g.Rules.Sum(r => r.Done.Values.Sum()))} 件{items}");
            report = ShiftReport.From(run, dryRun, workers);
            if (!dryRun)
            {
                LastRun = run;
                // 当天 = 晚班 + 第二天早班：早班结束后这一天结束
                if (shift == Shift.Morning) foreach (var w in State.Apprentices) w.OnDuty = false;
                if (shift == Shift.Morning) State.LastMorningDay = report.Day;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Automation] {Shift.DisplayName(shift)}执行出错: {ex}");
            return null;
        }

        string detail = report.Detail();
        Log.Info("[Automation]\n" + detail);
        AppendReportFile(detail);

        if (!dryRun)
        {
            LastReport = report;
            State.AddReport(report);
            // 晚班的摘要写进夜间日志，第二天早报里显示；早班的结果用界面展示
            if (shift == Shift.Night && report.Entries.Count > 0)
                try { GameApi.Store?.AddNightLog(report.Summary()); } catch { }
            try { ShiftCompleted?.Invoke(report); } catch (Exception ex) { Log.Warn($"[Automation] 班次报告显示失败: {ex.Message}"); }
        }
        return report;
    }

    private static void AppendReportFile(string text)
    {
        try
        {
            string old = ModHelper.ReadModDataFileText(ModInfo.Id, ReportFile) ?? "";
            if (old.Length > ReportFileMax) old = old.Substring(old.Length - ReportFileMax / 2);
            ModHelper.WriteModDataFileText(ModInfo.Id, ReportFile, old + text + Environment.NewLine);
        }
        catch (Exception ex) { Log.Warn($"[Automation] 写报告文件失败: {ex.Message}"); }
    }
}
