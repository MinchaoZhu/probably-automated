using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AutoMod;
using AutoMod.Domain;

namespace AutoMod.UI.Kit;

/// <summary>游戏自带 mod 窗口构建器（CustomUIManager）的薄封装。</summary>
public static class Ui
{
    private static CustomUIManager _mgr;

    public static CustomUIManager Mgr
    {
        get
        {
            if (_mgr == null) _mgr = CustomUIManager.Instance;
            return _mgr;
        }
    }

    // ---------- 委托转换 ----------

    // 界面回调统一包一层 Guard（异常保护）

    public static Action A(Action a) => () => Guard(a);

    public static Action<int> AI(Action<int> a) => i => Guard(() => a(i));

    public static Action<bool> AB(Action<bool> a) => b => Guard(() => a(b));

    public static Action<string> AS(Action<string> a) => s => Guard(() => a(s));

    /// <summary>界面回调里的异常不能抛回游戏，否则会打断游戏的界面逻辑。</summary>
    public static void Guard(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"[UI] {ex}"); }
    }

    public static IList<string> L(IEnumerable<string> items) => items.ToList();

    /// <summary>
    /// 店里的 mod 窗口放在游戏的 meta 层（CanvasScreenLayer2）：这一层的窗口带游戏的 StoreUI 标记，
    /// 游戏判断鼠标在店铺界面上时会停用物品的鼠标处理，在窗口上悬停、双击不会落到下面的物品上。
    /// 不在店里（主菜单）时用 overlay 层。
    /// </summary>
    public const string StoreLayer = "meta";

    public static RectTransform LayerRoot =>
        Mgr == null ? null : Mgr.GetLayerRoot(AutoMod.Adapter.GameApi.InStore ? StoreLayer : "overlay");

    /// <summary>界面大小倍数（配置项，界面根节点每帧同步）：窗口建好后按它缩放；分辨率的缩放由游戏的画布负责。</summary>
    public static float UserScale = 1f;

    /// <summary>mod 窗口的游戏样式。</summary>
    public static CustomUIManager.Style Style = CustomUIManager.Style.Machinery;

    // ---------- 窗口 ----------

    /// <summary>新建或重建窗口（同 id 的窗口会被清空重建，位置保留）。</summary>
    /// <param name="draggable">大窗口（编辑器、模拟器）关掉拖动：标题栏太大，误拖会把窗口拖出屏幕。</param>
    /// <param name="escape">按 Esc 是否关掉。向导和点选提示要关掉这个：玩家在店里按 Esc 关容器窗口时，向导不能跟着没了。</param>
    /// <param name="keepScroll">重建同一个窗口时保留滚动位置（点了窗口靠下的按钮后不跳回顶部）。</param>
    public static CustomUIBuilder Window(string id, string title, float w, float h, bool draggable = true, bool escape = true, bool keepScroll = false)
    {
        Vector2? keep = null;
        var old = Get(id);
        if (old != null && old.IsAlive)
        {
            keep = old.Rect.anchoredPosition;
            if (keepScroll) RememberScroll(id, old);
        }
        Ids.Add(id);
        var b = (AutoMod.Adapter.GameApi.InStore
                ? Mgr.CreateWindow(id, title, StoreLayer, null, Style) // 机器窗口的样式（灰色面板）
                : CustomUIManager.Window(id, title))
            .SetSize(w, h).SetCloseOnEscape(escape).SetDraggable(draggable);
        if (!draggable) { b.Center(); return b; }
        if (keep.HasValue) b.SetPosition(keep.Value); else b.Center();
        return b;
    }

    /// <summary>重建前记下的各滚动区内容位置（窗口根节点名 → 位置），窗口显示后由 UiFixes 恢复。</summary>
    private static readonly Dictionary<string, List<Vector2>> PendingScroll = new();

    private static void RememberScroll(string id, CustomUIWindow old)
    {
        try
        {
            var list = new List<Vector2>();
            foreach (var sr in old.Rect.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true))
                list.Add(sr.content != null ? sr.content.anchoredPosition : Vector2.zero);
            PendingScroll[old.Rect.name] = list;
        }
        catch { }
    }

    /// <summary>窗口显示后恢复滚动位置（只用一次）。</summary>
    public static void RestoreScroll(CustomUIWindow win)
    {
        try
        {
            if (win?.Rect == null || !PendingScroll.Remove(win.Rect.name, out var list)) return;
            Canvas.ForceUpdateCanvases();
            var srs = win.Rect.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true);
            for (int i = 0; i < srs.Length && i < list.Count; i++)
                if (srs[i].content != null) srs[i].content.anchoredPosition = list[i];
        }
        catch { }
    }

    /// <summary>mod 打开过的窗口 id（Ui.Window 登记）：Esc 处理、窗口修正按它找 mod 自己的窗口。</summary>
    private static readonly HashSet<string> Ids = new();

    /// <summary>mod 打开过、现在还在的窗口。</summary>
    public static IEnumerable<(string id, CustomUIWindow win)> Windows()
    {
        foreach (var id in Ids)
        {
            var w = Get(id);
            if (w != null && w.IsAlive) yield return (id, w);
        }
    }

    public static CustomUIWindow Get(string id)
    {
        try { return Mgr?.GetWindow(id); } catch { return null; }
    }

    public static bool IsOpen(string id)
    {
        try { return Mgr != null && Mgr.IsOpen(id); } catch { return false; }
    }

    public static void Close(string id)
    {
        try { if (IsOpen(id)) Mgr.CloseWindow(id); } catch { }
    }

    public static void CloseAll()
    {
        try
        {
            if (Mgr != null)
                foreach (var (id, _) in Windows().ToList())
                    Close(id);
        }
        catch { }
    }

    /// <summary>
    /// 游戏窗口在屏幕上的范围（左、右、上，屏幕像素）。PixelWindow 自身的矩形接近 0 大小，
    /// 真正的外观在子元素上，所以取所有子元素的并集。
    /// </summary>
    public static bool ScreenBounds(PixelWindow target, out float left, out float right, out float top)
    {
        left = float.MaxValue; right = float.MinValue; top = float.MinValue;
        var srcCam = CanvasCamera(target.rectTransform.GetComponentInParent<Canvas>());
        var corners = new Vector3[4];
        foreach (var rt in target.rectTransform.GetComponentsInChildren<RectTransform>())
        {
            if (rt == null || rt.rect.width <= 1 || rt.rect.height <= 1) continue;
            rt.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(srcCam, corners[i]);
                left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x); top = Mathf.Max(top, p.y);
            }
        }
        return right > left;
    }

    private static Camera CanvasCamera(Canvas c)
    {
        if (c == null) return null;
        c = c.rootCanvas;
        return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
    }

    /// <summary>下拉框：选中值的序号（找不到时 0）。</summary>
    public static int IndexOf<T>(IList<T> list, Func<T, bool> pred)
    {
        for (int i = 0; i < list.Count; i++) if (pred(list[i])) return i;
        return 0;
    }
}
