using System;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>规则语言的通用词汇：系统变量、运算符、统计函数、比较。类别、属性、预设、操作在 Catalog。</summary>
public static class Vocab
{
    // ---------- 系统变量、运算符 ----------

    public static (string id, string name)[] SysVars => new[]
    {
        (SysVar.ApLeft, SysName(SysVar.ApLeft)),
        (SysVar.Shift, SysName(SysVar.Shift)),
        (SysVar.Day, SysName(SysVar.Day)),
    };

    public static string SysName(string id) => id switch
    {
        SysVar.ApLeft => L.T("vocab.sys.ap_left"),
        SysVar.Shift => L.T("vocab.sys.shift"),
        SysVar.Day => L.T("vocab.sys.day"),
        _ => id,
    };

    /// <summary>系统变量的简短名称（用于表达式和规则卡片，不含括号说明）。</summary>
    public static string SysShortName(string id) => id switch
    {
        SysVar.ApLeft => L.T("vocab.sys.ap_left"),
        SysVar.Shift => L.T("vocab.sys.shift.short"),
        SysVar.Day => L.T("vocab.sys.day"),
        _ => SysName(id),
    };

    public static readonly string[] CmpOps = { ">", ">=", "<", "<=", "=" };
    public static readonly string[] ArithOps = { "+", "-", "*", "/" };

    public static string OpText(string op) => op switch
    {
        ">=" => "≥", "<=" => "≤", "!=" => "≠", "*" => "×", "/" => "÷", "-" => "−", _ => op,
    };

    public static (string id, string name)[] AggFns => new[]
    {
        (AggFn.Count, AggName(AggFn.Count)),
        (AggFn.Min, AggName(AggFn.Min)),
        (AggFn.Max, AggName(AggFn.Max)),
        (AggFn.Avg, AggName(AggFn.Avg)),
        (AggFn.Sum, AggName(AggFn.Sum)),
    };

    public static string AggName(string fn) => fn switch
    {
        AggFn.Count => L.T("vocab.agg.count"),
        AggFn.Min => L.T("vocab.agg.min"),
        AggFn.Max => L.T("vocab.agg.max"),
        AggFn.Avg => L.T("vocab.agg.avg"),
        AggFn.Sum => L.T("vocab.agg.sum"),
        _ => fn,
    };

    public static bool Compare(double? a, string op, double? b)
    {
        if (a is not { } x || b is not { } y || double.IsNaN(x) || double.IsNaN(y)) return false;
        const double eps = 1e-9;
        return op switch
        {
            ">" => x > y + eps, ">=" => x >= y - eps, "<" => x < y - eps, "<=" => x <= y + eps, "=" => Math.Abs(x - y) <= eps,
            _ => false,
        };
    }
}
