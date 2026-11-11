using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoMod.Domain.Rules;

/// <summary>定义与规则的文字表示。</summary>
public static class Text
{
    /// <summary>属性名（游戏侧可补充更多原始属性的名字）。</summary>
    public static Func<string, string> ExtraPropName;

    /// <summary>工具来源：为空 = 店里存放着的物品。</summary>
    public static string Name(RulesData d, string id) =>
        string.IsNullOrEmpty(id) ? L.T("text.unset")
        : Places.IsShop(id) ? L.T("scope.shop")
        : Places.IsName(id) ? Places.NameOf(id)
        : Fixtures.Is(id) ? Fixtures.Name(id)
        : d.Objects.FirstOrDefault(o => o.Id == id)?.Name
          ?? d.Selectors.FirstOrDefault(s => s.Id == id)?.Name
          ?? d.Properties.FirstOrDefault(p => p.Id == id)?.Name
          ?? L.T("text.deleted");

    public static string PropName(RulesData d, string id)
    {
        if (string.IsNullOrEmpty(id)) return L.T("text.unset");
        return d.Properties.FirstOrDefault(p => p.Id == id)?.Name ?? Catalog.PropName(id) ?? ExtraPropName?.Invoke(id) ?? id;
    }

    // ---------- 表达式 ----------

    public static string Expr(RulesData d, Expr e, Schedule s = null)
    {
        if (e?.Terms == null || e.Terms.Count == 0) return L.T("expr.empty");
        var parts = new List<string>();
        for (int i = 0; i < e.Terms.Count; i++)
        {
            if (i > 0) parts.Add(Vocab.OpText(i - 1 < e.Ops.Count ? e.Ops[i - 1] : "+"));
            parts.Add(Term(d, e.Terms[i], s));
        }
        return string.Join(" ", parts);
    }

    public static string Term(RulesData d, Term t, Schedule s = null) => t.Kind switch
    {
        TermKind.Num => L.Num(t.Num),
        TermKind.Const => Name(d, t.Ref),
        TermKind.Own => L.T("expr.own", PropName(d, t.Ref)),
        TermKind.Stat => d.Selectors.FirstOrDefault(x => x.Id == t.Ref) is { } x ? (x.IsNumber ? x.Name : L.T("expr.count", x.Name)) : L.T("text.unset"),
        TermKind.Note => s?.Notepad.FirstOrDefault(n => n.Id == t.Ref)?.Name ?? L.T("text.unset"),
        TermKind.Sys => Vocab.SysShortName(t.Ref),
        _ => "?",
    };

    // ---------- 条件 ----------

    public static string Cond(RulesData d, Cond c, bool top = true)
    {
        if (c == null) return "";
        if (c.Group != null)
        {
            var t = L.Join(c.Group.IsAny ? "or" : "list", c.Group.Items.Select(x => Cond(d, x, false)));
            return top || c.Group.Items.Count < 2 ? t : L.T("text.paren", t);
        }
        if (!string.IsNullOrEmpty(c.Preset)) return Catalog.Preset(c.Preset)?.Name ?? c.Preset;
        if (c.Prop == Catalog.TypeProp && c.Text != null) return c.Op == "!=" ? L.T("cond.type_is_not", Catalog.TypeName(c.Text)) : L.T("cond.type_is", Catalog.TypeName(c.Text));
        if (c.Text != null) return $"{PropName(d, c.Prop)} {(c.Op == "!=" ? "≠" : "=")} {c.Text}";
        // 品质等级：序号 0 最好，写成等级名
        if (Catalog.GradeNames(c.Prop) is { Count: > 0 } grades && c.Right?.Terms.Count == 1 && c.Right.Terms[0].Kind == TermKind.Num)
        {
            int g = Math.Clamp((int)Math.Round(c.Right.Terms[0].Num), 0, grades.Count - 1);
            string word = c.Op switch
            {
                "<=" => L.T("grade.at_least"),
                ">=" => L.T("grade.at_most"),
                "<" => L.T("grade.better_than"),
                ">" => L.T("grade.worse_than"),
                "!=" => L.T("grade.is_not"),
                _ => L.T("grade.is"),
            };
            return $"{GradeProp(c.Prop, PropName(d, c.Prop))} {word} {grades[g]}";
        }
        // 是非属性：“在机器里”“不在机器里”“已死亡”“未死亡”这样写
        if (Catalog.IsBool(c.Prop) && c.Op == "=" && c.Right?.Terms.Count == 1 && c.Right.Terms[0].Kind == TermKind.Num)
            return c.Right.Terms[0].Num != 0 ? Catalog.PropYesName(c.Prop) ?? PropName(d, c.Prop) : Catalog.PropNotName(c.Prop);
        return $"{PropName(d, c.Prop)} {Vocab.OpText(c.Op)} {Expr(d, c.Right)}";
    }

