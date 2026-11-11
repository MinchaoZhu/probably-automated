using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using AutoMod.Adapter;
using AutoMod.App;
using AutoMod.UI.Kit;
using AutoMod.UI.Store;

namespace AutoMod.UI.Schedules;

/// <summary>
/// 参数对话框：类型（数值 / 设备 / 列表）、名称、值（数值和单位，或选一个设备名 / 筛选器）、说明；范围（共用 / 本排班，可改成共用）。
/// 已经被用到的参数不能改类型。
/// </summary>
public static class ObjectDialog
{
    private const string Id = "am_objdlg";
    private static ObjectDef _d, _orig;
    private static string _valueText;

    private static RulesData D => RulesUi.D;

    /// <param name="owner">所属排班 Id；为空 = 共用。</param>
    public static void New(string owner = null)
    {
        _orig = null;
        string name = Domain.L.T("object_dlg.default_name");
        for (int i = 2; Scope.NameConflict(D, false, name, owner) != null; i++) name = Domain.L.T("object_dlg.default_name_seq", i);
        _d = new ObjectDef { Id = Defs.NewId("o"), Name = name, Owner = owner };
        _valueText = "0";
        Show();
    }

    public static void Edit(ObjectDef o)
    {
        _orig = o;
        _d = Defs.Clone(o);
        _valueText = o.Value.ToString("0.##");
        Show();
    }

    public static void Show()
    {
        var d = _d;
        var b = Ui.Window(Id, _orig == null ? Domain.L.T("object_dlg.title_new") : Domain.L.T("object_dlg.title_edit", _orig.Name), 500, 320);
        var users = _orig == null ? new List<(Schedule, WorkRule)>() : Check.UsedBy(D, _orig.Id);
        bool used = _orig != null && (users.Count > 0 || Check.Referrers(D, _orig.Id).Count > 0);
        var types = new[] { (ParamType.Number, Domain.L.T("object_dlg.type_number")), (ParamType.Device, Domain.L.T("object_dlg.type_device")), (ParamType.List, Domain.L.T("object_dlg.type_list")) };
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Domain.L.T("object_dlg.type")).Width(50);
        if (used) b.AddLabel(types.First(t => t.Item1 == d.Type).Item2).Tip(Domain.L.T("object_dlg.type_locked"));
        else b.AddDropdown(Ui.L(types.Select(t => t.Item2)), Array.FindIndex(types, t => t.Item1 == d.Type), Ui.AI(k => { d.Type = types[k].Item1; d.Ref = null; Show(); })).Width(160);
        b.End();
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Domain.L.T("ui.name")).Width(50).AddInput(Domain.L.T("ui.name"), d.Name, Ui.AS(v => d.Name = v)).End();
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Tone.Dim(ScopeText(D, d.Owner)));
        if (_orig != null && d.Owner != null) b.AddButton(Domain.L.T("object_dlg.btn_promote"), Ui.A(() => Promote(_orig.Id)));
        b.End();
        if (!d.IsRef)
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Domain.L.T("ui.value")).Width(50).AddInput("0", _valueText, Ui.AS(v => _valueText = v)).Width(120)
                .AddLabel(Domain.L.T("object_dlg.unit")).AddInput(Domain.L.T("object_dlg.unit_placeholder"), d.Unit ?? "", Ui.AS(v => d.Unit = string.IsNullOrWhiteSpace(v) ? null : v.Trim())).Width(100).End();
        else
        {
            // 设备：店里的设备名；列表：这个参数能看到的筛选器（本流程的和全局的，全局参数只能选全局的）
            var opts = (d.Type == ParamType.Device ? RulesUi.DeviceNames() : Scope.Selectors(D, d.Owner).Where(x => !x.IsNumber).Select(x => (x.Id, x.Name)).ToList()).ToList();
            opts.Insert(0, (null, Domain.L.T("object_dlg.pick_value")));
            b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Domain.L.T("object_dlg.value_ref")).Width(50)
                .AddDropdown(Ui.L(opts.Select(o => o.Item2)), Math.Max(0, opts.FindIndex(o => o.Item1 == d.Ref)), Ui.AI(k => d.Ref = opts[k].Item1)).Searchable().Width(320).End();
        }
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft).AddLabel(Domain.L.T("ui.hint")).Width(50).AddInput(Domain.L.T("object_dlg.hint_placeholder"), d.Hint ?? "", Ui.AS(v => d.Hint = string.IsNullOrWhiteSpace(v) ? null : v.Trim())).End();
        b.AddFlexibleSpace();
        b.BeginRow(6, UnityEngine.TextAnchor.MiddleLeft);
        if (_orig != null) b.AddButton(Domain.L.T("ui.delete"), Ui.A(Delete));
        b.AddLabel(Tone.Small(Tone.Dim(users.Count > 0 ? Domain.L.N("object_dlg.used_by_rules", users.Count) : "")))
            .AddFlexibleSpace().AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(Id))).AddButton(Domain.L.T("ui.save"), Ui.A(Save)).End();
        b.Show();
    }

    private static void Save()
    {
        var d = _d;
        if (string.IsNullOrWhiteSpace(d.Name)) { Toast.Show(Domain.L.T("object_dlg.name_empty")); return; }
        d.Name = d.Name.Trim();
        if (Scope.NameConflict(D, false, d.Name, d.Owner, d.Id) is { } conflict) { Toast.Show(conflict + "。"); return; }
        if (d.IsRef)
        {
            if (string.IsNullOrEmpty(d.Ref)) { Toast.Show(Domain.L.T("object_dlg.value_missing")); return; }
            d.Value = 0;
            d.Unit = null;
        }
        else
        {
            if (!double.TryParse(_valueText, out var v)) { Toast.Show(Domain.L.T("object_dlg.value_nan")); return; }
            d.Value = v;
            d.Ref = null;
        }
        if (_orig == null) D.Objects.Add(d);
        else D.Objects[D.Objects.IndexOf(_orig)] = d;
        Ui.Close(Id);
        ScheduleEditor.Refresh();
        SchedulePage.Refresh();
    }

    /// <summary>“全局”或“只给「流程名」用”。</summary>
    public static string ScopeText(RulesData d, string owner) =>
        owner == null ? Domain.L.T("object_dlg.scope_global") : Domain.L.T("object_dlg.scope_owner", d.Schedules.FirstOrDefault(s => s.Id == owner)?.Name);

    /// <summary>改成全局：确认框列出一起改的定义；重名时不改。</summary>
    public static void Promote(string id)
    {
        var set = Scope.PromoteSet(D, id);
        Pickers.Confirm(Domain.L.T("object_dlg.promote_title"), new[] { Domain.L.T("object_dlg.promote_note"), Domain.L.Join("list", set.Select(x => RulesUi.Name(x))) },
            (Domain.L.T("object_dlg.btn_confirm_promote"), () =>
            {
                if (Scope.Promote(D, id) is { } why) { Toast.Show(why + "。"); return; }
                Ui.Close(Id);
                Ui.Close(SelectorDialog.DialogId);
                ScheduleEditor.Refresh();
                SchedulePage.Refresh();
            }), (Domain.L.T("ui.cancel"), () => { }));
    }

    private static void Delete()
    {
        var refs = Check.Referrers(D, _orig.Id);
        if (refs.Count > 0) { Toast.Show(Domain.L.T("object_dlg.delete_still_used", Domain.L.Join("list", refs.Take(4)))); return; }
        D.Objects.Remove(_orig);
        Ui.Close(Id);
        ScheduleEditor.Refresh();
        SchedulePage.Refresh();
    }
}
