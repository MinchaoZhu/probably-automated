using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>条件的编辑行：筛选器的条件、前提的条件共用。</summary>
public static class CondEditor
{
    public const string GroupBase = "base";
    public const string GroupQuality = "quality";
    public const string GroupIngredient = "ingredient";
    public const string GroupRatio = "ratio";
    public const string GroupTag = "tag";
    public const string GroupType = "type";

    /// <summary>条件属性的分组：属性多（基本属性、品质、成分、游戏标签……），先选组再选属性。</summary>
    public static string GroupOf(string prop)
    {
        if (prop == Catalog.TypeProp) return GroupType;
        if (prop == null || !GameTags.IsDynamicProp(prop)) return GroupBase;
        if (prop.StartsWith("liq:")) return GroupIngredient;
        if (prop.StartsWith("pct:")) return GroupRatio;
        if (prop.StartsWith("tag:") || prop.StartsWith("str:") || prop.StartsWith("has:")) return GroupTag;
        return GroupQuality;
    }

    /// <summary>选了“其他…”、正在手填文字的条件。</summary>
    private static readonly HashSet<Cond> Typing = new();

    private static readonly string[] GroupIds = { GroupBase, GroupQuality, GroupIngredient, GroupRatio, GroupTag, GroupType };

    public static string GroupName(string id) => id switch
    {
        GroupBase => Domain.L.T("cond.group.base"),
        GroupQuality => Domain.L.T("cond.group.quality"),
        GroupIngredient => Domain.L.T("cond.group.ingredient"),
        GroupRatio => Domain.L.T("cond.group.ratio"),
        GroupTag => Domain.L.T("cond.group.tag"),
        GroupType => Domain.L.T("cond.group.type"),
        _ => id,
    };

