using System.Collections.Generic;
using UnityEngine;

namespace AutoMod.UI.Kit;

/// <summary>
/// 按 Esc 时游戏的 CustomUIManager 关掉最上层可按 Esc 关闭的 mod 窗口，但游戏自己的 Esc 处理
/// （StoreUIManager.ResolveEscape）不认识 mod 窗口，没有别的可关就打开暂停菜单，两边在同一帧动作。不打补丁，每帧检查：
/// - 暂停菜单开着时，mod 窗口临时不响应 Esc（这次 Esc 是关菜单的）；
/// - 这一帧 Esc 关掉了 mod 窗口、暂停菜单又在这一帧打开了，就把菜单关回去。
/// </summary>
public static class EscapeGuard
{
    private static EscapeUIManager _menu;
    private static bool _menuWasShown;
    private static int _escapableBefore;
    private static readonly List<CustomUIWindow> Suspended = new();

    /// <summary>每帧 LateUpdate 调用（游戏和 CustomUIManager 都处理完这一帧的 Esc 之后）。</summary>
    public static void LateTick()
    {
        if (_menu == null) _menu = Object.FindObjectOfType<EscapeUIManager>();
        bool shown = _menu != null && _menu.menuPanel != null && _menu.menuPanel.activeInHierarchy;
        int escapable = CountEscapable();

        if (Input.GetKeyDown(KeyCode.Escape) && shown && !_menuWasShown && escapable < _escapableBefore)
        {
            _menu.CloseEscapeMenu();
            shown = false;
        }

        if (shown) Suspend();
        else Resume();

        _menuWasShown = shown;
        _escapableBefore = CountEscapable();
    }

    private static int CountEscapable()
    {
        int n = 0;
        foreach (var (_, w) in Ui.Windows())
            if (w.IsAlive && w.IsVisible && w.closeOnEscape) n++;
        return n;
    }

    private static void Suspend()
    {
        foreach (var (_, w) in Ui.Windows())
            if (w.IsAlive && w.closeOnEscape) { w.closeOnEscape = false; Suspended.Add(w); }
    }

    private static void Resume()
    {
        if (Suspended.Count == 0) return;
        foreach (var w in Suspended) if (w.IsAlive) w.closeOnEscape = true;
        Suspended.Clear();
    }
}
