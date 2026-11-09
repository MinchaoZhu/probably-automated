using System.Collections.Generic;
using UnityEngine;
using AutoMod.Domain.Workforce;

namespace AutoMod.UI.Guide;

/// <summary>手册里的一块内容：横幅、条目（图标 + 小标题 + 一两句话）、编号步骤、橙色提示、示意图。</summary>
public class HandbookBlock
{
    public enum Kinds { Banner, Entry, Steps, Tip, Diagram }

    public Kinds Kind;
    public string Title = "";
    public string Text = "";
    public Color Color;

    /// <summary>条目的图标：游戏物品的原型 id（取它的贴图）。</summary>
    public string Icon;

    public HandbookDiagram Diagram;
}

/// <summary>示意图：方框（店里的东西）和带标签的箭头（任务）。坐标以内容区左上角为原点，向右、向下为正，宽 470。</summary>
public class HandbookDiagram
{
    /// <summary>物品图：游戏的精灵名，相对方框图区中心的偏移（像素，放大 2 倍后），按顺序叠放。</summary>
    public record Art(string Sprite, float Dx = 0, float Dy = 0);

    /// <summary>方框：上面是物品图（Art，放大 2 倍），下面是带底色的名称和说明。</summary>
    public record Node(string Title, string Note, float X, float Y, float W, float H, Color Color, Art[] Arts = null);

    /// <summary>箭头从 (X1, Y1) 指向 (X2, Y2)；标签放在 (LX, LY)，宽度为 W，Right = 标签的右边对齐到 LX。</summary>
    public record Arrow(float X1, float Y1, float X2, float Y2, string Label, float LX, float LY, float W = 150f, bool Right = false);

    public float Height;
    public List<Node> Nodes = new();
    public List<Arrow> Arrows = new();
}

/// <summary>
/// 学徒手册的内容：照游戏自己的手册（水培农业手册、化学入门指南）写——彩色横幅分节，每条一个物品图标、
/// 一个下划线小标题、一两句话，每页一句橙色提示。界面上不写规则，规则都在这里。
/// </summary>
public static class HandbookPages
{
    public static string Title => Domain.L.T("handbook.title");

    /// <summary>页码（从 0 开始），界面上的“?”按钮按主题跳转。</summary>
    public const int Apprentices = 0, Ap = 0, Proficiency = 1, Schedules = 2, Definitions = 2, Tasks = 3, Rotation = 4, Examples = 5, Results = 6;

    // 横幅颜色取自游戏的手册：绿（快速入门）、棕（工具与机器）；提示句是橙色
    public static readonly Color Green = new(0.478f, 0.604f, 0.306f), Brown = new(0.722f, 0.518f, 0.345f);
    public static readonly Color Teal = new(0.506f, 0.667f, 0.655f);

    private static HandbookBlock Banner(string t, Color c) => new() { Kind = HandbookBlock.Kinds.Banner, Text = t, Color = c };
    private static HandbookBlock Entry(string icon, string title, string text) => new() { Kind = HandbookBlock.Kinds.Entry, Icon = icon, Title = title, Text = text };
    private static HandbookBlock Steps(string t) => new() { Kind = HandbookBlock.Kinds.Steps, Text = t };
    private static HandbookBlock Tip(string t) => new() { Kind = HandbookBlock.Kinds.Tip, Text = t };
    private static HandbookBlock Diagram(HandbookDiagram d) => new() { Kind = HandbookBlock.Kinds.Diagram, Diagram = d };

