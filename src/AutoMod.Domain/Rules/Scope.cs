using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>
/// 定义的范围：对象、挑选器可以是全局的（Owner 为空），也可以只属于一个排班。
/// 只能由窄范围引用宽范围；全局挑选器排在本排班挑选器上方；同一排班能看到的范围内不重名。
/// </summary>
public static class Scope
{
    // ================= 范围与可见性 =================

    /// <summary>定义的所属排班 Id；全局、自定义属性、找不到时为空。</summary>
    public static string OwnerOf(RulesData d, string id) =>
        d.Objects.FirstOrDefault(o => o.Id == id)?.Owner ?? d.Selectors.FirstOrDefault(x => x.Id == id)?.Owner;

    /// <summary>从 view（排班 Id；为空 = 只看全局）能不能看到、引用这个定义。</summary>
    public static bool Visible(RulesData d, string view, string id)
    {
        string owner = OwnerOf(d, id);
        return owner == null || owner == view;
    }

    /// <summary>view 能看到的对象：本排班的在前，全局的在后。</summary>
    public static List<ObjectDef> Objects(RulesData d, string view) =>
        d.Objects.Where(o => view != null && o.Owner == view).Concat(d.Objects.Where(o => o.Owner == null)).ToList();

    /// <summary>view 能看到的挑选器：本排班的在前，全局的在后（各自保持列表顺序）。</summary>
    public static List<SelectorDef> Selectors(RulesData d, string view) =>
        d.Selectors.Where(x => view != null && x.Owner == view).Concat(d.Selectors.Where(x => x.Owner == null)).ToList();

    /// <summary>名称后面带上所属排班，用在排班以外的地方（引用检查的提示）：“满电架（自动换电）”。</summary>
    public static string DisplayName(RulesData d, string id)
    {
        string name = Text.Name(d, id);
        string owner = OwnerOf(d, id);
        var s = owner == null ? null : d.Schedules.FirstOrDefault(x => x.Id == owner);
        return s == null ? name : name + L.T("text.paren", s.Name);
    }

    // ================= 顺序 =================

    /// <summary>
    /// 整理：全局挑选器移到本排班挑选器上方（各自保持原顺序）；所属排班已不存在的定义改成全局（不丢数据）。
    /// 读档后、复制 / 提升之后调用。
    /// </summary>
    public static void Normalize(RulesData d)
    {
        var ids = d.Schedules.Select(s => s.Id).ToHashSet();
        foreach (var o in d.Objects) if (o.Owner != null && !ids.Contains(o.Owner)) o.Owner = null;
        foreach (var x in d.Selectors) if (x.Owner != null && !ids.Contains(x.Owner)) x.Owner = null;
        Calls.DropStaleArgs(d); // 执行流程里覆盖的参数：去掉已经删除、改成全局、类型对不上的
        var ordered = d.Selectors.Where(x => x.Owner == null).Concat(d.Selectors.Where(x => x.Owner != null)).ToList();
        d.Selectors.Clear();
        d.Selectors.AddRange(ordered);
    }

    // ================= 重名 =================

    /// <summary>
    /// 名称冲突：本排班定义不能与全局定义、同一排班的其他本排班定义重名；全局定义不能与任何定义重名。
    /// 对象、挑选器各算一类。self 为正在改名的定义（不和自己比）。没有冲突返回空。
    /// </summary>
    public static string NameConflict(RulesData d, bool selector, string name, string owner, string self = null)
    {
        name = (name ?? "").Trim();
        IEnumerable<(string id, string name, string owner)> defs = selector
            ? d.Selectors.Select(x => (x.Id, x.Name, x.Owner))
            : d.Objects.Select(o => (o.Id, o.Name, o.Owner));
        foreach (var (id, n, o) in defs)
        {
            if (id == self || n != name) continue;
            if (o == null) return L.T(selector ? "scope.err.dup_global_selector" : "scope.err.dup_global_param", n);
            if (owner == null || o == owner)
            {
                string sch = d.Schedules.FirstOrDefault(s => s.Id == o)?.Name ?? "?";
                return L.T(selector ? "scope.err.dup_local_selector" : "scope.err.dup_local_param", sch, n);
            }
        }
        return null;
    }

    // ================= 提升为全局 =================