    /// <summary>
    /// 一条属性条件：先选组（常用、品质、成分……）再选属性，然后比较方式和值。
    /// stats：右边表达式能引用的筛选器；kind：当前的类型（换成“类型”组时的默认值）；kinds：“是 / 不是某类型”的选项。
    /// </summary>
    public static void Row(CustomUIBuilder b, Cond c, List<string> props, List<string> stats, string indent,
        Func<string> kind, Func<List<(string id, string name)>> kinds, Action remove, Action rebuild, RulesData data = null)
    {
        var all = props.ToList();
        if (c.Prop != null && c.Prop != Catalog.TypeProp && !all.Contains(c.Prop)) all.Add(c.Prop);
        string group = GroupOf(c.Prop);
        var groups = GroupIds.Where(g => g == GroupType || g == group || all.Any(p => GroupOf(p) == g)).ToList();
        var inGroup = all.Where(p => GroupOf(p) == group).Select(p => (p, RulesUi.PropName(p))).ToList();

        b.BeginRow(4).AddLabel(indent)
            .AddDropdown(Ui.L(groups.Select(GroupName)), Math.Max(0, groups.IndexOf(group)), Ui.AI(k =>
            {
                // 换组：取组里第一个属性；类型组变成“是 + 当前类型”
                if (groups[k] == GroupType) { c.Prop = Catalog.TypeProp; c.Text ??= kind(); c.Right = null; if (c.Op != "!=") c.Op = "="; }
                else if (all.FirstOrDefault(p => GroupOf(p) == groups[k]) is { } first) SetProp(c, first);
                rebuild();
            })).Searchable().Width(Domain.L.Int("layout.cond.value_width", 120));

        // 类型：是 / 不是某类型（例：不是违禁品）
        if (c.Prop == Catalog.TypeProp)
        {
            var kindList = kinds();
            if (c.Text != null && kindList.All(x => x.id != c.Text)) kindList.Add((c.Text, Catalog.TypeName(c.Text)));
            b.AddDropdown(Ui.L(new[] { Domain.L.T("cond.is"), Domain.L.T("cond.is_not") }), c.Op == "!=" ? 1 : 0, Ui.AI(k => { c.Op = k == 1 ? "!=" : "="; rebuild(); }))
                .AddDropdown(Ui.L(kindList.Select(x => x.name)), RulesUi.IndexOf(kindList, x => x.id == c.Text), Ui.AI(k => { c.Text = kindList[k].id; rebuild(); })).Searchable(i => RulesUi.KindGroup(kindList[i].id))
                .AddButton("×", Ui.A(() => { remove(); rebuild(); }))
                .End();
            return;
        }
        // 是非属性在属性列表里直接列正反两项（“在储藏区里 / 不在储藏区里”），不再另选“是 / 不是”
        bool simpleBool = Catalog.IsBool(c.Prop) && (c.Right == null || (c.Right.Terms.Count <= 1 && c.Right.Terms.All(t => t.Kind == TermKind.Num)));
        bool yes = !simpleBool || ((c.Right?.Terms.FirstOrDefault()?.Num ?? 1) != 0) != (c.Op is "<" or "<=");
        var opts = new List<(string p, int? v, string name)>();
        foreach (var (p, name) in inGroup)
        {
            if (Catalog.IsBool(p)) { opts.Add((p, 1, Catalog.PropYesName(p) ?? name)); opts.Add((p, 0, Catalog.PropNotName(p) ?? name)); }
            else opts.Add((p, null, name));
        }
        int at = RulesUi.IndexOf(opts, x => x.p == c.Prop && (x.v == null || !simpleBool || (x.v == 1) == yes));
        b.AddDropdown(Ui.L(opts.Select(x => x.name)), at, Ui.AI(k =>
        {
            SetProp(c, opts[k].p);
            if (opts[k].v is { } v) { c.Op = "="; c.Right = Expr.Of(v); }
            rebuild();
        })).Searchable().Width(200);
        // 文字属性（标签文字、品质等级名）：等于 / 不等于一段文字。值从店里出现过的里选，“其他…”再手填
        if (GameTags.IsTextProp(c.Prop))
        {
            var tops = new[] { "=", "!=" };
            var values = RulesUi.TextValues(c.Prop);
            if (c.Text != null && !values.Contains(c.Text)) values.Add(c.Text);
            bool typing = Typing.Contains(c);
            var shown = values.Select(v => v.Length == 0 ? Domain.L.T("cond.text_empty") : v).Append(Domain.L.T("cond.text_other")).ToList();
            b.AddDropdown(Ui.L(new[] { Domain.L.T("cond.equal"), Domain.L.T("cond.not_equal") }), c.Op == "!=" ? 1 : 0, Ui.AI(k => { c.Op = tops[k]; rebuild(); }))
                .Width(Domain.L.Int("layout.cond.value_width", 120))
                .AddDropdown(Ui.L(shown), typing ? shown.Count - 1 : Math.Max(0, values.IndexOf(c.Text)), Ui.AI(k =>
                {
                    if (k == shown.Count - 1) Typing.Add(c);
                    else { Typing.Remove(c); c.Text = values[k]; }
                    rebuild();
                })).Searchable();
            if (typing) b.AddInput(Domain.L.T("cond.text_input"), c.Text ?? "", Ui.AS(v => { c.Text = v.Trim(); rebuild(); }));
            b.AddButton("×", Ui.A(() => { Typing.Remove(c); remove(); rebuild(); })).End();
            return;
        }
        // 是非属性：正反已经在属性列表里选了
        if (simpleBool)
        {
            b.AddFlexibleSpace().AddButton("×", Ui.A(() => { remove(); rebuild(); })).End();
            return;
        }
        // 品质等级：选等级名（0 最好），“至少是 基础水” = 等级序号 ≤ 基础水的序号
        if (RulesUi.Grades(c.Prop) is { Count: > 0 } grades && (c.Right == null || (c.Right.Terms.Count <= 1 && c.Right.Terms.All(t => t.Kind == TermKind.Num))))
        {
            var gops = new[] { "<=", ">=", "=" };
            int cur = (int)Math.Round(c.Right?.Terms.FirstOrDefault()?.Num ?? 0);
            b.AddDropdown(Ui.L(new[] { Domain.L.T("cond.at_least"), Domain.L.T("cond.at_most"), Domain.L.T("cond.is") }), Math.Max(0, Array.IndexOf(gops, c.Op)), Ui.AI(k => { c.Op = gops[k]; rebuild(); }))
                .AddDropdown(Ui.L(grades), Math.Clamp(cur, 0, grades.Count - 1), Ui.AI(k => { c.Right = Expr.Of(k); rebuild(); })).Searchable()
                .AddButton("×", Ui.A(() => { remove(); rebuild(); }))
                .End();
            return;
        }
        var ops = new[] { ">", ">=", "<", "<=", "=" };
        var opn = new[] { ">", "≥", "<", "≤", "=" };
        // 数值比较：比较的值接在同一行（多项的式子换行接着写），删除按钮在最后
        b.AddDropdown(Ui.L(opn), Math.Max(0, Array.IndexOf(ops, c.Op)), Ui.AI(k => { c.Op = ops[k]; rebuild(); })).Width(60);
        ExprEditor.Build(b, c.Right ??= Expr.Of(0), new ExprEditor.Options
        {
            OwnProps = props, Stats = stats, Inline = true, Data = data,
            Tail = r => r.AddButton("×", Ui.A(() => { remove(); rebuild(); })),
        }, rebuild);
    }

    /// <summary>换属性：文字属性用文字比较（没有右边的表达式），数值属性反之。</summary>
    public static void SetProp(Cond c, string prop)
    {
        c.Prop = prop;
        if (prop != Catalog.TypeProp && c.Text != null && !GameTags.IsTextProp(prop)) c.Text = null;
        if (GameTags.IsTextProp(prop)) { c.Text ??= ""; c.Right = null; if (c.Op != "!=") c.Op = "="; }
        else { c.Text = null; c.Right ??= Expr.Of(0); if (c.Op == "!=") c.Op = "="; }
    }
}
