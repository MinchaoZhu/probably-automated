using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>表达式的使用位置（决定可用的项）。</summary>
public enum ExprScope
{
    Property,   // 自定义属性的公式：数值、常量、自身属性
    Condition,  // 挑选器条件的右侧：再加上方挑选器的统计值
    Take,       // 挑选器的数量 N：数值、常量、上方挑选器的统计值
    Runtime,    // 执行前提、记录：数值、常量、任意挑选器、记事本、系统变量
}

/// <summary>静态检查与引用关系。只看定义和绑定，不预演世界状态。</summary>
public static class Check
{
    // ================= 引用关系 =================

    public static IEnumerable<string> ExprRefs(Expr e) =>
        e?.Terms?.Where(t => t.Kind is TermKind.Const or TermKind.Stat or TermKind.Own).Select(t => t.Ref) ?? Enumerable.Empty<string>();

    public static IEnumerable<string> CondRefs(Cond c)
    {
        if (c == null) yield break;
        if (c.Group != null) { foreach (var x in GroupRefs(c.Group)) yield return x; yield break; }
        if (!string.IsNullOrEmpty(c.Prop)) yield return c.Prop;
        foreach (var x in ExprRefs(c.Right)) yield return x;
    }

    public static IEnumerable<string> GroupRefs(CondGroup g) => g?.Items?.SelectMany(CondRefs) ?? Enumerable.Empty<string>();

    /// <summary>挑选器直接引用的定义（对象、挑选器、属性）。</summary>
    public static IEnumerable<string> SelectorRefs(SelectorDef s)
    {
        foreach (var f in s.From) yield return f;
        foreach (var x in GroupRefs(s.Where)) yield return x;
        if (!string.IsNullOrEmpty(s.Minus)) yield return s.Minus;
        if (s.Sort != null) yield return s.Sort.Prop;
        if (s.Take != null) foreach (var x in ExprRefs(s.Take.N)) yield return x;
        if (s.Agg != null) yield return s.Agg.Prop;
    }

    /// <summary>挑选器引用的其他挑选器（依赖顺序检查用）。</summary>
    public static IEnumerable<string> SelectorDeps(RulesData d, SelectorDef s) =>
        SelectorRefs(s).Where(id => d.Selectors.Any(x => x.Id == id)).Distinct();

    public static IEnumerable<string> RuleRefs(WorkRule r)
    {
        foreach (var p in r.Pre)
        {
            if (p.RefKind == PreRef.Selector) yield return p.Ref;
            foreach (var x in ExprRefs(p.Right)) yield return x;
            if (p.Where != null) foreach (var x in CondRefs(p.Where)) yield return x;
        }
        foreach (var a in r.Actions)
        {
            foreach (var id in new[] { a.List, a.To, a.Tool })
                if (!string.IsNullOrEmpty(id) && !Fixtures.Is(id)) yield return id;
            foreach (var x in ExprRefs(a.Value)) yield return x;
            foreach (var x in Calls.ArgRefs(a)) yield return x;
        }
    }

