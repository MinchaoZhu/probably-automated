using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 一个排班导出成的一段文字：排班本身、它用到的参数 / 筛选器 / 自定义属性。设备名就是名字，
/// 换一家店照样用（那家店里给东西起同样的名字就行）。不含学徒、熟练度、报告。
/// </summary>
public class SchedulePack
{
    public const int CurrentFormat = 3;

    public int Format { get; set; } = CurrentFormat;

    /// <summary>导出时的 mod 版本（只是记一下）。</summary>
    public string Version { get; set; }

    public Schedule Schedule { get; set; }
    public List<ObjectDef> Objects { get; set; } = new();
    public List<SelectorDef> Selectors { get; set; } = new();
    public List<PropertyDef> Properties { get; set; } = new();
}

/// <summary>导入失败（文字不是排班、格式版本不对……），Message 是给玩家看的一句话。</summary>
public class TransferException : Exception
{
    public TransferException(string message) : base(message) { }
}

/// <summary>排班的导出 / 导入（走剪贴板，文字是开头带一个标记的单行 JSON）。</summary>
public static class Transfer
{
    public static string Marker => L.T("transfer.marker");

    /// <summary>导出排班 s。</summary>
    public static string Export(RulesData d, Schedule s, string version)
    {
        var used = Check.Closure(d, s.Rules.SelectMany(Check.RuleRefs));
        bool Take(string id, string owner) => used.Contains(id) || owner == s.Id;
        var pack = new SchedulePack { Version = version, Schedule = Defs.Clone(s) };
        pack.Schedule.Profile = null;
        pack.Objects.AddRange(d.Objects.Where(o => Take(o.Id, o.Owner)).Select(Defs.Clone));
        pack.Selectors.AddRange(d.Selectors.Where(x => Take(x.Id, x.Owner)).Select(Defs.Clone));
        pack.Properties.AddRange(d.Properties.Where(p => used.Contains(p.Id)).Select(Defs.Clone));
        return Marker + JsonCodec.Serialize(pack);
    }

    /// <summary>
    /// 导入：已有同名、内容一样的共用定义直接用；其余换新 Id，参数、筛选器归新排班（不进共用），
    /// 自定义属性加到共用里；重名的加“（2）”。返回新排班。
    /// </summary>
    public static Schedule Import(RulesData d, string text)
    {
        var pack = Parse(text);
        var s = pack.Schedule;
        var map = new Dictionary<string, string>();
        string M(string x) => x != null && map.TryGetValue(x, out var y) ? y : x;
        string newSch = Defs.NewId("sch");
        foreach (var o in pack.Objects) map[o.Id] = Defs.NewId("o");
        foreach (var x in pack.Selectors) map[x.Id] = Defs.NewId("s");
        foreach (var p in pack.Properties) map[p.Id] = Defs.NewId("p");

        foreach (var o in pack.Objects)
        {
            string old = o.Id;
            // 已有同名同值的共用参数：直接用
            if (d.Objects.FirstOrDefault(x => x.Owner == null && x.Name == o.Name && x.Value == o.Value) is { } same) { map[old] = same.Id; continue; }
            o.Id = map[old];
            o.Owner = newSch;
            o.Name = Unique(d, false, o.Name, newSch);
            d.Objects.Add(o);
        }
        foreach (var p in pack.Properties)
        {
            string old = p.Id;
            Defs.MapExpr(p.Formula, M);
            // 已有同名、同公式的自定义属性：直接用（在同一个存档里导回来时不重复一份）
            if (d.Properties.FirstOrDefault(x => x.Name == p.Name && x.ItemKind == p.ItemKind && Defs.Json(x.Formula) == Defs.Json(p.Formula)) is { } same) { map[old] = same.Id; continue; }
            p.Id = map[old];
            p.Name = Defs.UniqueName(d.Properties.Select(x => x.Name), p.Name);
            d.Properties.Add(p);
        }
        foreach (var x in pack.Selectors)
        {
            string old = x.Id;
            x.Where ??= new CondGroup();
            Defs.MapSelector(x, M);
            // 已有同名、定义一样（引用换算之后）的共用筛选器：直接用
            if (d.Selectors.FirstOrDefault(y => y.Owner == null && y.Name == x.Name && Same(y, x)) is { } same) { map[old] = same.Id; continue; }
            x.Id = map[old];
            x.Owner = newSch;
            x.Name = Unique(d, true, x.Name, newSch);
            d.Selectors.Add(x);
        }
        s.Id = newSch;
        s.Name = Defs.UniqueName(d.Schedules.Select(x => x.Name), s.Name);
        s.Profile = null;
        s.Created = Defs.Now();
        foreach (var r in s.Rules)
        {
            r.Id = Defs.NewId("r");
            Defs.MapRule(r, M);
        }
        d.Schedules.Add(s);
        Scope.Normalize(d);
        return s;
    }

    /// <summary>两个筛选器的定义一样（不看 Id、所属、名字）。</summary>
    private static bool Same(SelectorDef a, SelectorDef b)
    {
        var x = Defs.Clone(a); var y = Defs.Clone(b);
        x.Id = y.Id = ""; x.Owner = y.Owner = null; x.Name = y.Name = "";
        return Defs.Json(x) == Defs.Json(y);
    }

    private static string Unique(RulesData d, bool selector, string name, string owner)
    {
        string n = name;
        for (int i = 2; Scope.NameConflict(d, selector, n, owner) != null; i++) n = L.T("transfer.dup_suffix", name, i);
        return n;
    }

    private static SchedulePack Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new TransferException(L.T("transfer.err.clipboard_empty"));
        int brace = text.IndexOf('{');
        if (brace < 0) throw new TransferException(L.T("transfer.err.not_a_routine"));
        SchedulePack pack;
        try { pack = JsonCodec.Deserialize<SchedulePack>(text[brace..].Trim()); }
        catch { throw new TransferException(L.T("transfer.err.not_a_routine")); }
        if (pack?.Schedule == null) throw new TransferException(L.T("transfer.err.not_a_routine"));
        if (pack.Format > SchedulePack.CurrentFormat) throw new TransferException(L.T("transfer.err.newer_version"));
        if (pack.Format < SchedulePack.CurrentFormat) throw new TransferException(L.T("transfer.err.older_version"));
        pack.Schedule.Rules ??= new List<WorkRule>();
        pack.Schedule.Notepad ??= new List<NoteDef>();
        pack.Objects ??= new List<ObjectDef>();
        pack.Selectors ??= new List<SelectorDef>();
        pack.Properties ??= new List<PropertyDef>();
        return pack;
    }
}