    /// <summary>“水质等级” → “水质”（后面接等级名）。短名键优先。</summary>
    private static string GradeProp(string propId, string name) =>
        Catalog.GradeShortName(propId) ?? name;

    public static string Group(RulesData d, CondGroup g)
    {
        if (g?.Items == null || g.Items.Count == 0) return "";
        return L.Join(g.IsAny ? "or" : "list", g.Items.Select(x => Cond(d, x, false)));
    }

    // ---------- 挑选器 ----------

    private static string ExpandSource(RulesData d, SelectorDef s, string src)
    {
        var (kind, slot) = Eval.Expand(d, s, src);
        string name = Name(d, src);
        return kind switch
        {
            Eval.ExpandKind.Slot => L.T("eval.from.slot", name, slot),
            Eval.ExpandKind.Self => L.T("eval.from.self", name),
            Eval.ExpandKind.Contents => L.T("eval.from.contents", name),
            _ => name,
        };
    }

    /// <summary>完整定义（和筛选器对话框的说法一致）：范围：…；类型：…；条件：…；排除：…；选取：…</summary>
    public static string Selector(RulesData d, SelectorDef s)
    {
        var from = s.From.Count == 0 ? L.T("text.unset") : L.Join("list", s.From.Select(f => ExpandSource(d, s, f)));
        var parts = new List<string> { L.T("text.selector.scope", from) };
        if (s.Kind != Catalog.Self) parts.Add(L.T("text.selector.type", Catalog.TypeName(s.Kind)));
        var conds = Group(d, s.Where);
        if (conds != "") parts.Add(L.T("text.selector.conds", conds));
        if (s.IsNumber)
        {
            var agg = s.Agg ?? new AggSpec();
            string aggStr = agg.Fn == AggFn.Count
                ? L.T("text.selector.stat_count", Vocab.AggName(agg.Fn))
                : L.T("text.selector.stat_other", Vocab.AggName(agg.Fn), PropName(d, agg.Prop));
            parts.Insert(0, aggStr);
            return L.Join("semi", parts);
        }
        if (!string.IsNullOrEmpty(s.Minus)) parts.Add(L.T("text.selector.minus", Name(d, s.Minus)));
        if (Take(d, s) is { Length: > 0 } take) parts.Add(L.T("text.selector.take", take));
        return L.Join("semi", parts);
    }

    /// <summary>
    /// 选取（和对话框的说法一致）：电量从低到高 前 1 件 / 年龄从低到高 排除前 2 件 / 全部时写排序（“按电量从高到低”）。
    /// 没有排序也没有数量时为空。“排除前 N 件”存的是从第 N + 1 件起。
    /// </summary>
    public static string Take(RulesData d, SelectorDef s)
    {
        bool sorted = s.Sort != null && !string.IsNullOrEmpty(s.Sort.Prop);
        string order = sorted ? L.T(s.Sort.Desc ? "sort.desc" : "sort.asc", PropName(d, s.Sort.Prop)) : "";
        if (s.Take == null) return sorted ? L.T("take.by_sort", order) : "";
        bool plain = s.Take.N is { Terms.Count: 1 } e && e.Terms[0].Kind == TermKind.Num;
        bool from = s.Take.Mode == TakeMode.From;
        double n = plain ? (from ? s.Take.N.Terms[0].Num - 1 : s.Take.N.Terms[0].Num) : 2;
        string count = plain ? L.Num(n) : Expr(d, s.Take.N) + (from ? " - 1" : "");
        string body = L.NAs(from ? "take.exclude_first" : "take.first", n, count);
        return sorted ? L.T("take.sorted_take", order, body) : body;
    }