    /// <summary>
    /// 自动换电示意图：充能器、用电机器、储藏区三个方框（用游戏里的物品图），电池沿两个任务流动。
    /// </summary>
    private static HandbookDiagram BatteryLoop()
    {
        var A = (System.Func<string, float, float, HandbookDiagram.Art>)((n, x, y) => new HandbookDiagram.Art(n, x, y));
        float lxFull = Domain.L.Float("layout.handbook.diagram.arrow_full_x", Domain.L.Float("layout.handbook.p5_label_x", 156f));
        float lxEmpty = Domain.L.Float("layout.handbook.diagram.arrow_empty_x", Domain.L.Float("layout.handbook.p5_label_x", 156f));
        float lyFull = Domain.L.Float("layout.handbook.diagram.arrow_full_y", Domain.L.Float("layout.handbook.p5_full_y", 6f));
        float lyEmpty = Domain.L.Float("layout.handbook.diagram.arrow_empty_y", Domain.L.Float("layout.handbook.p5_empty_y", 56f));
        float lwFull = Domain.L.Float("layout.handbook.diagram.arrow_full_w", 160f);
        float lwEmpty = Domain.L.Float("layout.handbook.diagram.arrow_empty_w", 160f);

        float lxCharge = Domain.L.Float("layout.handbook.diagram.arrow_charge_x", 84f);
        float lyCharge = Domain.L.Float("layout.handbook.diagram.arrow_charge_y", 151f);
        float lwCharge = Domain.L.Float("layout.handbook.diagram.arrow_charge_w", 230f);

        float rw = Domain.L.Float("layout.handbook.diagram.recharger_w", 150f);
        float mx = Domain.L.Float("layout.handbook.diagram.machines_x", 320f);
        float mw = Domain.L.Float("layout.handbook.diagram.machines_w", 150f);
        float sw = Domain.L.Float("layout.handbook.diagram.storage_w", 150f);

        return new()
        {
            Height = 300,
            Nodes =
            {
                new(Domain.L.T("handbook.diagram.recharger"), Domain.L.T("handbook.diagram.recharger_note"), 0, 0, rw, 110, Teal, new[] { A("recharger_base", 0, 0), A("battery20", -24, -4), A("battery100", 8, -4) }),
                new(Domain.L.T("handbook.diagram.machines"), Domain.L.T("handbook.diagram.machines_note"), mx, 0, mw, 110, Teal, new[] { A("projector", 0, 0) }),
                new(Domain.L.T("handbook.diagram.storage"), Domain.L.T("handbook.diagram.storage_note"), 0, 190, sw, 110, Brown, new[] { A("storage_bay_large", 0, 0), A("battery20", 30, 20) }),
            },
            Arrows =
            {
                new(152, 30, 318, 30, Domain.L.T("handbook.diagram.arrow_full"), lxFull, lyFull, lwFull),
                new(318, 80, 152, 80, Domain.L.T("handbook.diagram.arrow_empty"), lxEmpty, lyEmpty, lwEmpty),
                new(75, 188, 75, 114, Domain.L.T("handbook.diagram.arrow_charge"), lxCharge, lyCharge, lwCharge),
            },
        };
    }

