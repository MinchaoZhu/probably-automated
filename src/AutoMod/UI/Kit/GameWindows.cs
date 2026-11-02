using System;
using System.Collections.Generic;
using AutoMod.Adapter;

namespace AutoMod.UI.Kit;

/// <summary>
/// 游戏窗口（容器、机器的 PixelWindow）的打开 / 关闭。游戏的窗口管理器 WindowsHandler 公开了当前显示的窗口列表，
/// 每帧和上一帧比较，得到刚打开和刚关闭的窗口（不打补丁）。点选模式订阅它。
/// </summary>
public static class GameWindows
{
    /// <summary>容器 / 机器的窗口刚打开：所属物品、窗口。处理函数返回 true = 这次打开已被消费，后面的不再处理。</summary>
    public static readonly List<Func<GameItem, PixelWindow, bool>> Opened = new();

    public static event Action<PixelWindow> Closed;

    private static readonly HashSet<PixelWindow> Seen = new();
    private static readonly List<PixelWindow> Diff = new();

    /// <summary>窗口所属的物品：窗口是物品图节点的子节点。</summary>
    public static GameItem OwnerOf(PixelWindow w)
    {
        try
        {
            foreach (var p in w.parents)
                if (p is GameItem item) return item;
        }
        catch { }
        return null;
    }

    /// <summary>每帧调用（界面根节点）。</summary>
    public static void Tick()
    {
        var visible = WindowsHandler.current?.visibleWindows;
        Diff.Clear();
        foreach (var w in Seen)
            if (visible == null || !visible.Contains(w)) Diff.Add(w);
        foreach (var w in Diff)
        {
            Seen.Remove(w);
            Ui.Guard(() => Closed?.Invoke(w));
        }
        if (visible == null) return;
        Diff.Clear();
        foreach (var w in visible)
            if (w != null && Seen.Add(w)) Diff.Add(w);
        foreach (var w in Diff)
        {
            var owner = GameApi.InStore ? OwnerOf(w) : null;
            if (owner == null) continue;
            foreach (var handler in Opened)
            {
                bool consumed = false;
                Ui.Guard(() => consumed = handler(owner, w));
                if (consumed) break;
            }
        }
    }
}