    /// <summary>提升 id 时要一起提升的本排班定义：它自己和它连带引用的、同一排班的定义（按列表顺序，对象在前）。</summary>
    public static List<string> PromoteSet(RulesData d, string id)
    {
        string owner = OwnerOf(d, id);
        if (owner == null) return new List<string>();
        var closure = Check.Closure(d, new[] { id });
        return d.Objects.Where(o => o.Owner == owner && closure.Contains(o.Id)).Select(o => o.Id)
            .Concat(d.Selectors.Where(x => x.Owner == owner && closure.Contains(x.Id)).Select(x => x.Id)).ToList();
    }

    /// <summary>提升为全局（连同 PromoteSet）。有重名时不改动，返回原因；成功返回空。</summary>
    public static string Promote(RulesData d, string id)
    {
        var set = PromoteSet(d, id);
        if (set.Count == 0) return L.T("scope.err.already_global");
        foreach (var x in set)
        {
            var o = d.Objects.FirstOrDefault(v => v.Id == x);
            var s = d.Selectors.FirstOrDefault(v => v.Id == x);
            string conflict = NameConflict(d, s != null, o?.Name ?? s?.Name, null, x);
            if (conflict != null) return L.T("scope.err.conflict", o?.Name ?? s?.Name, conflict);
        }
        foreach (var o in d.Objects.Where(v => set.Contains(v.Id))) o.Owner = null;
        foreach (var s in d.Selectors.Where(v => set.Contains(v.Id))) s.Owner = null;
        Normalize(d); // 提升的挑选器原来排在全局的下方，稳定整理后正好接在全局组末尾
        return null;
    }

    // ================= 复制 / 删除排班 =================

    /// <summary>复制排班：本排班定义一起复制（新 Id，名称不变），副本的规则和定义改为引用复制出的定义；全局定义共用。</summary>
    public static Schedule CopySchedule(RulesData d, Schedule s, string name)
    {
        var copy = Defs.Clone(s);
        copy.Id = Defs.NewId("sch");
        copy.Name = name;
        foreach (var r in copy.Rules) r.Id = Defs.NewId("r");
        var map = new Dictionary<string, string>();
        string M(string x) => x != null && map.TryGetValue(x, out var y) ? y : x;
        var objs = d.Objects.Where(o => o.Owner == s.Id).ToList();
        var sels = d.Selectors.Where(x => x.Owner == s.Id).ToList();
        foreach (var o in objs) map[o.Id] = Defs.NewId("o");
        foreach (var x in sels) map[x.Id] = Defs.NewId("s");
        foreach (var o in objs)
        {
            var c = Defs.Clone(o);
            c.Id = map[o.Id];
            c.Owner = copy.Id;
            c.Ref = M(c.Ref); // 列表参数的值是本流程筛选器时，改成复制出的那个
            d.Objects.Add(c);
        }
        foreach (var x in sels)
        {
            var c = Defs.Clone(x);
            Defs.MapSelector(c, M);
            c.Id = map[x.Id];
            c.Owner = copy.Id;
            d.Selectors.Add(c);
        }
        foreach (var r in copy.Rules) Defs.MapRule(r, M);
        copy.Created = Defs.Now();
        d.Schedules.Add(copy);
        return copy;
    }

    /// <summary>本排班定义的数量（删除确认框用）。</summary>
    public static int OwnCount(RulesData d, Schedule s) => d.Objects.Count(o => o.Owner == s.Id) + d.Selectors.Count(x => x.Owner == s.Id);

    /// <summary>删除排班和它的本排班定义；返回删掉的定义数。全局定义不受影响。</summary>
    public static int DeleteSchedule(RulesData d, Schedule s)
    {
        int n = OwnCount(d, s);
        d.Objects.RemoveAll(o => o.Owner == s.Id);
        d.Selectors.RemoveAll(x => x.Owner == s.Id);
        d.Schedules.Remove(s);
        return n;
    }

    // ================= 检查 =================

    /// <summary>定义 refs 里越界的引用：全局定义引用了本排班定义，或引用了其他排班的本排班定义。</summary>
    public static IEnumerable<string> BadRefs(RulesData d, string view, IEnumerable<string> refs) =>
        refs.Where(r => !string.IsNullOrEmpty(r) && !Visible(d, view, r)).Distinct()
            .Select(r => view == null ? L.T("scope.err.global_ref_local", DisplayName(d, r)) : L.T("scope.err.cross_ref_local", DisplayName(d, r)));
}