    public static List<List<HandbookBlock>> All => new()
    {
        // 1 快速上手、学徒（AP、多个流程）
        new()
        {
            Banner(Domain.L.T("handbook.p1.banner_quickstart"), Green),
            Steps(Domain.L.T("handbook.p1.steps_quickstart")),
            Tip(Domain.L.T("handbook.p1.tip_payroll")),
            Banner(Domain.L.T("handbook.p1.banner_apprentices"), Brown),
            Entry("caffeine_pill", Domain.L.T("handbook.p1.title_ap"), Domain.L.T("handbook.p1.text_ap")),
            Entry("toolbox", Domain.L.T("handbook.p1.title_multi_sched"), Domain.L.T("handbook.p1.text_multi_sched")),
        },
        // 2 熟练度与等级
        new()
        {
            Banner(Domain.L.T("handbook.p2.banner"), Green),
            Entry("chemistry_guide", Domain.L.T("handbook.p2.title_prof"), Domain.L.T("handbook.p2.text_prof")),
            Entry("handnote", Domain.L.T("handbook.p2.title_switch"), Domain.L.T("handbook.p2.text_switch")),
            Entry("newspaper", Domain.L.T("handbook.p2.title_level"), Domain.L.T("handbook.p2.text_level", Scales.DailyAp(SkillMath.StartAp + SkillMath.ApPerLevel) - Scales.DailyAp(SkillMath.StartAp), ApprenticeFactory.WageOf(new Apprentice { Level = 2 }) - ApprenticeFactory.WageOf(new Apprentice { Level = 1 }))),
            Tip(Domain.L.T("handbook.p2.tip")),
        },
        // 3 全店、设备名、筛选器、参数
        new()
        {
            Banner(Domain.L.T("handbook.p3.banner"), Green),
            Entry("storage_bay_large", Domain.L.T("handbook.p3.title_scope"), Domain.L.T("handbook.p3.text_scope")),
            Entry("water_jug", Domain.L.T("handbook.p3.title_filter"), Domain.L.T("handbook.p3.text_filter")),
            Tip(Domain.L.T("handbook.p3.tip_name")),
            Entry("recharger", Domain.L.T("handbook.p3.title_range"), Domain.L.T("handbook.p3.text_range")),
            Steps(Domain.L.T("handbook.p3.steps_pick")),
            Entry("handnote", Domain.L.T("handbook.p3.title_param"), Domain.L.T("handbook.p3.text_param")),
            Steps(Domain.L.T("handbook.p3.steps_conds")),
        },
        // 4 任务和动作
        new()
        {
            Banner(Domain.L.T("handbook.p4.banner"), Brown),
            Entry("newspaper", Domain.L.T("handbook.p4.title_pre"), Domain.L.T("handbook.p4.text_pre")),
            Entry("storage_bay_large", Domain.L.T("handbook.p4.title_move"), Domain.L.T("handbook.p4.text_move")),
            Entry("energy_credit", Domain.L.T("handbook.p4.title_swap"), Domain.L.T("handbook.p4.text_swap")),
            Entry("kitchen_cleaver", Domain.L.T("handbook.p4.title_use"), Domain.L.T("handbook.p4.text_use")),
            Entry("bottle_printer", Domain.L.T("handbook.p4.title_press"), Domain.L.T("handbook.p4.text_press")),
            Tip(Domain.L.T("handbook.p4.tip")),
            Tip(Domain.L.T("handbook.p4.tip_sub")),
        },
        // 5 自动换电、执行顺序
        new()
        {
            Banner(Domain.L.T("handbook.p5.banner_swap"), Green),
            Diagram(BatteryLoop()),
            Steps(Domain.L.T("handbook.p5.steps_swap")),
            Banner(Domain.L.T("handbook.p5.banner_order"), Brown),
            Entry("toolbox", Domain.L.T("handbook.p5.title_seq"), Domain.L.T("handbook.p5.text_seq")),
        },
        // 6 示例
        new()
        {
            Banner(Domain.L.T("handbook.p6.banner"), Green),
            Entry("recharger", Domain.L.T("handbook.p6.title_battery"), Domain.L.T("handbook.p6.text_battery")),
            Entry("storage_bay_large", Domain.L.T("handbook.p6.title_organize"), Domain.L.T("handbook.p6.text_organize")),
            Entry("hydroponic", Domain.L.T("handbook.p6.title_hydro"), Domain.L.T("handbook.p6.text_hydro")),
            Entry("ras", Domain.L.T("handbook.p6.title_ras"), Domain.L.T("handbook.p6.text_ras")),
            Entry("animal_cage", Domain.L.T("handbook.p6.title_rat"), Domain.L.T("handbook.p6.text_rat")),
            Entry("printer", Domain.L.T("handbook.p6.title_print"), Domain.L.T("handbook.p6.text_print")),
            Entry("vending_fountain", Domain.L.T("handbook.p6.title_vending"), Domain.L.T("handbook.p6.text_vending")),
            Tip(Domain.L.T("handbook.p6.tip")),
        },
        // 7 预演和报告
        new()
        {
            Banner(Domain.L.T("handbook.p7.banner"), Brown),
            Entry("magnifier", Domain.L.T("handbook.p7.title_sim"), Domain.L.T("handbook.p7.text_sim")),
            Entry("newspaper", Domain.L.T("handbook.p7.title_report"), Domain.L.T("handbook.p7.text_report")),
            Tip(Domain.L.T("handbook.p7.tip")),
        },
    };
}