    /// <summary>精简定义（左栏）：来源 · 类型 · 条件 · 排序 · 数量</summary>
    public static string SelectorShort(RulesData d, SelectorDef s)
    {
        var parts = new List<string>
        {
            s.From.Count == 0 ? L.T("text.selector.unset_from") : L.Join("list", s.From.Select(f => Name(d, f))),
            Catalog.TypeName(s.Kind) + (s.Slot == "battery" ? L.T("text.slot.battery") : s.Slot == "filter" ? L.T("text.slot.filter") : s.Slot != null && s.Slot.StartsWith(SlotNames.Prefix) ? L.T("text.paren", SlotNames.Name(s.Slot, d, s.From)) : ""),
        };
        var conds = Group(d, s.Where);
        if (conds != "") parts.Add(conds);
        if (!string.IsNullOrEmpty(s.Minus)) parts.Add(L.T("text.short.minus", Name(d, s.Minus)));
        string head = "";
        if (s.IsNumber)
        {
            var agg = s.Agg ?? new AggSpec();
            head = Vocab.AggName(agg.Fn) + (agg.Fn == AggFn.Count ? "" : L.T("text.paren", PropName(d, agg.Prop))) + L.T("text.dot_sep");
        }
        else
        {
            if (Take(d, s) is { Length: > 0 } take) parts.Add(take);
        }
        return head + L.Join("dot", parts);
    }

    public static string Property(RulesData d, PropertyDef p) => L.T("text.prop_def", Catalog.TypeName(p.ItemKind), Expr(d, p.Formula));

    public static string Object(ObjectDef o) => L.T("text.object_def", L.Num(o.Value), o.Unit);

