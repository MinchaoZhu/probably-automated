using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AutoMod.Domain;

/// <summary>
/// 分段计时：班次执行时各环节累计用了多久、调用了几次，写进日志的一行里（玩家反馈卡顿时能看出慢在哪）。
/// 只在 Start 和 Stop 之间记。环节：act = 游戏执行一次作用 / 触发，scan = 收集全店物品，selector = 筛选器求值，
/// fits / place = 问游戏放不放得下 / 放进去，shop = 收集“全店”，check = 结构检查，profile = 估算工作构成。
/// 环节各计自己的总时间，有包含关系的会重复计（selector 里含 shop）。
/// </summary>
public static class Perf
{
    private static readonly Dictionary<string, (long ticks, int n)> Acc = new();
    private static bool _on;

    public static void Start()
    {
        Acc.Clear();
        _on = true;
    }

    /// <summary>结束并返回摘要（“scan 12ms×3 act 40ms×62”，按用时从多到少，不足 1 ms 的不写）。</summary>
    public static string Stop()
    {
        _on = false;
        var parts = Acc.OrderByDescending(kv => kv.Value.ticks)
            .Select(kv => (kv.Key, ms: kv.Value.ticks * 1000 / Stopwatch.Frequency, kv.Value.n))
            .Where(x => x.ms >= 1).Select(x => $"{x.Key} {x.ms}ms×{x.n}").ToList();
        Acc.Clear();
        return string.Join(" ", parts);
    }

    public static Scope Begin(string name) => _on ? new Scope(name, Stopwatch.GetTimestamp()) : default;

    public readonly struct Scope : IDisposable
    {
        private readonly string _name;
        private readonly long _t0;

        internal Scope(string name, long t0)
        {
            _name = name;
            _t0 = t0;
        }

        public void Dispose()
        {
            if (_name == null || !_on) return;
            Acc.TryGetValue(_name, out var a);
            Acc[_name] = (a.ticks + Stopwatch.GetTimestamp() - _t0, a.n + 1);
        }
    }
}
