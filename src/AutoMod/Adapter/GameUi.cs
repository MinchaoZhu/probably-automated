using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoMod.Adapter;

/// <summary>
/// 游戏界面有没有因为某个调用打开新的面板或窗口（学徒“使用”工具时，检视、改名、成分分析这类工具会打开界面让玩家看或填）。
/// 调用前拍快照，调用后比较：各根画布下刚激活的直接子物体、刚出现的游戏窗口（PixelWindow）。
/// </summary>
public sealed class GameUi
{
    private readonly HashSet<GameObject> _panels;
    private readonly HashSet<PixelWindow> _windows;

    private GameUi(HashSet<GameObject> panels, HashSet<PixelWindow> windows)
    {
        _panels = panels;
        _windows = windows;
    }

    public static GameUi Snapshot() => new(ActivePanels(), new HashSet<PixelWindow>(WindowsHandler.current?.visibleWindows ?? new List<PixelWindow>()));

    private static HashSet<GameObject> ActivePanels()
    {
        var set = new HashSet<GameObject>();
        foreach (var canvas in Object.FindObjectsOfType<Canvas>())
        {
            if (!canvas.isRootCanvas) continue;
            foreach (Transform child in canvas.transform)
                if (child.gameObject.activeSelf) set.Add(child.gameObject);
        }
        return set;
    }

    /// <summary>快照之后新打开的面板和窗口；close 时把它们关掉。返回打开了几个。</summary>
    public int Opened(bool close)
    {
        var panels = ActivePanels().Where(p => !_panels.Contains(p)).ToList();
        var windows = (WindowsHandler.current?.visibleWindows ?? new List<PixelWindow>()).Where(w => !_windows.Contains(w)).ToList();
        if (close)
        {
            foreach (var w in windows) WindowsHandler.CloseWindow(w);
            foreach (var p in panels) p.SetActive(false);
        }
        return panels.Count + windows.Count;
    }
}
