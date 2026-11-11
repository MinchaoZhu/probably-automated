using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.App;
using AutoMod.Domain.Rules;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Schedules;

/// <summary>“新建排班”：空白，或者从示例开始。每项一个图标和名字，做什么写在悬停提示里（任务顺序）。</summary>
internal static class NewScheduleWindow
{
    private const string Id = "am_newsch";

    public static void Show(Action blank, Action battery, Action<string> profession, Action import)
    {
        int btnW = Domain.L.Int("layout.new_schedule.btn_width", 170);
        int colW = btnW + 60;
        int winW = colW * 2 + 60;
        var b = Ui.Window(Id, Domain.L.T("schedule.new_title"), winW, 370);
        b.BeginGrid(2, colW, 44, 8);
        // 第一行：空白、最简单的自动换电
        Item(b, null, Domain.L.T("schedule.new_blank"), Domain.L.T("schedule.new_blank_tip"), blank, btnW);
        var bat = Examples.BatteryRotation(new RulesData());
        Item(b, "recharger", Examples.BatteryName, bat.Description + "\n" + Flow(bat), battery, btnW);
        foreach (var spec in ProfessionExamples.All)
        {
            string id = spec.Id;
            var s = ProfessionExamples.Build(new RulesData(), id);
            Item(b, spec.MachineKinds[0], spec.Name, s.Description + "\n" + Flow(s), () => profession(id), btnW);
        }
        b.End();
        b.AddFlexibleSpace();
        b.BeginRow(8)
            .AddButton(Domain.L.T("schedule.import_clipboard"), Ui.A(() => { Ui.Close(Id); import(); })).Tip(Domain.L.T("schedule.import_clipboard_tip"))
            .AddFlexibleSpace().AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(Id))).End();
        b.Show();
    }

    private static string Flow(Schedule s) => string.Join(" → ", s.Rules.Select(r => r.Name));

    private static void Item(CustomUIBuilder b, string kind, string name, string tip, Action act, int btnWidth)
    {
        b.BeginRow(8, UnityEngine.TextAnchor.MiddleLeft);
        var icon = kind == null ? null : Icons.Kind(kind);
        if (icon != null) b.AddImage(icon, 32, 32); else b.AddSpace(32);
        b.AddButton(name, Ui.A(() => { Ui.Close(Id); act(); })).Width(btnWidth).Tip(tip);
        b.End();
    }
}
