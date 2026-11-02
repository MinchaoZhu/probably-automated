using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using AutoMod.Domain;

namespace AutoMod.UI.Kit;

/// <summary>mod 界面的修正：中文字体后备（游戏语言不是中文时）、滚轮速度、按配置缩放、重建后恢复滚动位置。</summary>
public static class UiFixes
{
    /// <summary>每个窗口已经修正过的根节点（窗口重建后根节点会换）。</summary>
    private static readonly Dictionary<string, GameObject> Fixed = new();

    /// <summary>开发用：关掉滚轮修正，对比游戏原样的效果。</summary>
    public static bool Disabled;

    /// <summary>每帧调用：窗口（重）建后的第一帧修正一次。</summary>
    public static void Tick()
    {
        foreach (var (id, win) in Ui.Windows().ToList())
        {
            var root = win.root;
            if (Fixed.TryGetValue(id, out var done) && ReferenceEquals(done, root)) continue;
            Fixed[id] = root;
            FixWindow(win);
        }
    }

    private static bool _fallbackDone;

    /// <summary>
    /// 游戏语言不是中文时，文字组件用的字体没有中文字形，mod 的中文全部显示成空白。
    /// 把中文字体加进 TextMeshPro 的全局后备字体：缺字时自动改用它，mod 窗口一次全部覆盖。
    /// 每帧调用，找到字体后只做一次。
    /// </summary>
    public static void EnsureCjkFallback()
    {
        if (_fallbackDone) return;
        try
        {
            // 优先用游戏中文界面的字体 NotoSansSC（动态字体，按字号生成字形，清晰）；
            // WDXLLubrifontSC 是固定图集的 SDF 字体，小字号发虚，只作备选
            TMPro.TMP_FontAsset found = null;
            int rank = 0;
            foreach (var o in Resources.FindObjectsOfTypeAll(typeof(TMPro.TMP_FontAsset)))
            {
                var f = (o as TMPro.TMP_FontAsset);
                if (f == null) continue;
                // 动态字体要试着生成字形才知道能不能用（英文模式下源字体可能没载入）
                int r = f.name.StartsWith("NotoSansSC") && f.HasCharacters("学徒流程", out _, false, true) ? 3
                    : f.name.Contains("LubrifontSC") ? 2 : f.HasCharacters("学徒", out _, false, false) ? 1 : 0;
                if (r > rank) { rank = r; found = f; }
            }
            if (found == null) return;
            var list = TMPro.TMP_Settings.fallbackFontAssets;
            if (list != null && !list.Contains(found)) list.Add(found);
            _fallbackDone = true;
            Log.Info($"[UI] 中文后备字体：{found.name}");
        }
        catch (Exception ex)
        {
            _fallbackDone = true;
            Log.Warn($"[UI] 设置中文后备字体失败: {ex.Message}");
        }
    }

    /// <summary>每个 mod 窗口（重）建后：按配置缩放、恢复滚动位置、调快滚轮。</summary>
    private static void FixWindow(CustomUIWindow win)
    {
        try
        {
            var root = win?.Rect;
            if (root == null) return;
            if (Mathf.Abs(Ui.UserScale - 1f) > 0.01f) root.localScale = Vector3.one * Ui.UserScale;
            Ui.RestoreScroll(win);
            if (Disabled) return;
            // 构建器的滚动区默认一格滚轮只动 1 像素，长的属性栏要滚几百下
            foreach (var sr in root.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true))
                if (sr.scrollSensitivity < 30) sr.scrollSensitivity = 30;
        }
        catch (Exception ex) { Log.Warn($"[UI] 修正窗口失败: {ex.Message}"); }
    }
}
