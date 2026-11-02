using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.App;
using AutoMod.Domain;
using AutoMod.UI.Kit;
using UnityEngine;

namespace AutoMod.UI.Guide;

/// <summary>
/// 学徒手册：游戏的 mod 纸质手册（PaperUI），用代码按 HandbookPages 的内容生成。
/// 第一次打开时才生成并登记（那时游戏的物品贴图已经载入）。翻页、页码、关闭、放大由游戏处理。
/// 店里的“学徒手册”物品（App.ModItems）和对讲机里的按钮都调用 Open。
/// </summary>
public static class Handbook
{
    public const string BookId = "handbook";

    // 版面（notebook_manual 背景，设计尺寸取显示尺寸 628×804，1 单位 = 1 像素）
    internal const float W = 628, H = 804, Left = 72, Top = 74, ContentW = 508, ContentBottom = 730;
    internal static float Gap => Domain.L.Float("layout.handbook.gap", 8f);
    internal static float BodySize => Domain.L.Float("layout.handbook.body_size", 15f);
    internal const string Ink = "#3A3836";

    private static bool _registered;
    private static LoadedMod _mod;

    public static void ResetRegistration() => _registered = false;

    /// <summary>游戏载入完 mod 的贴图后调用（ModHook.OnModAssetsLoaded）：记下本 mod，补纸张贴图要用。</summary>
    public static void OnAssetsLoaded(LoadedMod mod)
    {
        if (mod?.Manifest?.ID == ModInfo.Id) _mod = mod;
    }

