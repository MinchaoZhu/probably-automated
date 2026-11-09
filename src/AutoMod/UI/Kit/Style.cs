using System;
using UnityEngine;

namespace AutoMod.UI.Kit;

/// <summary>界面的颜色和几种常用写法（窗口统一用游戏的机器面板样式，见 Ui.Style）。</summary>
public static class Tone
{
    public const string Ok = "#7FD18A";
    public const string Warn = "#F0C060";
    public const string Bad = "#FF7A6B";
    public const string Muted = "#9AA5A0";

    public static string Color(string text, string color) => $"<color={color}>{text}</color>";
    public static string Dim(string text) => Color(text, Muted);
    public static string Small(string text) => $"<size=13>{text}</size>";
    public static string Bold(string text) => $"<b>{text}</b>";

    public static string Money(int n) => Domain.L.T("style.credits", n);
}

/// <summary>在构建器上加的几种组合控件。</summary>
public static class UiExt
{
    /// <summary>一排页签：当前的那个按钮不可点（和游戏自带的页签一样）。</summary>
    public static CustomUIBuilder Tabs(this CustomUIBuilder b, string current, params (string id, string label, Action act)[] tabs)
    {
        b.BeginRow(4);
        foreach (var (id, label, act) in tabs)
        {
            b.AddButton(label, Ui.A(act));
            if (id == current) b.Last?.SetInteractable(false);
        }
        return b.End();
    }

    /// <summary>分组标题：一行加粗文字，前面空一点。</summary>
    public static CustomUIBuilder Section(this CustomUIBuilder b, string title, string right = null)
    {
        b.AddSpace(4);
        if (right == null) return b.AddLabel(Tone.Bold(title));
        return b.BeginRow(8).AddLabel(Tone.Bold(title)).AddFlexibleSpace().AddLabel(Tone.Dim(right)).End();
    }

    /// <summary>最近加的下拉框：列表顶部有搜索框，展开时滚动到当前项。</summary>
    /// <param name="group">第几行属于哪个大类；传了才有大类按钮。</param>
    /// <param name="pinned">搜索时固定显示的行。</param>
    public static CustomUIBuilder Searchable(this CustomUIBuilder b, Func<int, string> group = null, params int[] pinned)
    {
        DropdownSearch.Attach(b.Last?.dropdown, group, pinned);
        return b;
    }

    /// <summary>最近加的元素：固定宽度。</summary>
    public static CustomUIBuilder Width(this CustomUIBuilder b, float w)
    {
        b.Last?.SetFixedWidth(w);
        return b;
    }

    /// <summary>最近加的元素：悬停提示（null 不加）。</summary>
    public static CustomUIBuilder Tip(this CustomUIBuilder b, string tip) => tip == null ? b : b.WithTooltip(tip);

    /// <summary>物品图标（取不到时什么都不加）。</summary>
    public static CustomUIBuilder Icon(this CustomUIBuilder b, Sprite sprite, float size = 24)
    {
        if (sprite != null) b.AddImage(sprite, size, size);
        return b;
    }
}
