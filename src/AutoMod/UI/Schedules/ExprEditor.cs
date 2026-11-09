using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>表达式编辑器：一行“项 [运算] 项 …”，每项为“项类型 + 值”两个控件。</summary>
public static class ExprEditor
{
    public class Options
    {
        public List<string> OwnProps;       // 自身属性（为空 = 不可用）
        public List<string> Stats;          // 可引用的挑选器
        public Schedule Notes;              // 记事本（为空 = 不可用）
        public bool Sys;
        public int PerRow = 2;              // 每行几项，超出换行
        public bool Inline;                 // 接在调用方已开始的行后面（第一项和比较方式同一行），不另起一行
        public Action<CustomUIBuilder> Tail; // 最后一行末尾再加的控件（例如条件的删除按钮）
        public RulesData Data;              // 查筛选器名称用的数据（对话框的草稿）；为空 = 规则数据
    }

    private static readonly string[] OpIds = { "+", "-", "*", "/" };
    private static readonly string[] OpNames = { "+", "−", "×", "÷" };

    /// <param name="changed">结构变化（换项类型、换引用、加减项）后调用，一般是重建窗口。数值输入只改数据不重建。</param>
    public static void Build(CustomUIBuilder b, Expr e, Options opt, Action changed)
    {
        e.Terms ??= new List<Term>();
        if (e.Terms.Count == 0) e.Terms.Add(Term.N(0));
        var kinds = new List<(string id, string name)> { (TermKind.Num, Domain.L.T("expr.term.num")), (TermKind.Const, Domain.L.T("expr.term.const")) };
        if (opt.OwnProps != null) kinds.Add((TermKind.Own, Domain.L.T("expr.term.own")));
        if (opt.Stats != null) kinds.Add((TermKind.Stat, Domain.L.T("expr.term.stat")));
        if (opt.Notes != null) kinds.Add((TermKind.Note, Domain.L.T("expr.term.note")));
        if (opt.Sys) kinds.Add((TermKind.Sys, Domain.L.T("expr.term.sys")));

        if (!opt.Inline) b.BeginRow(3);
        int perRow = Math.Max(1, opt.Inline ? 1 : opt.PerRow);
        for (int i = 0; i < e.Terms.Count; i++)
        {
            int idx = i;
            var t = e.Terms[i];
            if (i > 0 && i % perRow == 0) b.End().BeginRow(3); // 换行：一行放不下太多项
            if (i > 0)
            {
                while (e.Ops.Count < i) e.Ops.Add("+");
                b.AddDropdown(Ui.L(OpNames), Math.Max(0, Array.IndexOf(OpIds, e.Ops[i - 1])), Ui.AI(k => { e.Ops[idx - 1] = OpIds[k]; changed(); }));
                if (opt.Inline) b.Width(60);
            }
            b.AddDropdown(Ui.L(kinds.Select(k => k.name)), RulesUi.IndexOf(kinds, k => k.id == t.Kind), Ui.AI(k =>
            {
                e.Terms[idx] = kinds[k].id == TermKind.Num ? Term.N(0) : new Term { Kind = kinds[k].id };
                changed();
            }));
            if (opt.Inline) b.Width(100);
            switch (t.Kind)
            {
                case TermKind.Num:
                    b.AddInput(Domain.L.T("expr.term.num"), t.Num.ToString("0.##"), Ui.AS(s => { if (double.TryParse(s, out var v)) t.Num = v; }));
                    if (opt.Inline) b.Width(90);
                    break;
                case TermKind.Const:
                    RefDropdown(b, t, RulesUi.Constants.Select(o => (o.Id, o.Name + Domain.L.T("text.paren", o.Value.ToString("0.##")))).ToList(), changed);
                    break;
                case TermKind.Own:
                    RefDropdown(b, t, (opt.OwnProps ?? new List<string>()).Select(p => (p, RulesUi.PropName(p))).ToList(), changed);
                    break;
                case TermKind.Stat:
                    RefDropdown(b, t, (opt.Stats ?? new List<string>()).Select(id => (opt.Data ?? RulesUi.D).Selectors.FirstOrDefault(x => x.Id == id)).Where(x => x != null)
                        .Select(x => (x.Id, x.IsNumber ? x.Name : x.Name + ".数量")).ToList(), changed);
                    break;
                case TermKind.Note:
                    RefDropdown(b, t, opt.Notes?.Notepad.Select(n => (n.Id, n.Name)).ToList() ?? new(), changed);
                    break;
                case TermKind.Sys:
                    RefDropdown(b, t, Vocab.SysVars.Select(v => (v.id, v.name)).ToList(), changed);
                    break;
            }
            if (opt.Inline && t.Kind != TermKind.Num) b.Width(180);
        }
        b.AddButton("＋", Ui.A(() => { e.Ops.Add("+"); e.Terms.Add(Term.N(0)); changed(); })).Tip(Domain.L.T("expr.tip_add"));
        if (e.Terms.Count > 1) b.AddButton("－", Ui.A(() => { e.Terms.RemoveAt(e.Terms.Count - 1); if (e.Ops.Count > 0) e.Ops.RemoveAt(e.Ops.Count - 1); changed(); })).Tip(Domain.L.T("expr.tip_remove"));
        if (opt.Tail != null) { b.AddFlexibleSpace(); opt.Tail(b); }
        b.End();
    }

    private static void RefDropdown(CustomUIBuilder b, Term t, List<(string id, string name)> opts, Action changed)
    {
        var list = new List<(string id, string name)> { (null, Domain.L.T("expr.select_placeholder")) };
        list.AddRange(opts);
        b.AddDropdown(Ui.L(list.Select(o => o.name)), RulesUi.IndexOf(list, o => o.id == t.Ref), Ui.AI(k => { t.Ref = list[k].id; changed(); }));
    }
}
