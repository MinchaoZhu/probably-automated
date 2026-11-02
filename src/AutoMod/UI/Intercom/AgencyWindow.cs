using System;
using System.Linq;
using AutoMod.Domain.Persistence;
using AutoMod.Domain.Workforce;
using AutoMod.Adapter;
using AutoMod.App;
using AutoMod.UI.Kit;
using AutoMod.UI.Store;

namespace AutoMod.UI.Intercom;

/// <summary>呼叫中介窗口：雇用学徒（固定 Lv1、每天 25 AP）、扩编名额。</summary>
public static class AgencyWindow
{
    private const string Id = "am_agency";
    private static readonly Random Rng = new();
    private static string _msg;

    public static void Show()
    {
        var st = Automation.State;
        var b = Ui.Window(Id, Domain.L.T("agency.title"), 520, 300);
        b.AddLabel(Tone.Dim(Domain.L.T("agency.header_stats", Cash.Current, st.Apprentices.Count, st.Slots)));
        b.AddSeparator();
        int wage = ApprenticeFactory.WageOf(new Apprentice { Level = 1 });
        b.AddLabel(Domain.L.T("agency.new_apprentice_info", 1, SkillMath.StartAp, wage, Proficiency.Floor(1)));
        b.AddLabel(Tone.Small(Tone.Dim(Domain.L.T("agency.level_up_info", SkillMath.ApPerLevel, 10))));
        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
        if (Agency.CanHire(st)) b.AddButton(Domain.L.T("agency.btn_hire", Agency.HireFee), Ui.A(Hire)).Tip(Domain.L.T("agency.tip_hire"));
        else b.AddLabel(Tone.Color(Domain.L.T("agency.slots_full"), Tone.Warn));
        b.End();
        if (_msg != null) b.AddLabel(Tone.Small(_msg));
        b.AddFlexibleSpace();
        b.AddSeparator();
        int expand = Agency.ExpandFee(st.Slots);
        b.BeginRow(8);
        if (st.Slots >= ModState.MaxSlots || expand <= 0) b.AddLabel(Tone.Dim(Domain.L.T("agency.slots_maxed", ModState.MaxSlots)));
        else b.AddLabel(Tone.Dim(Domain.L.T("agency.slots_expand", st.Slots, st.Slots + 1))).AddButton(Domain.L.T("agency.btn_expand", expand), Ui.A(Expand));
        b.AddFlexibleSpace().AddButton(Domain.L.T("agency.btn_return"), Ui.A(() => { Ui.Close(Id); MainWindow.Show("apprentices"); }));
        b.End();
        b.Show();
    }

    private static void Hire()
    {
        var st = Automation.State;
        if (!Agency.CanHire(st)) { _msg = Tone.Color(Domain.L.T("agency.slots_full"), Tone.Warn); Show(); return; }
        int fee = Agency.HireFee;
        if (!Cash.TryPay(fee, Domain.L.T("agency.fee_hire"))) { _msg = Tone.Color(Domain.L.T("agency.cash_insufficient", fee), Tone.Bad); Show(); return; }
        var a = Agency.Hire(st, Rng, StoreStation.GetDayCounter());
        _msg = Tone.Color(Domain.L.T("agency.hired_msg", a.Name), Tone.Ok);
        Refresh();
    }

    private static void Expand()
    {
        var st = Automation.State;
        int fee = Agency.ExpandFee(st.Slots);
        if (fee <= 0) return;
        if (!Cash.TryPay(fee, Domain.L.T("agency.fee_expand"))) { _msg = Tone.Color(Domain.L.T("agency.cash_insufficient", fee), Tone.Bad); Show(); return; }
        st.Slots++;
        _msg = null;
        Refresh();
    }

    /// <summary>刷新自己，并同步对讲机窗口。重建的窗口会跑到最上层，所以最后重建自己。</summary>
    private static void Refresh()
    {
        if (Ui.IsOpen("am_main")) MainWindow.Show();
        Show();
    }
}

/// <summary>培训：付费买一份培训材料，学徒在下个上岗班次开始时阅读，加等级经验（不影响熟练度）。</summary>
public static class Training
{
    public static void Choose(Apprentice app)
    {
        if (app.Training != null) { Pickers.Confirm(Domain.L.T("agency.train_title", app.Name), new[] { Domain.L.T("agency.train_pending_read") }, (Domain.L.T("ui.confirm"), () => { }), default); return; }
        if (!Agency.CanTrain(app)) { Pickers.Confirm(Domain.L.T("agency.train_title", app.Name), new[] { Domain.L.T("agency.train_max_level", Agency.TrainingMaxLevel) }, (Domain.L.T("ui.confirm"), () => { }), default); return; }
        int fee = Agency.TrainingFee(app.Level);
        Pickers.Confirm(Domain.L.T("agency.train_title", app.Name),
            new[] { Domain.L.T("agency.train_desc", Agency.TrainingXp(app.Level), Agency.TrainingAp), Tone.Dim(Domain.L.T("agency.train_cash", Cash.Current)) },
            (Domain.L.T("agency.btn_buy_train", fee), () => Buy(app, fee)), (Domain.L.T("ui.cancel"), () => { }));
    }

    private static void Buy(Apprentice app, int fee)
    {
        if (!Cash.TryPay(fee, Domain.L.T("agency.fee_train_item", app.Name)))
        {
            Pickers.Confirm(Domain.L.T("agency.train_no_cash_title"), new[] { Domain.L.T("agency.train_no_cash_body", fee, Cash.Current) }, (Domain.L.T("ui.confirm"), () => { }), default);
            return;
        }
        Agency.BookTraining(app);
        MainWindow.Show("apprentices");
    }
}
