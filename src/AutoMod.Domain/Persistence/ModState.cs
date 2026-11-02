using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AutoMod.Domain;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Workforce;

namespace AutoMod.Domain.Persistence;

/// <summary>
/// mod 的全部存档数据，序列化成 JSON，由插件存进游戏存档的 mod 数据（ModHelper.SetModData）。
/// 不做存档转换：格式版本不是当前版本的数据整份忽略，下次存档时用新格式覆盖。
/// </summary>
public class ModState : Roster
{
    public const int MaxReports = 20;

    /// <summary>当前存档格式。改动不兼容的结构时加一。</summary>
    public const int Format = 8;

    public int Version { get; set; } = Format;

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

    public string ToJson() => JsonCodec.Serialize(this);

    public static ModState FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ModState();
        try
        {
            var root = JObject.Parse(json);
            var v = root.GetValue("version", System.StringComparison.OrdinalIgnoreCase);
            int version = v != null && v.Type == JTokenType.Integer ? (int)v : 0;
            if (version != Format)
            {
                Log.Info($"[ModState] 存档里是旧格式的 mod 数据（版本 {version}，当前 {Format}），已忽略，下次存档时覆盖");
                return new ModState();
            }
            var st = JsonCodec.Deserialize<ModState>(json) ?? new ModState();
            AutoMod.Domain.Rules.Scope.Normalize(st.Rules); // 挑选器按作用域和依赖排好序
            return st;
        }
        catch (Exception ex)
        {
            Log.Error($"[ModState] 存档数据解析失败，使用空数据: {ex.Message}");
            return new ModState();
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
