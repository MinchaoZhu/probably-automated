using System;
using System.Linq;
using UnityEngine;

namespace AutoMod.UI.Kit;

/// <summary>
/// 表格式列表：表头一行；每项一块底色交替的面板，选中的高亮。
/// 名字可能很长的列表用两行：第一行名字（占满整行）和状态，第二行按列对齐的信息；名字短的用一行。
/// 列里的文字超出列宽时截断加“…”，悬停显示全文。
/// </summary>
public static class Table
{
    public static readonly Color HeadBg = new(0.13f, 0.12f, 0.16f, 0.92f);
    public static readonly Color RowA = new(0.20f, 0.19f, 0.24f, 1f);
    public static readonly Color RowB = new(0.17f, 0.16f, 0.20f, 1f);
    public static readonly Color RowSel = new(0.36f, 0.42f, 0.50f, 1f);

    /// <summary>表头：一行灰色小字，各列宽度和第二行（或单行）的列一致。lead：前面空出的宽度（单行列表前面有图标时）。</summary>
    public static void Header(CustomUIBuilder b, float lead, params (string label, float width)[] cols)
    {
        b.BeginPanel(-1, -1, TextAnchor.MiddleLeft, 6, 0);
        b.Last.SetImageColor(HeadBg);
        b.BeginRow(8, TextAnchor.MiddleLeft);
        if (lead > 0) Gap(b, lead);
        foreach (var (label, width) in cols)
        {
            if (width > 0)
            {
                string shown = Fit(label, width, 11, out bool cut);
                b.AddLabel(Tone.Small(Tone.Dim(shown))).Width(width);
                if (cut) b.Tip(label);
            }
            else
            {
                b.AddLabel(Tone.Small(Tone.Dim(label)));
            }
        }
        b.AddFlexibleSpace().End();
        b.End();
    }

    /// <summary>开始一项（底色按序号交替，选中的高亮）；之后加一行或两行，最后 End。</summary>
    public static void BeginItem(CustomUIBuilder b, int index, bool selected)
    {
        b.BeginPanel(-1, -1, TextAnchor.UpperLeft, 6, 2);
        b.Last.SetImageColor(selected ? RowSel : index % 2 == 0 ? RowA : RowB);
    }

    public static void EndItem(CustomUIBuilder b) => b.End();

    /// <summary>展开 / 收起这一项的文字按钮（放在第二行右侧，和“编辑”等按钮一起）。</summary>
    public static CustomUIBuilder More(CustomUIBuilder b, bool open, Action toggle) =>
        b.AddButton(open ? Domain.L.T("ui.collapse") : Domain.L.T("table.btn_details"), Ui.A(toggle))
         .Width(Domain.L.Int("layout.btn_more_width", 95));

    /// <summary>第二行开始：列和表头对齐。</summary>
    public static void BeginSecondLine(CustomUIBuilder b) => b.BeginRow(8, TextAnchor.MiddleLeft);

    /// <summary>固定宽度的空白（构建器的 AddSpace 只设首选宽度，一行放不下时会先被压没，后面的列就对不齐了）。</summary>
    public static CustomUIBuilder Gap(CustomUIBuilder b, float width) => b.AddLabel("").Width(width);

    /// <summary>
    /// 一格：固定宽度，小字。text 是纯文字（不带格式标签），超出宽度时截断加“…”并悬停显示全文；
    /// style 给截断后的文字加颜色等格式。
    /// </summary>
    public static CustomUIBuilder Cell(CustomUIBuilder b, string text, float width, Func<string, string> style = null, int size = 13, string tip = null)
    {
        string shown = Fit(text ?? "", width, size, out bool cut);
        b.AddLabel(Tone.Small(style == null ? shown : style(shown))).Width(width);
        string t = cut ? text + (tip == null ? "" : "\n" + tip) : tip;
        return t == null ? b : b.Tip(t);
    }

    /// <summary>状态：彩色小字（不截断，放在第一行右侧）。</summary>
    public static string Status(string text, string color) => Tone.Small(Tone.Color(text, color));

    /// <summary>
    /// 按宽度截断：汉字、全角符号约等于字号宽，其他字符约半个字号。放不下时留出“…”的位置。
    /// </summary>
    public static string Fit(string text, float width, int size, out bool cut)
    {
        float W(char c) => c > 0x2E7F ? size : size * 0.56f;
        float total = text.Sum(W);
        cut = total > width;
        if (!cut) return text;
        float room = width - size, used = 0;
        int n = 0;
        while (n < text.Length && used + W(text[n]) <= room) used += W(text[n++]);
        return text.Substring(0, n) + "…";
    }
}
