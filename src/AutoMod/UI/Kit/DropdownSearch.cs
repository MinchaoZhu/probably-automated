using System;
using System.Collections.Generic;
using System.Linq;
using AutoMod.Domain.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoMod.UI.Kit;

/// <summary>
/// 下拉列表顶部的搜索框和大类按钮，展开时滚动到当前项。
/// 下拉框每次展开都新建列表，所以挂在下拉框上，发现新列表时往里加。过滤只隐藏行并重新排列：
/// 下拉框按行的位置确定选中项，隐藏不改变位置，“编辑”列和悬停提示也不受影响。
/// </summary>
public class DropdownSearch : MonoBehaviour
{
    private const float MinWidth = 240f;
    private const float GroupedWidth = 500f;
    private const float InputHeight = 30f;
    private const float ChipHeight = 26f;
    private const float Gap = 4f;
    private const int ChipColumns = 6;
    private static readonly Color ActiveChip = new(0.94f, 0.75f, 0.38f);

    private class Row
    {
        public Toggle Toggle;
        public RectTransform Rect;
        public string Text;
        public string Group;
        public bool Pinned;
    }

    private TMP_Dropdown _dropdown;
    private Func<int, string> _groupOf;
    private HashSet<int> _pinned;

    private GameObject _list;
    private List<Row> _rows;
    private Row _current;
    private RectTransform _content;
    private ScrollRect _scroll;
    private float _rowHeight, _bottom, _padding;
    private TMP_InputField _input;
    private GameObject _noMatch;
    private string _group;
    private readonly Dictionary<string, (Button button, Graphic graphic, Color color)> _chips = new();
    private readonly List<CustomUIWindow> _held = new();

    /// <param name="groupOf">第几行属于哪个大类；null 的行只在“全部”下显示。不传时没有大类按钮。</param>
    /// <param name="pinned">搜索时固定显示的行。</param>
    public static void Attach(TMP_Dropdown dropdown, Func<int, string> groupOf, IEnumerable<int> pinned)
    {
        if (dropdown == null) return;
        var c = dropdown.gameObject.AddComponent<DropdownSearch>();
        c._dropdown = dropdown;
        c._groupOf = groupOf;
        c._pinned = new HashSet<int>(pinned ?? Enumerable.Empty<int>());
    }

    private void LateUpdate()
    {
        if (_dropdown == null || !_dropdown.IsExpanded)
        {
            if (_list != null) { _list = null; Release(); }
            return;
        }
        var list = _dropdown.transform.Find("Dropdown List")?.gameObject;
        if (list == null) return;
        if (list != _list)
        {
            _list = list;
            Ui.Guard(() => Build(list));
        }
        if (Input.GetKeyDown(KeyCode.Escape)) Ui.Guard(Dismiss);
    }

    private void OnDestroy() => Release();

    /// <summary>Esc 只收起列表：列表开着时窗口不响应 Esc，这次 Esc 打开的暂停菜单关回去。</summary>
    private void Dismiss()
    {
        _dropdown.Hide();
        EscapeGuard.CloseMenu();
    }

    private void Hold()
    {
        foreach (var (_, w) in Ui.Windows())
            if (w.closeOnEscape) { w.closeOnEscape = false; _held.Add(w); }
    }

    private void Release()
    {
        foreach (var w in _held) if (w.IsAlive) w.closeOnEscape = true;
        _held.Clear();
    }

