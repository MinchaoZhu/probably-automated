using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoMod.Domain.Persistence.Migrations;

/// <summary>
/// mod 数据的格式升级：存档和导出的流程按顺序走完 Steps，再读成当前模型。
///
/// 什么时候要加一步：改名、删字段、改含义、拆分结构。只加可选字段、默认值等于旧行为的不用，也不升格式号
/// （升了以后旧版本 mod 读不了这份数据）。
///
/// 加一步：写 VnToVm.cs（static void，参数是 JObject），加进 Steps，ModState.Format 加一，
/// 把这一版真实写出的数据存成 tests/AutoMod.Domain.Tests/fixtures/state_vN.json。
/// 步骤只改 JSON，不引用当前模型（模型以后还会变，步骤发布后就不再改）；模型里删掉的东西留在步骤里，以字段名的形式。
/// </summary>
public static class Migrator
{
    /// <summary>发布过的最早格式（1.0.0）。更早的数据没有发布过，不升级。</summary>
    public const int First = 8;

    /// <summary>从 From 升到 From + 1。State 改整份存档；Rules 改规则部分，存档和导出的流程共用，二者都可以为空。</summary>
    public sealed class Step
    {
        public int From;
        public Action<JObject> State;
        public Action<JObject> Rules;
    }

    public static readonly IReadOnlyList<Step> Steps = Array.Empty<Step>();

    private static readonly JsonSerializerSettings Raw = new() { DateParseHandling = DateParseHandling.None };

    /// <summary>读成 JSON 树：文字保持原样（默认设置会把像日期的字符串改掉）。</summary>
    public static JObject Parse(string json) => JsonConvert.DeserializeObject<JObject>(json, Raw);

    public static string Write(JObject root) => root.ToString(Formatting.None);

    /// <summary>整数字段（大小写不敏感）；没有时给 fallback。</summary>
    public static int Version(JObject root, string key, int fallback)
    {
        var v = root.GetValue(key, StringComparison.OrdinalIgnoreCase);
        return v != null && v.Type == JTokenType.Integer ? (int)v : fallback;
    }

    /// <summary>升级整份存档，version 跟着改。缺一步时抛异常。</summary>
    public static void Run(JObject state, int from, int to, IReadOnlyList<Step> steps)
    {
        for (int v = from; v < to; v++)
        {
            var step = StepFrom(steps, v);
            step.State?.Invoke(state);
            if (state["rules"] is JObject rules) step.Rules?.Invoke(rules);
            state["version"] = v + 1;
        }
    }

    /// <summary>升级导出的流程：它的规则部分拼成存档里 rules 的形状，走同样的步骤。</summary>
    public static void RunPack(JObject pack, int from, int to, IReadOnlyList<Step> steps)
    {
        var rules = new JObject
        {
            ["objects"] = pack["objects"] ?? new JArray(),
            ["selectors"] = pack["selectors"] ?? new JArray(),
            ["properties"] = pack["properties"] ?? new JArray(),
            ["schedules"] = new JArray(pack["schedule"]),
        };
        for (int v = from; v < to; v++) StepFrom(steps, v).Rules?.Invoke(rules);
        pack["objects"] = rules["objects"];
        pack["selectors"] = rules["selectors"];
        pack["properties"] = rules["properties"];
        pack["schedule"] = rules["schedules"]?[0];
        pack["dataFormat"] = to;
    }

    private static Step StepFrom(IReadOnlyList<Step> steps, int from) =>
        steps.FirstOrDefault(s => s.From == from) ?? throw new InvalidOperationException($"缺少从格式 {from} 升级的步骤");
}
