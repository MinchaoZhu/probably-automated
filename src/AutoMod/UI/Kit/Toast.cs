using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AutoMod.Domain.Rules;
using AutoMod.Domain.Workforce;

namespace AutoMod.UI.Kit;

/// <summary>屏幕顶部的短提示。</summary>
public static class Toast
{
    public static void Show(string text)
    {
        // 高度随文字行数（每行约 30 个字）
        int lines = text.Split('\n').Sum(l => Math.Max(1, (l.Length + 27) / 28));
        Ui.Window("am_toastmsg", "", 480, 100 + 26 * lines).AddLabel(text)
            .BeginRow(8).AddFlexibleSpace().AddButton(Domain.L.T("ui.ok"), Ui.A(() => Ui.Close("am_toastmsg"))).End().Show();
    }
}
