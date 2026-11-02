using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.App;
using AutoMod.Domain;
using AutoMod.Domain.Reports;
using AutoMod.UI.Kit;

namespace AutoMod.UI.Guide;

/// <summary>
/// 工作日报的纸面（游戏的 mod 纸质读物 PaperUI，和学徒手册同一套）：
/// 报头、要闻栏、流程一览横幅、问题横幅、学徒条、页脚。
/// 日常情况一页放下，内容为总结，不再逐条列任务。
/// </summary>
public static class DailyPaper
{
    private const string BookId = "daily";
    private const string Ink = "#3A3836", Ok = "#2F6B3A", Warn = "#8A5F0A", Bad = "#A3302A", Dim = "#6B665C", QuoteBg = "#C9C5A8";

    public static void Open()
    {
        var edition = Automation.State.Daily;
        if (edition == null) { Toast.Show(Domain.L.T("daily.cannot_open")); return; }
        PaperUI ui = null;
        try
        {
            Build(edition);
            ui = ModHelper.OpenPaper(ModInfo.Id, BookId);
        }
        catch (Exception ex) { Log.Warn($"[Daily] 生成工作日报失败: {ex}"); }
        if (ui == null) { Toast.Show(Domain.L.T("daily.cannot_open")); return; }
        ui.PageShown -= Handbook.Relayout;
        ui.PageShown += Handbook.Relayout;
        ui.FlipTo(0);
        Handbook.Relayout(ui, 0);
        Handbook.RaiseAboveModWindows(ui);
    }

    private static float EstWidth(string text, float size)
    {
        float w = 0;
        foreach (char c in text) w += c > 0x2E7F ? size : size * 0.56f;
        return w;
    }

