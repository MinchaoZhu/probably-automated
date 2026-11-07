using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using AutoMod.Domain;
using AutoMod.Domain.Persistence.Migrations;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Workforce;

namespace AutoMod.Domain.Persistence;

/// <summary>
/// mod 的全部存档数据，序列化成 JSON，由插件存进游戏存档的 mod 数据（ModHelper.SetModData）。
/// 旧格式的数据按 Migrator 升级。读不了的（比当前新、损坏、升级失败）不覆盖：原文留在 Frozen 里原样写回，学徒暂停工作。
/// </summary>
public class ModState : Roster
{
    public const int MaxReports = 20;

    /// <summary>当前存档格式。什么时候加一见 Migrator。</summary>
    public const int Format = 8;

    public int Version { get; set; } = Format;

    /// <summary>写这份数据的 mod 版本（只是记一下，排查问题用）。</summary>
    public string ModVersion { get; set; }

    /// <summary>读不了的数据原文：存档时原样写回，不跑班次。</summary>
    [JsonIgnore] public string Frozen { get; private set; }

    /// <summary>Frozen 的格式；比当前新时才有，损坏的为 0。</summary>
    [JsonIgnore] public int FrozenFormat { get; private set; }

    /// <summary>刚升级过：升级前的原文，要另外存一份；没升级为空。</summary>
    [JsonIgnore] public string Backup { get; private set; }

    /// <summary>对象、挑选器、自定义属性、排班。</summary>
    public AutoMod.Domain.Rules.RulesData Rules { get; set; } = new();
    public List<ShiftReport> Reports { get; set; } = new();


    /// <summary>上一次执行早班是第几天（读档时判断是否需要补跑早班）。</summary>
    public int LastMorningDay { get; set; } = -1;

    /// <summary>店里那份工作日报（昨晚+今晨的合并汇总；读报纸时用）；没发过为空。</summary>
    public DailyEdition Daily { get; set; }

    /// <summary>入睡时定下：明早早班结束后发一份新的工作日报。</summary>
    public bool DailyPlanned { get; set; }

    /// <summary>执行时游戏要打开界面、学徒做不了的（动作|种类）：记住不再尝试，玩家可以在报告页清除。</summary>
    public List<string> NeedsPlayer { get; set; } = new();

    /// <summary>每晚付的工资（报告页显示每晚和近 7 天合计），只留最近 30 晚。</summary>
    public List<WageNight> Wages { get; set; } = new();

    public void AddWages(int day, int amount, int strikers)
    {
        Wages.RemoveAll(w => w.Day == day);
        Wages.Add(new WageNight { Day = day, Amount = amount, Strikers = strikers });
        if (Wages.Count > 30) Wages.RemoveRange(0, Wages.Count - 30);
    }

    /// <summary>第 fromDay 天（含）以后付的工资合计。</summary>
    public int WagesSince(int fromDay) => Wages.Where(w => w.Day >= fromDay).Sum(w => w.Amount);


    public void AddReport(ShiftReport r)
    {
        Reports.Add(r);
        if (Reports.Count > MaxReports) Reports.RemoveRange(0, Reports.Count - MaxReports);
    }

    public string ToJson() => Frozen ?? JsonCodec.Serialize(this);

    public static ModState FromJson(string json) => FromJson(json, Migrator.Steps, Format);

    public static ModState FromJson(string json, IReadOnlyList<Migrator.Step> steps, int format)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ModState();
        try
        {
            var root = Migrator.Parse(json);
            int version = Migrator.Version(root, "version", 0);
            if (version < Migrator.First)
            {
                Log.Info($"[ModState] 存档里是发布前的 mod 数据（版本 {version}），已忽略，下次存档时覆盖");
                return new ModState();
            }
            if (version > format)
            {
                Log.Warn($"[ModState] 存档里的 mod 数据比当前版本新（版本 {version}，当前 {format}），保留原文，学徒暂停工作");
                return new ModState { Frozen = json, FrozenFormat = version };
            }
            string current = json;
            if (version < format)
            {
                Migrator.Run(root, version, format, steps);
                current = Migrator.Write(root);
                Log.Info($"[ModState] mod 数据已从版本 {version} 升级到 {format}");
            }
            var st = JsonCodec.Deserialize<ModState>(current) ?? new ModState();
            if (version < format) st.Backup = json;
            AutoMod.Domain.Rules.Scope.Normalize(st.Rules); // 挑选器按作用域和依赖排好序
            return st;
        }
        catch (Exception ex)
        {
            Log.Error($"[ModState] 存档数据读取失败，保留原文，学徒暂停工作: {ex.Message}");
            return new ModState { Frozen = json };
        }
    }
}

/// <summary>某一晚付的工资。</summary>
public class WageNight
{
    public int Day { get; set; }
    public int Amount { get; set; }
    public int Strikers { get; set; }
}
