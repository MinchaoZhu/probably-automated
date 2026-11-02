using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>自定义属性弹窗：名称、适用类型、计算公式，附一件样本物品的计算结果。</summary>
public static class PropertyDialog
{
    private const string Id = "am_propdlg";
    private static PropertyDef _d, _orig;

    private static RulesData D => RulesUi.D;

    private static readonly (string id, string name)[] Kinds =
    {
        ("rat", Catalog.TypeName("rat")), ("battery", Catalog.TypeName("battery")), ("filter", Catalog.TypeName("filter")), ("liquid", Catalog.TypeName("liquid")),
    };

    public static void New()
    {
        _orig = null;
        _d = new PropertyDef { Id = Defs.NewId("p"), Name = Domain.L.T("prop_dlg.default_name"), ItemKind = "rat", Formula = Expr.Ref(TermKind.Own, "weight") };
        Show();
    }

    public static void Edit(PropertyDef p)
    {
        _orig = p;
        _d = Defs.Clone(p);
        Show();
    }

    public static void Show()
    {
        RulesUi.View = null; // 自定义属性只有全局：公式只能引用全局常量
        var d = _d;
        var b = Ui.Window(Id, _orig == null ? Domain.L.T("prop_dlg.title_new") : Domain.L.T("prop_dlg.title_edit", _orig.Name), 760, 360);
        b.BeginRow(6).AddLabel(Domain.L.T("ui.name")).AddInput(Domain.L.T("ui.name"), d.Name, Ui.AS(v => d.Name = v)).End();
        b.BeginRow(6).AddLabel(Domain.L.T("prop_dlg.target_kind")).AddDropdown(Ui.L(Kinds.Select(k => k.name)), RulesUi.IndexOf(Kinds, k => k.id == d.ItemKind), Ui.AI(k =>
        {
            d.ItemKind = Kinds[k].id;
            d.Formula = Expr.Ref(TermKind.Own, Catalog.PropsFor(d.ItemKind, RulesUi.World).FirstOrDefault());
            Show();
        })).End();
        b.AddLabel(Tone.Bold(Domain.L.T("prop_dlg.formula")));
        ExprEditor.Build(b, d.Formula, new ExprEditor.Options { OwnProps = Catalog.PropsFor(d.ItemKind, RulesUi.World).ToList() }, Show);
        b.AddLabel($"<size=13>{Domain.L.T("prop_dlg.formula_display", AutoMod.Domain.Rules.Text.Expr(D, d.Formula))}</size>");
        b.AddLabel($"<size=13>{Domain.L.T("prop_dlg.sample_calc", Sample(d))}</size>");
        var errs = Check.PropertyErrors(D, d);
        if (errs.Count > 0) b.AddLabel($"<size=13><color=#FF8A6B>{Domain.L.Join("seq", errs)}</color></size>");
        b.BeginRow(6);
        if (_orig != null) b.AddButton(Domain.L.T("ui.delete"), Ui.A(Delete));
        b.AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(Id))).AddButton(Domain.L.T("ui.save"), Ui.A(Save)).End();
        b.Show();
    }

    private static string Sample(PropertyDef d)
    {
        try
        {
            var w = RulesUi.World;
            var ctx = new EvalCtx(w, new RulesData { Objects = D.Objects, Properties = new List<PropertyDef> { d } });
            foreach (var it in w.Shop())
                if (Catalog.Matches(w, it, d.ItemKind))
                    return $"{w.Describe(it)} → {Eval.Prop(ctx, it, d.Id)?.ToString("0.##") ?? Domain.L.T("prop_dlg.sample_empty")}";
        }
        catch { }
        return Domain.L.T("prop_dlg.sample_none");
    }

    private static void Save()
    {
        var d = _d;
        if (string.IsNullOrWhiteSpace(d.Name)) { Toast.Show(Domain.L.T("prop_dlg.name_empty")); return; }
        if (Check.PropertyErrors(D, d).Count > 0) { Toast.Show(Domain.L.T("prop_dlg.formula_error")); return; }
        d.Name = d.Name.Trim();
        if (_orig == null) D.Properties.Add(d);
        else D.Properties[D.Properties.IndexOf(_orig)] = d;
        Ui.Close(Id);
        ScheduleEditor.Refresh();
    }

    private static void Delete()
    {
        var refs = Check.Referrers(D, _orig.Id);
        if (refs.Count > 0) { Toast.Show(Domain.L.T("prop_dlg.delete_still_used", Domain.L.Join("list", refs.Take(4)))); return; }
        D.Properties.Remove(_orig);
        Ui.Close(Id);
        ScheduleEditor.Refresh();
    }
}