    /// <summary>
    /// 游戏 0.46D 没有打包纸张素材目录（Resources/PaperUI/PaperAssetCatalog），原版背景和翻页 / 关闭按钮按名字找不到。
    /// 纸张按名字找图时最后会查本 mod 的纸张贴图，所以把内存里同名的原版精灵登记进去（游戏能找到的就不动）。
    /// </summary>
    internal static void ProvideMissingPaperSprites()
    {
        if (_mod == null) return;
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            string n = sprite.name;
            if (n != "notebook_manual" && !n.StartsWith("paper_buttons")) continue;
            if (_mod.PaperSprites.ContainsKey(n) || PaperAssets.FindInCatalog(n) != null) continue;
            _mod.PaperSprites[n] = sprite;
        }
    }

    /// <summary>打开手册，翻到第 page 页（从 0 开始）。</summary>
    public static void Open(int page = 0)
    {
        if (!_registered && !Register()) { Toast.Show(Domain.L.T("handbook.cannot_open")); return; }
        var ui = ModHelper.OpenPaper(ModInfo.Id, BookId);
        if (ui == null) { Toast.Show(Domain.L.T("handbook.cannot_open")); return; }
        ui.PageShown -= Relayout;
        ui.PageShown += Relayout;
        ui.FlipTo(page);
        Relayout(ui, page);
        RaiseAboveModWindows(ui);
    }

    /// <summary>页面建好时是隐藏的，显示后流式布局不会自己重算：打开和翻页时强制重排一次。</summary>
    internal static void Relayout(PaperUI ui, int page)
    {
        if (ui.Root == null) return;
        Canvas.ForceUpdateCanvases();
        foreach (var g in ui.Root.GetComponentsInChildren<UnityEngine.UI.VerticalLayoutGroup>())
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)g.transform);
    }

    /// <summary>
    /// 纸质画布在 mod 窗口所在的 meta 层下面：从对讲机、编辑器里打开时会被盖住。打开期间把纸质画布调到上面，关上时恢复。
    /// </summary>
    internal static void RaiseAboveModWindows(PaperUI ui)
    {
        var canvas = ui.Root != null ? ui.Root.GetComponentInParent<Canvas>()?.rootCanvas : null;
        if (canvas == null || canvas.sortingOrder >= 950) return;
        int old = canvas.sortingOrder;
        canvas.sortingOrder = 950;
        void Restore(PaperUI u)
        {
            u.Closed -= Restore;
            if (canvas != null) canvas.sortingOrder = old;
        }
        ui.Closed += Restore;
    }

    // ================= 生成 =================

    private static bool Register()
    {
        try
        {
            ProvideMissingPaperSprites();
            var b = ModHelper.CreatePaper(ModInfo.Id, BookId, "notebook_manual");
            b.Data.title = HandbookPages.Title;
            b.Data.designWidth = W;
            b.Data.designHeight = H;
            b.SetSize(W, H);
            b.SetChrome(); // 设计尺寸改了，翻页、关闭按钮按新尺寸重新摆
            var pages = HandbookPages.All;
            for (int n = 0; n < pages.Count; n++) BuildPage(b, n, pages[n]);
            ModHelper.RegisterPaperBook(ModInfo.Id, b.Data);
            _registered = true;
            Log.Info($"[Handbook] 已登记学徒手册（{pages.Count} 页）");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[Handbook] 生成手册失败: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 一页：页眉“学徒手册”，然后各块自上而下按量好的高度摆（纸张的元素都是绝对坐标）：
    /// 横幅是一条粗色线加白字；条目左边是物品图标，右边是下划线小标题和正文；提示是引用条（底色 + 本节颜色的竖条 + 深色正文）。
    /// </summary>
    private static void BuildPage(PaperUI b, int n, List<HandbookBlock> blocks)
    {
        b.AddPage($"p{n + 1}");
        b.AddText(Left, 34, ContentW, 30, HandbookPages.Title, PaperTextStyle.HEADER, align: "center", fontSize: 22);
        float y = Top;
        _accent = HandbookPages.Green;
        foreach (var block in blocks)
        {
            if (block.Kind == HandbookBlock.Kinds.Banner) _accent = block.Color; // 提示框的竖条跟本节横幅同色
            y += Place(b, block, y) + Gap * 1.5f;
        }
        if (y > ContentBottom) Log.Warn($"[Handbook] 第 {n + 1} 页内容超出纸面 overflow（{y:0} > {ContentBottom}）");
        else Log.Info($"[Handbook] 第 {n + 1} 页排版完成: y={y:0}/{ContentBottom}");
    }

    private const float IconSize = 52, TextLeft = IconSize + 16;

    /// <summary>当前小节横幅的颜色（提示框的竖条用）。</summary>
    private static Color _accent;

    /// <summary>提示框（引用条）：比纸面稍深的底色、左边一条本节颜色的竖条、深色正文。</summary>
    private const string QuoteBg = "#C9C5A8";
    private const float QuoteBar = 5, QuotePadX = 14, QuotePadY = 7;

    /// <summary>摆一块，返回它的高度。</summary>
    private static float Place(PaperUI b, HandbookBlock block, float y)
    {
        switch (block.Kind)
        {
            case HandbookBlock.Kinds.Banner:
            {
                const float h = 26;
                b.AddLine(Left, y + h / 2, Left + ContentW, y + h / 2, h, Hex(block.Color));
                b.AddText(Left, y + 3, ContentW, h - 4, $"<b>{block.Text}</b>", PaperTextStyle.LABEL, align: "center", fontSize: 16);
                b.Last.color = "#FFFFFF";
                return h;
            }
            case HandbookBlock.Kinds.Entry:
            {
                float w = ContentW - TextLeft;
                string title = $"<b><u>{block.Title}</u></b>";
                float th = TextHeight(title, PaperTextStyle.BODY, BodySize, w);
                float bh = TextHeight(block.Text, PaperTextStyle.BODY, BodySize - 1, w);
                if (ItemSprite(block.Icon) is { } icon) b.AddImage(Left, y + 2, IconSize, IconSize, icon, "fit");
                b.AddText(Left + TextLeft, y, w, th, title, PaperTextStyle.BODY, fontSize: BodySize);
                b.Last.color = Ink;
                b.AddText(Left + TextLeft, y + th + 2, w, bh, block.Text, PaperTextStyle.BODY, fontSize: BodySize - 1);
                b.Last.color = Ink;
                return Mathf.Max(IconSize + 4, th + 2 + bh);
            }
            case HandbookBlock.Kinds.Steps:
            {
                float h = TextHeight(block.Text, PaperTextStyle.BODY, BodySize, ContentW);
                b.AddText(Left, y, ContentW, h, block.Text, PaperTextStyle.BODY, fontSize: BodySize);
                b.Last.color = Ink;
                return h;
            }
            case HandbookBlock.Kinds.Tip:
            {
                float tw = ContentW - QuoteBar - QuotePadX * 2;
                float th = TextHeight(block.Text, PaperTextStyle.BODY, BodySize - 1, tw);
                float h = th + QuotePadY * 2;
                b.AddLine(Left, y + h / 2, Left + ContentW, y + h / 2, h, QuoteBg);
                b.AddLine(Left + QuoteBar / 2, y, Left + QuoteBar / 2, y + h, QuoteBar, Hex(_accent));
                b.AddText(Left + QuoteBar + QuotePadX, y + QuotePadY, tw, th, block.Text, PaperTextStyle.BODY, fontSize: BodySize - 1);
                b.Last.color = Ink;
                return h;
            }
            case HandbookBlock.Kinds.Diagram:
                DrawDiagram(b, block.Diagram, y);
                return block.Diagram.Height;
        }
        return 0;
    }

    private static TMPro.TextMeshProUGUI _measurer;

    internal static float TextHeight(string text, string style, float size, float width)
    {
        if (_measurer == null)
        {
            // 要激活才量得准；它不在任何画布下，不会显示
            var go = new GameObject("probably_automated_handbook_measure", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            _measurer = go.AddComponent<TMPro.TextMeshProUGUI>();
            _measurer.enableWordWrapping = true;
            _measurer.richText = true;
        }
        var font = PaperTextStyle.Get(style).Font();
        if (font != null) _measurer.font = font;
        _measurer.fontSize = size;
        float h = _measurer.GetPreferredValues(text, width, 0).y;
        return h > 1 ? h + 2 : size * 1.5f * (1 + text.Split('\n').Length); // 量不出来时按行数估算
    }

    // ================= 示意图 =================

    /// <summary>
    /// 示意图：方框（上半是物品图，下半是带底色的名称和说明）和带标签的箭头。坐标以示意图左上角为原点，宽 470，居中放在内容区。
    /// 底色块是加粗的横线（纸张没有矩形元素）。
    /// </summary>
    private static void DrawDiagram(PaperUI b, HandbookDiagram dg, float top)
    {
        float left = Left + (ContentW - 470) / 2;
        const float labelH = 44;
        foreach (var node in dg.Nodes)
        {
            float x = left + node.X, y = top + node.Y;
            float artH = node.Arts == null ? 0 : node.H - labelH;
            if (node.Arts != null)
                foreach (var art in node.Arts)
                {
                    // 物品图放大 2 倍（像素风），中心在图区中心加偏移
                    float cx = x + node.W / 2 + art.Dx, cy = y + artH / 2 + art.Dy;
                    b.AddImage(cx - 60, cy - 60, 120, 120, VanillaSprite(art.Sprite), "native");
                    b.Last.scale = 2f;
                }
            float ly = y + artH;
            b.AddLine(x, ly + labelH / 2, x + node.W, ly + labelH / 2, labelH, Hex(node.Color));
            b.AddText(x, ly + 4, node.W, 20, $"<b>{node.Title}</b>", PaperTextStyle.LABEL, align: "center", fontSize: 15);
            b.Last.color = "#FFFFFF";
            b.AddText(x, ly + 24, node.W, 16, node.Note, PaperTextStyle.CAPTION, align: "center", fontSize: 11);
            b.Last.color = "#FFFFFF";
        }
        foreach (var a in dg.Arrows)
        {
            b.AddLine(left + a.X1, top + a.Y1, left + a.X2, top + a.Y2, 3f, Ink, PaperLineArrow.END);
            b.Last.arrowSize = 9f;
            float labelW = a.W > 0 ? a.W : 150f;
            float lx = left + a.LX - (a.Right ? labelW : 0);
            b.AddText(lx, top + a.LY - 10, labelW, 20, a.Label, PaperTextStyle.CAPTION, align: a.Right ? "right" : "left", fontSize: 13);
            b.Last.color = Ink;
        }
    }

    private static readonly Dictionary<string, string> SpriteKeys = new();

    /// <summary>物品图标：按原型 id 取游戏物品的贴图；不是物品 id 就当精灵名找。</summary>
    private static string ItemSprite(string kind)
    {
        if (string.IsNullOrEmpty(kind)) return null;
        string name = kind;
        try { if (DirectoryMaster.Item(kind)?.spritePath is { Length: > 0 } p) name = p; }
        catch { }
        return VanillaSprite(name);
    }

    /// <summary>
    /// 游戏自己的物品图：纸张用“图集/精灵名”引用（例如 items_tool/battery100）。按名字找到已载入的精灵，取它的图集名。
    /// </summary>
    internal static string VanillaSprite(string name)
    {
        if (SpriteKeys.TryGetValue(name, out var key)) return key;
        var sprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == name);
        key = sprite?.texture != null ? $"{sprite.texture.name}/{name}" : name;
        if (PaperAssets.Resolve(key, null, true) == null)
        {
            Log.Warn($"[Handbook] 找不到游戏图 {name}（{key}）");
            key = name;
        }
        return SpriteKeys[name] = key;
    }

    internal static string Hex(Color c) => PaperTextStyle.ToHex(c);
}