    /// <summary>连带引用：挑选器、自定义属性引用的定义也算进来。</summary>
    public static HashSet<string> Closure(RulesData d, IEnumerable<string> ids)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>(ids.Where(i => !string.IsNullOrEmpty(i)));
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id)) continue;
            var s = d.Selectors.FirstOrDefault(x => x.Id == id);
            if (s != null) foreach (var x in SelectorRefs(s)) if (!string.IsNullOrEmpty(x)) stack.Push(x);
            var p = d.Properties.FirstOrDefault(x => x.Id == id);
            if (p != null) foreach (var x in ExprRefs(p.Formula)) if (!string.IsNullOrEmpty(x)) stack.Push(x);
            if (d.Objects.FirstOrDefault(x => x.Id == id) is { IsRef: true, Ref: { Length: > 0 } r }) stack.Push(r); // 设备 / 列表参数的值
        }
        return seen;
    }

    /// <summary>哪些规则（所有排班）连带引用了这个定义。</summary>
    public static List<(Schedule s, WorkRule r)> UsedBy(RulesData d, string id) =>
        d.Schedules.SelectMany(s => s.Rules.Select(r => (s, r))).Where(x => Closure(d, RuleRefs(x.r)).Contains(id)).ToList();

    /// <summary>任务的下拉框直接选的定义（执行条件和动作的对象、目标、工具），这些位置都能打开编辑。</summary>
    public static IEnumerable<string> PickerRefs(WorkRule r)
    {
        foreach (var p in r.Pre)
            if (p.RefKind == PreRef.Selector && !string.IsNullOrEmpty(p.Ref)) yield return p.Ref;
        foreach (var a in r.Actions)
        {
            if (a.Verb != ActVerb.Note && !string.IsNullOrEmpty(a.List)) yield return a.List;
            if (a.Verb is ActVerb.Move or ActVerb.Swap && !string.IsNullOrEmpty(a.To)) yield return a.To;
            if (a.Verb == ActVerb.Use && !string.IsNullOrEmpty(a.Tool) && !Fixtures.Is(a.Tool)) yield return a.Tool;
            if (a.Verb == ActVerb.Call) foreach (var x in Calls.ArgRefs(a)) yield return x;
        }
    }

    /// <summary>所有直接引用这个定义的地方（删除前检查用）：规则、挑选器、属性。</summary>
    public static List<string> Referrers(RulesData d, string id)
    {
        var list = new List<string>();
        foreach (var s in d.Schedules)
            foreach (var r in s.Rules)
                if (RuleRefs(r).Contains(id)) list.Add($"{s.Name} · {r.Name}");
        foreach (var x in d.Selectors)
            if (x.Id != id && SelectorRefs(x).Contains(id)) list.Add(L.T("check.ref.selector", x.Name));
        foreach (var p in d.Properties)
            if (p.Id != id && ExprRefs(p.Formula).Contains(id)) list.Add(L.T("check.ref.prop", p.Name));
        foreach (var o in d.Objects)
            if (o.Id != id && o.IsRef && o.Ref == id) list.Add(L.T("check.ref.param", o.Name));
        return list;
    }

    /// <summary>按给定顺序，第一个反向引用（A 引用了排在它下方的 B）；没有则为空。</summary>
    public static (SelectorDef a, SelectorDef b)? FirstBackRef(RulesData d, IList<SelectorDef> order)
    {
        for (int i = 0; i < order.Count; i++)
            foreach (var dep in SelectorDeps(d, order[i]))
            {
                int j = -1;
                for (int k = 0; k < order.Count; k++) if (order[k].Id == dep) { j = k; break; }
                if (j >= i) return (order[i], order[j]);
            }
        return null;
    }

    // ================= 定义检查 =================

    /// <summary>这些引用里用到、但店里还没有东西起这个名字的设备名（不算错误：任务照常执行，只是找不到东西）。</summary>
    public static List<string> MissingNames(RulesData d, IEnumerable<string> refs, IWorld w) =>
        Closure(d, refs).Where(Places.IsName).Select(Places.NameOf).Distinct()
            .Where(n => w != null && w.Named(n).Count == 0).ToList();

    public static List<string> ExprErrors(RulesData d, Expr e, ExprScope scope, Schedule s = null, int selectorIndex = int.MaxValue)
    {
        var errs = new List<string>();
        if (e?.Terms == null || e.Terms.Count == 0) { errs.Add(L.T("check.err.expr_empty")); return errs; }
        foreach (var t in e.Terms)
        {
            switch (t.Kind)
            {
                case TermKind.Num: break;
                case TermKind.Const:
                    var o = d.Objects.FirstOrDefault(x => x.Id == t.Ref);
                    if (o == null) errs.Add(L.T("check.err.expr_const_missing"));
                    else if (o.IsRef) errs.Add(L.T("check.err.expr_const_not_number", o.Name));
                    break;
                case TermKind.Own:
                    if (scope is ExprScope.Take or ExprScope.Runtime) errs.Add(L.T("check.err.expr_own_scope"));
                    else if (string.IsNullOrEmpty(t.Ref)) errs.Add(L.T("check.err.expr_empty"));
                    break;
                case TermKind.Stat:
                    if (scope == ExprScope.Property) { errs.Add(L.T("check.err.expr_sel_custom_prop")); break; }
                    if (d.Selectors.All(x => x.Id != t.Ref)) errs.Add(L.T("check.err.expr_sel_missing"));
                    break;
                case TermKind.Note:
                    if (scope != ExprScope.Runtime) errs.Add(L.T("check.err.expr_note_scope"));
                    else if (s?.Notepad.Any(n => n.Id == t.Ref) != true) errs.Add(L.T("check.err.expr_note_missing"));
                    break;
                case TermKind.Sys:
                    if (scope != ExprScope.Runtime) errs.Add(L.T("check.err.expr_sys_scope"));
                    else if (Vocab.SysVars.All(v => v.id != t.Ref)) errs.Add(L.T("check.err.expr_sys_missing"));
                    break;
            }
        }
        return errs.Distinct().ToList();
    }

    public static List<string> PropertyErrors(RulesData d, PropertyDef p)
    {
        var e = new List<string>();
        foreach (var t in p.Formula?.Terms ?? new List<Term>())
            if (t.Kind == TermKind.Own && d.Properties.Any(x => x.Id == t.Ref)) e.Add(L.T("check.err.prop_cycle"));
        e.AddRange(ExprErrors(d, p.Formula, ExprScope.Property));
        e.AddRange(Scope.BadRefs(d, null, ExprRefs(p.Formula))); // 自定义属性只有全局
        return e.Distinct().ToList();
    }

    public static List<string> SelectorErrors(RulesData d, SelectorDef s)
    {
        var e = new List<string>();
        int idx = d.Selectors.IndexOf(s);
        if (idx < 0) idx = d.Selectors.Count;
        if (s.From.Count == 0) e.Add(L.T("check.err.sel_from_empty"));
        foreach (var f in s.From)
        {
            if (Places.IsShop(f) || Places.IsDevice(f) || Defs.RefParam(d, f) != null) continue;
            if (d.Objects.Any(o => o.Id == f)) { e.Add(L.T("check.err.sel_from_param")); continue; }
            int j = d.Selectors.FindIndex(x => x.Id == f);
            if (j < 0) e.Add(L.T("check.err.sel_from_missing"));
            else if (d.Selectors[j].IsNumber) e.Add(L.T("check.err.sel_from_number"));
        }
        if (!string.IsNullOrEmpty(s.Minus))
        {
            if (Defs.Selector(d, s.Minus) is not { IsNumber: false }) e.Add(L.T("check.err.sel_minus_missing"));
        }
        // 筛选器之间可以按任意顺序引用，只是不能绕回自己
        if (Closure(d, SelectorRefs(s).Where(x => x != null)).Contains(s.Id)) e.Add(L.T("check.err.sel_cycle"));
        foreach (var c in AllConds(s.Where))
        {
            if (c.Preset != null) { if (Catalog.Preset(c.Preset) == null) e.Add(L.T("check.err.sel_preset_missing")); continue; }
            if (c.Group != null) continue;
            if (string.IsNullOrEmpty(c.Prop)) e.Add(L.T("check.err.sel_prop_empty"));
            if (c.Text == null) e.AddRange(ExprErrors(d, c.Right, ExprScope.Condition, null, idx)); // 文字比较（标签文字）没有右边的表达式
        }
        if (s.Take != null) e.AddRange(ExprErrors(d, s.Take.N, ExprScope.Take, null, idx));
        if (s.IsNumber && s.Agg?.Fn != AggFn.Count && string.IsNullOrEmpty(s.Agg?.Prop)) e.Add(L.T("check.err.sel_agg_prop_empty"));
        e.AddRange(Scope.BadRefs(d, s.Owner, SelectorRefs(s)));
        return e.Distinct().ToList();
    }

    public static IEnumerable<Cond> AllConds(CondGroup g)
    {
        if (g?.Items == null) yield break;
        foreach (var c in g.Items)
        {
            yield return c;
            if (c.Group != null) foreach (var x in AllConds(c.Group)) yield return x;
        }
    }

    // ================= 规则检查 =================

    private static bool IsContainerRef(RulesData d, string id, IWorld w = null)
    {
        if (string.IsNullOrEmpty(id)) return false;
        // 设备名：店里有叫这个名字的东西时，要是容器（给鼠、水桶起的名字不能当放入的目标）
        if (Places.IsDevice(id)) return w == null || Eval.Device(w, id) is not { Count: > 0 } named || named.All(x => Catalog.Matches(w, x, Catalog.Container));
        var s = Defs.Selector(d, id);
        if (s == null || s.IsNumber) return false;
        if (Defs.RefParam(d, id) is { } param) return Defs.RefParam(d, param.Ref) == null && IsContainerRef(d, param.Ref, w); // 看它的值
        // 没有世界时（新建示例、单元测试）具体种类认不出是不是容器，不当错误
        return Eval.YieldsBoxes(s, w) || w == null && !string.IsNullOrEmpty(s.Kind) && !Catalog.IsTrait(s.Kind) && s.Kind != Catalog.Any;
    }

    /// <summary>
    /// 规则的提醒（不阻止执行）：容易造成大范围或拿错东西的写法。
    /// ① 对象是全店的全部物品、没有条件；② 放入、交换的对象直接是一个容器的设备名（处理的是容器本身）；
    /// ③ 工具是容器（会用其中第一件能用的东西）。world 为空时只做 ①。
    /// </summary>
    public static List<string> RuleWarnings(RulesData d, WorkRule r, IWorld w = null)
    {
        var list = new List<string>();
        foreach (var a in r.Actions)
        {
            if (a.Verb == ActVerb.Note) continue;
            var sel = Defs.Selector(d, a.List);
            if (sel != null && !Places.IsName(sel.Id) && sel.From.Contains(Places.Shop) && (sel.Kind == Catalog.Any || sel.Kind == Catalog.Self)
                && sel.Where.Items.Count == 0 && sel.Take == null)
                list.Add(L.T("check.warn.sel_all_shop", sel.Name));
            if (w == null) continue;
            bool Boxes(string id) => Places.IsDevice(id) && Eval.Device(w, id) is { Count: > 0 } n && n.All(x => Catalog.Matches(w, x, Catalog.Container));
            if (a.Verb is ActVerb.Move or ActVerb.Swap && Boxes(a.List))
                list.Add(L.T("check.warn.sel_itself", Text.Name(d, a.List), Text.Name(d, a.List)));
            if (a.Verb == ActVerb.Use && !string.IsNullOrEmpty(a.Tool) && (Boxes(a.Tool) || Defs.Selector(d, a.Tool) is { } ts && !Places.IsDevice(ts.Id) && Eval.YieldsBoxes(ts, w)))
                list.Add(L.T("check.warn.tool_is_box", Text.Name(d, a.Tool)));
        }
        return list.Distinct().ToList();
    }

    /// <summary>规则的结构错误（为空 = 可执行）。world 可为空（不检查绑定是否仍存在）。</summary>
    public static List<string> RuleErrors(RulesData d, Schedule s, WorkRule r, IWorld w = null)
    {
        var e = new List<string>();
        foreach (var p in r.Pre)
        {
            switch (p.RefKind)
            {
                case PreRef.Selector:
                    if (Defs.Selector(d, p.Ref) == null) e.Add(L.T("check.err.pre_sel_missing"));
                    else if (p.Where != null && Defs.Selector(d, p.Ref).IsNumber) e.Add(L.T("check.err.pre_stat_where"));
                    break;
                case PreRef.Note:
                    if (s.Notepad.All(n => n.Id != p.Ref)) e.Add(L.T("check.err.pre_note_missing"));
                    break;
                case PreRef.Sys:
                    if (Vocab.SysVars.All(v => v.id != p.Ref)) e.Add(L.T("check.err.pre_sys_missing"));
                    break;
            }
            bool needsRight = !(p.RefKind == PreRef.Selector && p.Test is PreTest.NotEmpty or PreTest.Empty
                                && Defs.Selector(d, p.Ref) is { IsNumber: false });
            if (needsRight) e.AddRange(ExprErrors(d, p.Right, ExprScope.Runtime, s));
        }
        if (r.Actions.Count == 0) e.Add(L.T("check.err.rule_no_actions"));
        foreach (var a in r.Actions)
        {
            if (Catalog.Ops.All(o => o.Id != a.Verb)) { e.Add(L.T("check.err.rule_unknown_verb", a.Verb)); continue; }
            var spec = Catalog.Op(a.Verb);
            if (a.Verb == ActVerb.Note)
            {
                if (s.Notepad.All(n => n.Id != a.Note)) e.Add(L.T("check.err.rule_note_missing"));
                e.AddRange(ExprErrors(d, a.Value, ExprScope.Runtime, s));
                continue;
            }
            if (a.Verb == ActVerb.Call)
            {
                e.AddRange(Calls.Errors(d, s, a));
                continue;
            }
            var list = Defs.Selector(d, a.List);
            if (list == null || list.IsNumber) { e.Add(L.T("check.err.rule_target_missing", spec.Name)); continue; }
            if (a.Verb == ActVerb.Move && string.IsNullOrEmpty(a.To)) e.Add(L.T("check.err.rule_move_no_to"));
            else if (a.Verb == ActVerb.Move && !IsContainerRef(d, a.To, w)) e.Add(L.T("check.err.rule_move_not_box", Text.Name(d, a.To)));
            if (a.Verb == ActVerb.Swap && Defs.Selector(d, a.To) is not { IsNumber: false }) e.Add(L.T("check.err.rule_swap_no_to"));
            if (a.Verb == ActVerb.Use)
            {
                if (string.IsNullOrEmpty(a.Tool)) e.Add(L.T("check.err.rule_use_no_tool"));
                else if (!Fixtures.Is(a.Tool) && !Places.IsShop(a.Tool) && Defs.Selector(d, a.Tool) is not { IsNumber: false })
                    e.Add(L.T("check.err.rule_use_tool_kind"));
                if (a.Times < 1) e.Add(L.T("check.err.rule_use_times_invalid"));
            }
            if (a.Verb is ActVerb.Activate or ActVerb.Toggle or ActVerb.Press)
            {
                if (a.Verb == ActVerb.Press && string.IsNullOrWhiteSpace(a.Button)) e.Add(L.T("check.err.rule_press_no_button"));
                if (a.Times < 1) e.Add(L.T("check.err.rule_times_invalid", spec.Name));
            }
            if (a.Verb is ActVerb.Mark or ActVerb.Unmark && string.IsNullOrWhiteSpace(a.Text)) e.Add(L.T("check.err.rule_mark_empty"));
        }

        e.AddRange(Scope.BadRefs(d, s.Id, RuleRefs(r)));

        // 连带引用的定义
        foreach (var id in Closure(d, RuleRefs(r)))
        {
            if (d.Objects.FirstOrDefault(o => o.Id == id) is { IsRef: true } param)
            {
                foreach (var x in ParamErrors(d, param)) e.Add(x);
                continue;
            }
            if (Places.IsBuiltin(id) || d.Objects.Any(o => o.Id == id)) continue;
            if (d.Selectors.FirstOrDefault(x => x.Id == id) is { } sel)
            {
                foreach (var x in SelectorErrors(d, sel)) e.Add(L.T("check.err.sel_prefix", sel.Name, x));
                continue;
            }
            if (d.Properties.FirstOrDefault(x => x.Id == id) is { } prop)
            {
                foreach (var x in PropertyErrors(d, prop)) e.Add(L.T("check.err.prop_prefix", prop.Name, x));
                continue;
            }
            if (Catalog.PropName(id) != null || IsKnownProp(id)) continue;
            e.Add(L.T("check.err.def_missing"));
        }
        return e.Distinct().ToList();
    }

    /// <summary>设备、列表参数的错误：没选值、值的类型不对、值越出作用域（全局参数用了本流程筛选器）。</summary>
    public static List<string> ParamErrors(RulesData d, ObjectDef p)
    {
        var e = new List<string>();
        if (!p.IsRef) return e;
        bool ok = p.Type == ParamType.Device ? Places.IsName(p.Ref) : d.Selectors.FirstOrDefault(x => x.Id == p.Ref) is { IsNumber: false };
        if (!ok) e.Add(L.T("check.err.param_no_value", p.Name));
        else e.AddRange(Scope.BadRefs(d, p.Owner, new[] { p.Ref }));
        return e;
    }

    /// <summary>未列在词表里、但游戏能读的属性（mark:、原始标签等）。</summary>
    private static bool IsKnownProp(string id) => id.StartsWith("mark:") || id == Catalog.TypeProp || GameTags.IsDynamicProp(id) || id.Contains('.') || id is "kcal" or "maxHealth" or "uses" or "sex" or "hunger" or "maxHunger";

    /// <summary>未被任何规则引用的定义（警告）。</summary>
    public static bool Unused(RulesData d, string id) => UsedBy(d, id).Count == 0;
}