    private static void Build(DailyEdition ed)
    {
        const float W = 628, H = 804, Left = 72, Top = 74, CW = 508, Bottom = 730;
        Handbook.ProvideMissingPaperSprites();
        var b = ModHelper.CreatePaper(ModInfo.Id, BookId, "notebook_manual");
        b.Data.title = Domain.L.T("daily.title");
        b.Data.designWidth = W;
        b.Data.designHeight = H;
        b.SetSize(W, H);
        b.SetChrome();

        string greenHex = Handbook.Hex(HandbookPages.Green);
        string brownHex = Handbook.Hex(HandbookPages.Brown);

        int pageNo = 0;
        float y = 0;

        void Page()
        {
            b.AddPage($"p{++pageNo}");
            if (pageNo == 1)
            {
                // 1. 报头：粗线、大标题、日期行、双细线（去掉报纸小图标，长标题可占满宽度）
                b.AddLine(Left, 40, Left + CW, 40, 4, Ink);

                float titleW = CW;
                string title = $"<b>{Domain.L.T("daily.title")}</b>";
                float ts = 30;
                while (ts > 18 && Handbook.TextHeight(title, PaperTextStyle.HEADER, ts, titleW) > ts * 1.9f) ts -= 2;
                // 出品方小字（模组名），放在粗线和大标题之间
                b.AddText(Left, 44, CW, 18, Domain.L.T("daily.publisher"), PaperTextStyle.CAPTION, align: "center", fontSize: 13);
                b.Last.color = Dim;
                b.AddText(Left, 62, titleW, 44, title, PaperTextStyle.HEADER, align: "center", fontSize: ts);
                b.Last.color = Ink;

                string dateline = ed.Night != null
                    ? Domain.L.T("daily.dateline", ed.Day)
                    : Domain.L.T("daily.dateline_morning", ed.Day);
                b.AddText(Left, 110, CW, 20, dateline, PaperTextStyle.CAPTION, align: "center", fontSize: 14);
                b.Last.color = Dim;

                b.AddLine(Left, 134, Left + CW, 134, 1.5f, Ink);
                b.AddLine(Left, 138, Left + CW, 138, 1.5f, Ink);
                y = 150;
            }
            else
            {
                string dateline = ed.Night != null
                    ? Domain.L.T("daily.dateline", ed.Day)
                    : Domain.L.T("daily.dateline_morning", ed.Day);
                b.AddText(Left, 34, CW, 24, $"{Domain.L.T("daily.title")} · {dateline}", PaperTextStyle.CAPTION, align: "center", fontSize: 14);
                b.Last.color = Dim;
                b.AddLine(Left, 62, Left + CW, 62, 1.5f, Ink);
                y = Top;
            }
        }

        Page();

        // 2. 要闻栏：一排 4 个格子（竖细线分隔），整栏高度 76，说明文字 13 号，数字 30 号
        {
            float cellW = CW / 4f;
            float statY = y;
            const float statH = 76f;

            // 竖向分隔线（3 条）
            for (int i = 1; i < 4; i++)
            {
                float lx = Left + cellW * i;
                b.AddLine(lx, statY + 6, lx, statY + statH - 6, 1f, "#D5CFC9");
            }

            // 格子 0：完成任务（件）
            DrawStatCell(0, ed.Done.ToString(), Ink, Domain.L.T("daily.stat.done"));

            // 格子 1：使用 AP（“x / y”）
            string apStr = $"{ed.ApUsed} / {ed.ApTotal}";
            DrawStatCell(1, apStr, Ink, Domain.L.T("daily.stat.ap"));

            // 格子 2：工资（信用点）
            DrawStatCell(2, ed.Wage.ToString(), Ink, Domain.L.T("daily.stat.wage"));

            // 格子 3：问题（项；0 时用好的颜色，>0 用坏的颜色）
            int problemDisplay = ed.ProblemCount;
            string probColor = problemDisplay == 0 ? Ok : Bad;
            DrawStatCell(3, problemDisplay.ToString(), probColor, Domain.L.T("daily.stat.problems"));

            void DrawStatCell(int idx, string numText, string numColor, string labelText)
            {
                float cx = Left + cellW * idx;
                float ns = 30;
                while (ns > 16 && EstWidth(numText, ns) > cellW - 8) ns -= 2;
                b.AddText(cx, statY + 8, cellW, 34, $"<b>{numText}</b>", PaperTextStyle.HEADER, align: "center", fontSize: ns);
                b.Last.color = numColor;

                float ls = 13;
                while (ls > 11 && EstWidth(labelText, ls) > cellW - 6) ls -= 1;
                b.AddText(cx, statY + 46, cellW, 20, labelText, PaperTextStyle.BODY, align: "center", fontSize: ls);
                b.Last.color = Dim;
            }

            b.AddLine(Left, statY + statH + 4, Left + CW, statY + statH + 4, 1f, "#D5CFC9");
            y = statY + statH + 18;
        }

        // 3. “流程一览”横幅（绿色 HandbookPages.Green，高度 28，前后空白 16-18）
        {
            if (y + 80 > Bottom) Page();

            const float bh = 28;
            b.AddLine(Left, y + bh / 2, Left + CW, y + bh / 2, bh, greenHex);
            b.AddText(Left, y + 4, CW, bh - 4, $"<b>{Domain.L.T("daily.section.flows")}</b>", PaperTextStyle.LABEL, align: "center", fontSize: 16);
            b.Last.color = "#FFFFFF";
            y += bh + 16;

            float nameW = 268f, colW1 = 70f, colW2 = 80f, colW3 = 90f;
            float x0 = Left, x1 = Left + nameW, x2 = x1 + colW1, x3 = x2 + colW2;

            b.AddText(x1, y, colW1, 18, Domain.L.T("daily.col.night"), PaperTextStyle.BODY, align: "right", fontSize: 12);
            b.Last.color = Dim;
            b.AddText(x2, y, colW2, 18, Domain.L.T("daily.col.morning"), PaperTextStyle.BODY, align: "right", fontSize: 12);
            b.Last.color = Dim;
            b.AddText(x3, y, colW3, 18, Domain.L.T("daily.col.ap"), PaperTextStyle.BODY, align: "right", fontSize: 12);
            b.Last.color = Dim;
            y += 24;

            const float rowH = 26f;
            var flows = ed.Flows;
            int showFlowCount = Math.Min(8, flows.Count);
            for (int i = 0; i < showFlowCount; i++)
            {
                var f = flows[i];
                string nameStr = $"<b>{f.Name}</b>";
                if (f.Problems > 0)
                    nameStr += $"  <color={Bad}>{Domain.L.N("daily.flow_problems", f.Problems)}</color>";
                // 流程名太长折行时，行高跟着文字（不压住下一行或下面的横幅）
                float nameH = Math.Max(22, Handbook.TextHeight(nameStr, PaperTextStyle.BODY, 15, nameW));
                float thisRowH = Math.Max(rowH, nameH + 4);
                if (y + thisRowH > Bottom) Page();

                b.AddText(x0, y, nameW, nameH, nameStr, PaperTextStyle.BODY, align: "left", fontSize: 15);
                b.Last.color = Ink;

                string nStr = ed.Night != null ? f.NightDone.ToString() : "—";
                b.AddText(x1, y, colW1, 22, nStr, PaperTextStyle.BODY, align: "right", fontSize: 15);
                b.Last.color = f.NightDone > 0 ? Ink : Dim;

                b.AddText(x2, y, colW2, 22, f.MorningDone.ToString(), PaperTextStyle.BODY, align: "right", fontSize: 15);
                b.Last.color = f.MorningDone > 0 ? Ink : Dim;

                b.AddText(x3, y, colW3, 22, $"{f.ApUsed:0.#} {Domain.L.T("daily.col.ap")}", PaperTextStyle.BODY, align: "right", fontSize: 15);
                b.Last.color = Ink;

                y += thisRowH;
            }

            if (flows.Count > 8)
            {
                if (y + 20 > Bottom) Page();
                int rest = flows.Count - 8;
                b.AddText(x0, y, CW, 20, Domain.L.T("daily.flows_more", rest), PaperTextStyle.BODY, align: "left", fontSize: 12);
                b.Last.color = Dim;
                y += 22;
            }
        }

        // 4. “问题”横幅（棕色 HandbookPages.Brown，高度 28，前后空白 16-18）
        {
            y += 18;
            if (y + 80 > Bottom) Page();

            const float bh = 28;
            b.AddLine(Left, y + bh / 2, Left + CW, y + bh / 2, bh, brownHex);
            b.AddText(Left, y + 4, CW, bh - 4, $"<b>{Domain.L.T("daily.section.problems")}</b>", PaperTextStyle.LABEL, align: "center", fontSize: 16);
            b.Last.color = "#FFFFFF";
            y += bh + 16;

            var probs = ed.Problems;
            if (probs.Count == 0)
            {
                b.AddText(Left + 4, y, CW - 8, 22, $"✓ {Domain.L.T("daily.all_fine")}", PaperTextStyle.BODY, align: "left", fontSize: 14);
                b.Last.color = Ok;
                y += 26;
            }
            else
            {
                int showProbCount = Math.Min(6, probs.Count);
                for (int i = 0; i < showProbCount; i++)
                {
                    var p = probs[i];
                    string countSuffix = p.Count > 1 ? $" ×{p.Count}" : "";
                    string content = !string.IsNullOrEmpty(p.Flow) && !string.IsNullOrEmpty(p.Rule)
                        ? $"{p.Flow} · {p.Rule}：{p.Reason}{countSuffix}"
                        : (!string.IsNullOrEmpty(p.Rule) ? $"{p.Rule}：{p.Reason}{countSuffix}" : $"{p.Reason}{countSuffix}");
                    string itemText = $"•<indent=16px>{content}</indent>";

                    float ph = Handbook.TextHeight(itemText, PaperTextStyle.BODY, 14, CW - 8);
                    if (y + ph > Bottom) Page();
                    b.AddText(Left + 4, y, CW - 8, ph, itemText, PaperTextStyle.BODY, align: "left", fontSize: 14);
                    b.Last.color = Bad;
                    y += ph + 8;
                }

                if (probs.Count > 6)
                {
                    int rest = probs.Count - 6;
                    string moreText = Domain.L.T("daily.problems_more", rest);
                    float mh = Handbook.TextHeight(moreText, PaperTextStyle.BODY, 12, CW - 8);
                    if (y + mh > Bottom) Page();
                    b.AddText(Left + 4, y, CW - 8, mh, moreText, PaperTextStyle.BODY, align: "left", fontSize: 12);
                    b.Last.color = Dim;
                    y += mh + 8;
                }
            }

            if (ed.Deferred > 0)
            {
                string defContent = Domain.L.N("daily.deferred", ed.Deferred);
                string defText = $"…<indent=16px>{defContent}</indent>";
                float dh = Handbook.TextHeight(defText, PaperTextStyle.BODY, 14, CW - 8);
                if (y + dh > Bottom) Page();
                b.AddText(Left + 4, y, CW - 8, dh, defText, PaperTextStyle.BODY, align: "left", fontSize: 14);
                b.Last.color = Warn;
                y += dh + 8;
            }
        }

        // 5. “学徒”条（底色 #C9C5A8，左侧 5 宽 #2F6B3A 竖条，文字墨色，前面加淡色小标题“学徒”）
        {
            var quoteLines = new List<string>();
            if (ed.Strikers > 0)
                quoteLines.Add($"<color={Bad}>⚠</color> {Domain.L.N("daily.strike", ed.Strikers)}");
            foreach (var g in ed.Growth.Take(3))
                quoteLines.Add($"<color={Ok}>★</color> {g}");

            if (quoteLines.Count > 0)
            {
                y += 16;
                float quoteBar = 5f, padX = 14f, padY = 8f;
                float tw = CW - quoteBar - padX * 2f;
                string quoteContent = string.Join("\n", quoteLines);
                float qth = Handbook.TextHeight(quoteContent, PaperTextStyle.BODY, 14, tw);
                float qh = qth + padY * 2f;
                if (y + qh + 24 > Bottom) Page();

                b.AddText(Left + 4, y, CW - 8, 18, $"<b>{Domain.L.T("daily.section.apprentices")}</b>", PaperTextStyle.BODY, align: "left", fontSize: 13);
                b.Last.color = Dim;
                y += 22;

                b.AddLine(Left, y + qh / 2f, Left + CW, y + qh / 2f, qh, QuoteBg);
                b.AddLine(Left + quoteBar / 2f, y, Left + quoteBar / 2f, y + qh, quoteBar, Ok);
                b.AddText(Left + quoteBar + padX, y + padY, tw, qth, quoteContent, PaperTextStyle.BODY, fontSize: 14);
                b.Last.color = Ink;
                y += qh + 8;
            }
        }

        // 6. 页脚：固定在内容区底部（y≈Bottom−20）
        {
            float footerY = Bottom - 20;
            if (y > footerY) { Page(); footerY = Bottom - 20; }
            b.AddText(Left, footerY, CW, 18, Domain.L.T("daily.footer"), PaperTextStyle.CAPTION, align: "center", fontSize: 11);
            b.Last.color = Dim;
        }

        ModHelper.RegisterPaperBook(ModInfo.Id, b.Data);
        Log.Info($"[Daily] 第 {ed.Day} 天的工作日报排版完成：{pageNo} 页");
    }
}