    private void Build(GameObject list)
    {
        var toggles = list.GetComponentsInChildren<Toggle>(false);
        if (toggles.Length == 0) return;
        Hold();

        var rect = (RectTransform)list.transform;
        _scroll = list.GetComponentInChildren<ScrollRect>();
        _content = (RectTransform)toggles[0].transform.parent;
        var last = (RectTransform)toggles[toggles.Length - 1].transform;
        _rowHeight = last.rect.height;
        _bottom = last.anchoredPosition.y - _rowHeight * last.pivot.y;
        _padding = _content.sizeDelta.y - _rowHeight * toggles.Length;
        if (_scroll != null && _scroll.scrollSensitivity < 30) _scroll.scrollSensitivity = 30;

        var options = _dropdown.options;
        _rows = new List<Row>(toggles.Length);
        for (int i = 0; i < toggles.Length && i < options.Count; i++)
            _rows.Add(new Row
            {
                Toggle = toggles[i],
                Rect = (RectTransform)toggles[i].transform,
                Text = options[i].text,
                Group = _groupOf?.Invoke(i),
                Pinned = _pinned.Contains(i),
            });
        _current = _dropdown.value >= 0 && _dropdown.value < _rows.Count ? _rows[_dropdown.value] : null;
        if (_current != null) Highlight(_current);

        var groups = ItemGroups.Order.Where(g => _rows.Any(r => r.Group == g)).ToList();
        bool chips = groups.Count >= 2;
        int chipRows = chips ? (groups.Count + ChipColumns) / ChipColumns : 0; // 多一个“全部”
        float barHeight = InputHeight + Gap * 2 + chipRows * ChipHeight + (chipRows > 0 ? Gap : 0);

        Widen(rect, chips ? GroupedWidth : MinWidth);
        // 游戏的模板让滚动区每次布局都重设视口的矩形，搜索区让出的高度会被还原
        if (_scroll != null && _scroll.verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport)
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        Grow(rect, barHeight);

        var bar = new GameObject("am_search", typeof(RectTransform));
        var barRect = (RectTransform)bar.transform;
        barRect.SetParent(rect, false);
        barRect.anchorMin = new Vector2(0, 1);
        barRect.anchorMax = new Vector2(1, 1);
        barRect.pivot = new Vector2(0.5f, 1);
        barRect.offsetMin = new Vector2(Gap, -barHeight);
        barRect.offsetMax = new Vector2(-Gap, 0);

        var window = Ui.Windows().Select(x => x.win).FirstOrDefault(w => transform.IsChildOf(w.Rect));
        BuildInput(barRect, window);
        if (chips) BuildChips(barRect, window, groups);
        BuildNoMatch(rect, window, barHeight);

        Apply();
        _input.ActivateInputField();
    }

    /// <summary>当前项底色高亮、文字加粗。</summary>
    private static void Highlight(Row row)
    {
        var mark = new GameObject("am_current", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)mark.transform;
        rt.SetParent(row.Rect, false);
        rt.SetSiblingIndex(Mathf.Min(1, row.Rect.childCount - 1));
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var image = mark.GetComponent<Image>();
        image.color = new Color(0.94f, 0.75f, 0.38f, 0.25f);
        image.raycastTarget = false;
        var text = row.Rect.GetComponentInChildren<TMP_Text>();
        if (text != null) text.fontStyle |= FontStyles.Bold;
        // 游戏的选中标记是一个深色方块，在高亮底色上会露出来
        if (row.Toggle.graphic != null) row.Toggle.graphic.gameObject.SetActive(false);
    }

    /// <summary>加宽列表，超出屏幕时往左挪。</summary>
    private void Widen(RectTransform rect, float width)
    {
        float extra = width - rect.rect.width;
        if (extra <= 0) return;
        rect.offsetMax += new Vector2(extra, 0);
        float over = WorldBounds(rect).xMax - RootBounds().xMax;
        if (over > 0) rect.anchoredPosition -= new Vector2(over / rect.lossyScale.x, 0);
    }

    /// <summary>加高列表放下搜索区，滚动区和滚动条让出顶部；放不下时不超出屏幕。</summary>
    private void Grow(RectTransform rect, float height)
    {
        foreach (RectTransform child in rect)
            child.offsetMax -= new Vector2(0, height);
        rect.sizeDelta += new Vector2(0, height);
        var list = WorldBounds(rect);
        var root = RootBounds();
        float over = Mathf.Max(root.yMin - list.yMin, list.yMax - root.yMax);
        if (over > 0) rect.sizeDelta -= new Vector2(0, Mathf.Min(over / rect.lossyScale.y, height));
    }

