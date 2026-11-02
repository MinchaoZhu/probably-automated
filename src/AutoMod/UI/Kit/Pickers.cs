using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Reports;
using AutoMod.Domain.Workforce;

namespace AutoMod.UI.Kit;

/// <summary>
/// 点选模式：玩家双击打开的下一个容器会被当作选中的对象。
/// </summary>
public static class PickMode
{
    private const string Banner = "am_banner";
    private static Action<GameItem> _pending;

    public static bool Active => _pending != null;

    /// <summary>界面启用时登记：点选模式排在窗口打开处理的最前面。</summary>
    public static void Subscribe() => GameWindows.Opened.Insert(0, (owner, _) => TryConsume(owner));

    public static void Begin(string prompt, Action<GameItem> onPicked)
    {
        _pending = onPicked;
        Ui.Window(Banner, Domain.L.T("picker.pick_in_shop"), 420, 100, escape: false)
            .BeginRow(8).AddLabel(prompt).AddFlexibleSpace().AddButton(Domain.L.T("ui.cancel"), Ui.A(Cancel)).End()
            .Show();
    }

    public static void Cancel()
    {
        _pending = null;
        Ui.Close(Banner);
    }

    /// <summary>点选模式中打开了一个容器：选中它。返回 true 表示这次打开被消费了。</summary>
    private static bool TryConsume(GameItem container)
    {
        if (_pending == null || container == null) return false;
        var cb = _pending;
        _pending = null;
        Ui.Close(Banner);
        Ui.Guard(() => cb(container));
        return true;
    }
}

public static class Pickers
{
    private const string Id = "am_pick";

    /// <summary>通用列表选择。</summary>
    public static void Choose(string title, IEnumerable<(string label, Action action)> options, string note = null)
    {
        var list = options.ToList();
        // 宽度跟着最长的选项走，避免中文选项被截断
        float w = Math.Clamp(80 + list.Select(o => o.label.Length).DefaultIfEmpty(10).Max() * 17, 320, 760);
        var b = Ui.Window(Id, title, w, 380);
        if (note != null) b.AddLabel(note);
        b.BeginScroll(300);
        foreach (var (label, action) in list)
        {
            var act = action;
            b.AddButton(label, Ui.A(() => { Ui.Close(Id); act(); }));
        }
        b.End().BeginRow(8).AddFlexibleSpace().AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(Id))).End().Show();
    }

    /// <summary>输入一个数字的小窗。</summary>
    public static void Number(string title, double initial, Action<double> onDone)
    {
        string text = initial.ToString("0.##");
        Ui.Window(Id, title, 360, 150)
            .BeginRow(6).AddLabel(Domain.L.T("picker.number")).AddInput("0", text, Ui.AS(v => text = v)).End()
            .BeginRow(8).AddFlexibleSpace()
            .AddButton(Domain.L.T("ui.cancel"), Ui.A(() => Ui.Close(Id)))
            .AddButton(Domain.L.T("ui.confirm"), Ui.A(() =>
            {
                if (!double.TryParse(text, out var v)) return;
                Ui.Close(Id);
                onDone(v);
            }))
            .End().Show();
    }

    /// <summary>确认小窗：标题是问题，下面几行说明，右下角按钮（no 为 default 时只有一个按钮）。</summary>
    public static void Confirm(string question, IEnumerable<string> reasons, (string label, Action act) yes, (string label, Action act) no)
    {
        var lines = reasons.ToList();
        var b = Ui.Window(Id, question, 440, 130 + 26 * lines.Count);
        foreach (var l in lines) b.AddLabel(l);
        b.AddFlexibleSpace();
        b.BeginRow(8).AddFlexibleSpace();
        if (no.label != null) b.AddButton(no.label, Ui.A(() => { Ui.Close(Id); no.act?.Invoke(); }));
        b.AddButton(yes.label, Ui.A(() => { Ui.Close(Id); yes.act?.Invoke(); }));
        b.End().Show();
    }
}

/// <summary>只读的几行文字（列表、说明）。</summary>
public static class LinesWindow
{
    public static void Show(string title, IEnumerable<string> lines)
    {
        var b = Ui.Window("am_lines", title, 520, 420).BeginScroll(340);
        foreach (var l in lines) b.AddLabel(l);
        b.End().BeginRow(8).AddFlexibleSpace().AddButton(Domain.L.T("ui.close"), Ui.A(() => Ui.Close("am_lines"))).End().Show();
    }
}