    /// <summary>
    /// 容器列表的简短写法：不多于 max 个时逐个列出；多了按名字（去掉“#2”编号）归并，最多列 max 种：
    /// “高级涡轮增压器 ×17、清洁机器人 ×4、水分收集器 ×5 等（共 34 个）”。
    /// </summary>
    public static string Members(IList<string> names, int max = 3)
    {
        if (names.Count <= max) return L.Join("list", names);
        var groups = names.GroupBy(n => n.Split('#')[0]).OrderByDescending(g => g.Count()).ToList();
        string list = L.Join("list", groups.Take(max).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.First()));
        return L.T(groups.Count > max ? "text.members.more" : "text.members.all", list, names.Count);
    }

    // ---------- 规则 ----------

    public static string Pre(RulesData d, Schedule s, PreCond p)
    {
        string n = p.RefKind switch
        {
            PreRef.Note => s.Notepad.FirstOrDefault(x => x.Id == p.Ref)?.Name ?? L.T("text.deleted"),
            PreRef.Sys => Vocab.SysShortName(p.Ref),
            _ => Name(d, p.Ref),
        };
        if (p.Where != null && p.RefKind == PreRef.Selector)
        {
            string c = Cond(d, p.Where);
            return p.Test switch
            {
                PreTest.NotEmpty => L.T("pre.where.not_empty", c, n),
                PreTest.Empty => L.T("pre.where.empty", c, n),
                PreTest.CountGt => L.T("pre.where.gt", c, n, Expr(d, p.Right, s)),
                PreTest.CountLt => L.T("pre.where.lt", c, n, Expr(d, p.Right, s)),
                _ => L.T("pre.where.cmp", c, n, Vocab.OpText(p.Test), Expr(d, p.Right, s)),
            };
        }
        return p.Test switch
        {
            PreTest.NotEmpty => L.T("pre.not_empty", n),
            PreTest.Empty => L.T("pre.empty", n),
            PreTest.CountGt => L.T("pre.gt", n, Expr(d, p.Right, s)),
            PreTest.CountLt => L.T("pre.lt", n, Expr(d, p.Right, s)),
            _ => L.T("pre.cmp", n, Vocab.OpText(p.Test), Expr(d, p.Right, s)),
        };
    }

    /// <summary>执行条件的反面（报告里写“执行条件不满足：种子槽中的种子 不为空”）。</summary>
    public static string PreNot(RulesData d, Schedule s, PreCond p)
    {
        string op = p.Test switch
        {
            PreTest.NotEmpty => PreTest.Empty, PreTest.Empty => PreTest.NotEmpty,
            ">" => "<=", ">=" => "<", "<" => ">=", "<=" => ">", "=" => "!=", "!=" => "=",
            _ => p.Test,
        };
        if (p.Test is PreTest.CountGt or PreTest.CountLt)
        {
            string n = Name(d, p.Ref);
            string expr = Expr(d, p.Right, s);
            if (p.Where != null && p.RefKind == PreRef.Selector)
            {
                string c = Cond(d, p.Where);
                return L.T(p.Test == PreTest.CountGt ? "prenot.where_no_more" : "prenot.where_no_less", c, n, expr);
            }
            return L.T(p.Test == PreTest.CountGt ? "prenot.no_more" : "prenot.no_less", n, expr);
        }
        return Pre(d, s, new PreCond { RefKind = p.RefKind, Ref = p.Ref, Test = op, Right = p.Right, Where = p.Where });
    }

    /// <summary>按钮的显示名称（优先查本地化键，无本地化则显示原值）。</summary>
    public static string ButtonName(string id) =>
        string.IsNullOrEmpty(id) ? "" : L.Has("btn." + id) ? L.T("btn." + id) : L.Has(id) ? L.T(id) : id;

    public static string Action(RulesData d, Schedule s, ActionDef a)
    {
        string Lname = Name(d, a.List);
        string times = a.Times > 1 ? L.N("action.times", a.Times) : "";
        return a.Verb switch
        {
            ActVerb.Move => L.T("action.move", Lname, Name(d, a.To) + (string.IsNullOrEmpty(a.ToSlot) ? "" : L.T("text.paren", SlotNames.Name(a.ToSlot, d, new[] { a.To })))),
            ActVerb.Swap => L.T("action.swap", Lname, Name(d, a.To)),
            ActVerb.Activate => L.T("action.activate", Lname) + times,
            ActVerb.Press => L.T("action.press", Lname, ButtonName(a.Button)) + times,
            ActVerb.Toggle => L.T("action.toggle", Lname),
            ActVerb.Use => L.T("action.use", Name(d, a.Tool), Lname) + times,
            ActVerb.Mark => L.T("action.mark", Lname, a.Text),
            ActVerb.Unmark => L.T("action.unmark", Lname, a.Text),
            ActVerb.Note => L.T(a.NoteMode == "set" ? "action.note.set" : "action.note.add", Expr(d, a.Value, s), s.Notepad.FirstOrDefault(n => n.Id == a.Note)?.Name ?? L.T("text.unset")),
            _ => a.Verb,
        };
    }

    /// <summary>规则摘要：前提：… → 动作；动作</summary>
    public static string Rule(RulesData d, Schedule s, WorkRule r)
    {
        var pre = L.Join(r.Join == "any" ? "or" : "and", r.Pre.Select(p => Pre(d, s, p)));
        var acts = r.Actions.Count == 0 ? L.T("rule.no_actions") : L.Join("semi", r.Actions.Select(a => Action(d, s, a)));
        return pre == "" ? acts : L.T("rule.when_arrow", pre, acts);
    }

    // ---------- 报告 ----------

    /// <summary>任务的执行结果，带原因：“未执行：AP 不足（前面的任务已用 11 / 共 11 AP）”。</summary>
    public static string Outcome(RuleRunReport rr) =>
        string.IsNullOrEmpty(rr.Why) ? PreResult.Name(rr.Pre) : L.T("fmt.colon", PreResult.Name(rr.Pre), rr.Why);

    /// <summary>AP 的统一写法：已用 X / 共 Y AP。</summary>
    public static string Ap(double used, double total) =>
        L.T("text.ap", used.ToString("0.#"), total.ToString("0.#"));

    /// <summary>一个班次的一句话（写进游戏早报，要短）：“完成 31 件，6 件留到下次，2 项没做成；测试3 升到 Lv2”。</summary>
    public static string RunSummary(RunReport r)
    {
        string endDot = L.T("text.period");
        if (r.Groups.Count == 0)
            return r.Notes.Count > 0 ? L.Join("seq", r.Notes.Select(n => n.TrimEnd('。', '.'))) + endDot : L.T("runsummary.no_apprentices");
        int done = r.Groups.Sum(g => g.Rules.Sum(x => x.Done.Values.Sum()));
        int deferred = r.Groups.Sum(g => g.Deferred);
        int issues = r.Groups.Sum(g => g.Rules.Sum(x => x.Issues.Count(i => i.Code != "broken" && i.Code != Reports.FailCode.Many)));
        string s = L.N("runsummary.done", done);
        if (deferred > 0) s += L.N("runsummary.deferred", deferred);
        if (issues > 0) s += L.N("runsummary.issues", issues);
        var ups = r.Groups.SelectMany(g => g.LevelUps).ToList();
        if (ups.Count > 0) s += L.T("runsummary.levelup", L.Join("list", ups));
        return s + endDot;
    }

    /// <summary>结构化属性变化合成文字显示。</summary>
    public static string FormatMerged(string label, IReadOnlyList<StepChange> changes, IReadOnlyList<string> others)
    {
        var parts = new List<string>();
        if (changes != null)
        {
            foreach (var c in changes)
                parts.Add(L.T("fmt.arrow_prop", c.Name, c.Old ?? "—", c.New ?? "—"));
        }
        if (others != null)
        {
            foreach (var o in others)
                if (!string.IsNullOrEmpty(o)) parts.Add(o);
        }
        string body = L.Join("comma", parts);
        return string.IsNullOrEmpty(label) ? body : L.T("fmt.colon", label, body);
    }

    /// <summary>
    /// 同一件东西连做几次的结果合成一句：“标签：属性 旧 → 新，……”，同一属性取第一次的旧值和最后一次的新值；
    /// 不是这种格式的原样保留（去重）。
    /// </summary>
    public static string MergeSteps(IEnumerable<string> texts)
    {
        var list = texts.Where(t => !string.IsNullOrEmpty(t)).ToList();
        if (list.Count <= 1) return list.FirstOrDefault() ?? "";
        string label = null;
        var order = new List<string>();
        var changes = new Dictionary<string, (string old, string cur)>();
        var plain = new List<string>();
        foreach (var t in list)
        {
            int colon = t.IndexOfAny(new[] { '：', ':' });
            string head = colon >= 0 ? t[..colon] : null, body = colon >= 0 ? t[(colon + 1)..] : t;
            label ??= head;
            foreach (var part in body.Split(new[] { '，', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                int arrow = p.LastIndexOf(" → ", StringComparison.Ordinal);
                int space = arrow > 0 ? p.LastIndexOf(' ', arrow - 1) : -1;
                if (arrow < 0 || space < 0) { if (!plain.Contains(p)) plain.Add(p); continue; }
                string name = p[..space], old = p[(space + 1)..arrow], cur = p[(arrow + 3)..];
                if (changes.TryGetValue(name, out var c)) changes[name] = (c.old, cur);
                else { changes[name] = (old, cur); order.Add(name); }
            }
        }
        var mergedChanges = order.Select(n => new StepChange(n, changes[n].old, changes[n].cur)).ToList();
        return FormatMerged(label, mergedChanges, plain);
    }
}