    private static Rect WorldBounds(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private Rect RootBounds() => WorldBounds((RectTransform)_dropdown.GetComponentInParent<Canvas>().rootCanvas.transform);

    private void BuildInput(RectTransform bar, CustomUIWindow window)
    {
        var go = Ui.Mgr.SpawnElement(CustomUIManager.ElementKind.Input, bar, window, "am_search_input");
        var element = CustomUIElement.Bind(go, null);
        _input = element.input;
        if (_input.placeholder is TMP_Text placeholder) placeholder.text = Domain.L.T("list.search");
        Place((RectTransform)go.transform, 0, Gap, 1f, InputHeight);
        _input.onValueChanged.AddListener(_ => Ui.Guard(Apply));
        _input.onSubmit.AddListener(_ => Ui.Guard(PickFirst));
    }

    private void BuildChips(RectTransform bar, CustomUIWindow window, List<string> groups)
    {
        _group = null;
        _chips.Clear();
        float top = Gap * 2 + InputHeight;
        var all = new List<string> { null };
        all.AddRange(groups);
        for (int i = 0; i < all.Count; i++)
        {
            string group = all[i];
            var go = Ui.Mgr.SpawnElement(CustomUIManager.ElementKind.Button, bar, window, "am_search_chip");
            var element = CustomUIElement.Bind(go, null);
            element.SetText(group == null ? Domain.L.T("list.group_all") : ItemGroups.Name(group));
            if (element.text != null)
            {
                element.text.enableAutoSizing = true;
                element.text.fontSizeMin = 9;
                element.text.fontSizeMax = 13;
                element.text.enableWordWrapping = false;
            }
            int row = i / ChipColumns, column = i % ChipColumns;
            Place((RectTransform)go.transform, (float)column / ChipColumns, top + row * ChipHeight, 1f / ChipColumns, ChipHeight);
            element.OnClick(() => Ui.Guard(() =>
            {
                _group = group;
                Apply();
                _input.ActivateInputField();
            }));
            _chips[group ?? ""] = (element.button, element.button.targetGraphic, element.button.targetGraphic.color);
        }
    }

    private void BuildNoMatch(RectTransform list, CustomUIWindow window, float barHeight)
    {
        _noMatch = Ui.Mgr.SpawnElement(CustomUIManager.ElementKind.Label, list, window, "am_search_none");
        var element = CustomUIElement.Bind(_noMatch, null);
        element.SetText(Tone.Dim(Domain.L.T("list.no_match")));
        if (element.text != null)
        {
            element.text.alignment = TextAlignmentOptions.Center;
            element.text.raycastTarget = false;
        }
        var rt = (RectTransform)_noMatch.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(0, -barHeight);
    }

    /// <summary>放到搜索区内：横向按比例，纵向按距顶部的像素。</summary>
    private static void Place(RectTransform rt, float left, float top, float width, float height)
    {
        rt.anchorMin = new Vector2(left, 1);
        rt.anchorMax = new Vector2(left + width, 1);
        rt.pivot = new Vector2(0, 1);
        rt.offsetMin = new Vector2(1, -top - height + 1);
        rt.offsetMax = new Vector2(-1, -top - 1);
    }

    private void Apply()
    {
        string query = _input.text.Trim();
        bool filtering = query.Length > 0 || _group != null;
        bool Keep(Row r) => (_group == null || r.Group == _group) && ItemGroups.Matches(r.Text, query);

        var shown = _rows.Where(r => r.Pinned).Concat(_rows.Where(r => !r.Pinned && Keep(r))).ToList();
        if (!filtering) shown = _rows;
        foreach (var r in _rows) r.Rect.gameObject.SetActive(false);
        for (int i = 0; i < shown.Count; i++)
        {
            var r = shown[i];
            r.Rect.gameObject.SetActive(true);
            r.Rect.anchoredPosition = new Vector2(r.Rect.anchoredPosition.x, _bottom + _rowHeight * (shown.Count - 1 - i) + _rowHeight * r.Rect.pivot.y);
        }
        _content.sizeDelta = new Vector2(_content.sizeDelta.x, _rowHeight * shown.Count + _padding);
        _noMatch.SetActive(shown.All(r => r.Pinned));

        foreach (var kv in _chips)
        {
            bool active = kv.Key == (_group ?? "");
            kv.Value.button.interactable = !active;
            kv.Value.graphic.color = active ? ActiveChip : kv.Value.color;
        }
        ScrollTo(filtering ? null : _current, shown);
    }

    /// <summary>滚动到某一行的位置，尽量居中；null 回到顶部。</summary>
    private void ScrollTo(Row row, List<Row> shown)
    {
        if (_scroll == null) return;
        Canvas.ForceUpdateCanvases();
        var viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
        float range = _content.rect.height - viewport.rect.height;
        if (range <= 0 || row == null) { _scroll.verticalNormalizedPosition = 1; return; }
        float offset = shown.IndexOf(row) * _rowHeight - (viewport.rect.height - _rowHeight) / 2;
        _scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01(offset / range);
    }

    /// <summary>回车：选第一条搜索结果。没有输入时不做选择，焦点留在搜索框。</summary>
    private void PickFirst()
    {
        var first = _input.text.Trim().Length == 0 ? null : _rows.FirstOrDefault(r => !r.Pinned && r.Rect.gameObject.activeSelf);
        if (first == null) { _input.ActivateInputField(); return; }
        first.Toggle.isOn = !first.Toggle.isOn; // 已选中的行也要触发下拉框的选择和收起
    }
}
