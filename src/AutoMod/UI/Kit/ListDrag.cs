using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AutoMod.UI.Kit;

/// <summary>
/// 列表拖动排序（规则卡片、挑选器）：每帧轮询鼠标（Wine 下事件不可靠，和画布一样用轮询）。
/// 按住一行拖动超过阈值后显示插入线，松开时回调 (from, to)。
/// </summary>
public static class ListDrag
{
    private class Group
    {
        public List<RectTransform> Rows = new();
        public Action<int, int> OnMove;
    }

    private static readonly Dictionary<string, Group> Groups = new();
    private static string _group;
    private static int _from = -1;
    private static Vector2 _down;
    private static bool _dragging;
    private static RectTransform _marker;
    private static ScrollRect _scroll;

    /// <summary>窗口重建后重新登记一组行（按显示顺序）。</summary>
    public static void Register(string group, List<RectTransform> rows, Action<int, int> onMove)
    {
        Groups[group] = new Group { Rows = rows, OnMove = onMove };
        if (_group == group) Cancel();
    }

    public static void Clear(string group)
    {
        Groups.Remove(group);
        if (_group == group) Cancel();
    }

    private static bool Alive(RectTransform rt) => rt != null && rt.gameObject.activeInHierarchy;

    /// <summary>行的屏幕矩形（覆盖层画布，世界坐标 = 屏幕坐标）。
    /// </summary>
    private static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static int RowAt(Group g, Vector2 screen)
    {
        for (int i = 0; i < g.Rows.Count; i++)
            if (Alive(g.Rows[i]) && ScreenRect(g.Rows[i]).Contains(screen)) return i;
        return -1;
    }

    /// <summary>松开位置对应的最终序号：除被拖行外、中线在鼠标上方的行数（屏幕坐标 y 向上）。</summary>
    private static int TargetAt(Group g, Vector2 screen)
    {
        int n = 0;
        for (int i = 0; i < g.Rows.Count; i++)
            if (i != _from && Alive(g.Rows[i]) && ScreenRect(g.Rows[i]).center.y > screen.y) n++;
        return n;
    }

    public static void Tick()
    {
        Vector2 mouse = Input.mousePosition;
        if (Input.GetMouseButtonDown(0))
        {
            foreach (var (name, g) in Groups)
            {
                int i = RowAt(g, mouse);
                if (i < 0) continue;
                _group = name; _from = i; _down = mouse; _dragging = false;
                // 行在滚动区里：滚动区会跟着拖动移动内容，松开时行位置就不对了，拖动期间先停用
                _scroll = g.Rows[i].GetComponentInParent<ScrollRect>();
                if (_scroll != null) _scroll.enabled = false;
                return;
            }
            return;
        }
        if (_group == null || !Groups.TryGetValue(_group, out var grp)) return;
        if (Input.GetMouseButton(0))
        {
            if (!_dragging && (mouse - _down).magnitude > 10) _dragging = true;
            if (_dragging) ShowMarker(grp, mouse);
            return;
        }
        // 松开
        int from = _from;
        bool dragging = _dragging;
        int to = TargetAt(grp, mouse);
        Cancel();
        if (!dragging) return;
        if (to != from) Ui.Guard(() => grp.OnMove(from, to));
    }

    private static void ShowMarker(Group g, Vector2 mouse)
    {
        int to = TargetAt(g, mouse);
        if (to >= g.Rows.Count || !Alive(g.Rows[to])) return;
        if (_marker == null)
        {
            var root = Ui.LayerRoot;
            if (root == null) return;
            _marker = new GameObject("am_dragmarker").AddComponent<RectTransform>();
            _marker.SetParent(root, false);
            var img = _marker.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 0.85f, 0.3f);
            img.raycastTarget = false;
        }
        var r = ScreenRect(g.Rows[to]);
        bool below = to > _from;
        _marker.gameObject.SetActive(true);
        _marker.SetAsLastSibling();
        _marker.pivot = new Vector2(0, 0.5f);
        _marker.position = new Vector3(r.xMin, below ? r.yMin : r.yMax, 0);
        var scale = _marker.lossyScale.x <= 0 ? 1 : _marker.lossyScale.x;
        _marker.sizeDelta = new Vector2(r.width / scale, 3);
    }

    private static void Cancel()
    {
        if (_scroll != null) _scroll.enabled = true;
        _scroll = null;
        _group = null;
        _from = -1;
        _dragging = false;
        if (_marker != null) _marker.gameObject.SetActive(false);
    }
}
