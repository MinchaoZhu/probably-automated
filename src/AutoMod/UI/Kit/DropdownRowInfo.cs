using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoMod.UI.Kit;

/// <summary>
/// 下拉列表的行悬停时，在列表旁边的固定面板里显示信息，不跟随鼠标，不挡列表。
/// 下拉框每次展开都新建列表，所以挂在下拉框上，发现新列表时往行里加。内容悬停时才取，避免展开时把每一行都算一遍。
/// </summary>
public class DropdownRowInfo : MonoBehaviour
{
    private TMP_Dropdown _dropdown;
    private Func<Func<int, string>> _open;
    private GameObject _decorated;

    /// <param name="open">每次展开调用一次，返回“第几行的信息”；返回 null 的行没有信息。</param>
    public static void Attach(TMP_Dropdown dropdown, Func<Func<int, string>> open)
    {
        if (dropdown == null) return;
        var c = dropdown.gameObject.AddComponent<DropdownRowInfo>();
        c._dropdown = dropdown;
        c._open = open;
    }

    private void LateUpdate()
    {
        if (_dropdown == null || !_dropdown.IsExpanded) { _decorated = null; return; }
        var list = _dropdown.transform.Find("Dropdown List")?.gameObject;
        if (list == null || list == _decorated) return;
        _decorated = list;
        Ui.Guard(() => Decorate(list));
    }

    private void Decorate(GameObject list)
    {
        var rows = list.GetComponentsInChildren<Toggle>(false);
        if (rows.Length == 0) return;
        var text = _open();
        var panel = new RowInfoPanel((RectTransform)list.transform, rows[0].GetComponentInChildren<TMP_Text>());
        for (int i = 0; i < rows.Length; i++)
        {
            int at = i;
            var hover = rows[i].gameObject.AddComponent<RowHover>();
            hover.Text = () => text(at);
            hover.Panel = panel;
        }
    }

    private class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Text;
        public RowInfoPanel Panel;

        public void OnPointerEnter(PointerEventData eventData) => Ui.Guard(() => Panel.Show(Text()));

        public void OnPointerExit(PointerEventData eventData) => Ui.Guard(Panel.Hide);
    }
}

/// <summary>贴着下拉列表的信息面板：默认在右边，放不下时在左边，上下不超出屏幕。</summary>
internal class RowInfoPanel
{
    private const float MaxWidth = 420f;
    private const float Padding = 12f;
    private const float Gap = 6f;
    private const float FrameWidth = 3f;
    private static readonly Color Bright = new(0.96f, 0.95f, 0.9f);

    private readonly RectTransform _list;
    private readonly RectTransform _rect;
    private readonly TMP_Text _text;

    public RowInfoPanel(RectTransform list, TMP_Text style)
    {
        _list = list;
        // 用游戏的面板元素：和所在窗口的边框、底色一致
        var window = Ui.Windows().Select(x => x.win).FirstOrDefault(w => list.IsChildOf(w.Rect));
        _rect = (RectTransform)Ui.Mgr.SpawnElement(CustomUIManager.ElementKind.Panel, list, window, "am_row_info").transform;
        AddFill(list);

        _text = UnityEngine.Object.Instantiate(style, _rect);
        _text.name = "text";
        _text.raycastTarget = false;
        _text.enableAutoSizing = false;
        _text.fontStyle = FontStyles.Normal;
        _text.color = Bright;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.enableWordWrapping = true;
        _text.overflowMode = TextOverflowModes.Overflow;
        Stretch(_text.rectTransform, Padding);
        _rect.gameObject.SetActive(false);
    }

    /// <summary>边框内铺和列表相同的深色底，文字更清楚。</summary>
    private void AddFill(RectTransform list)
    {
        var go = new GameObject("fill", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_rect, false);
        Stretch((RectTransform)go.transform, FrameWidth);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        if (list.GetComponent<Image>() is not { } source) { image.color = new Color(0.16f, 0.13f, 0.19f); return; }
        image.sprite = source.sprite;
        image.type = source.type;
        image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        image.color = source.color;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    public void Show(string text)
    {
        if (string.IsNullOrEmpty(text)) { Hide(); return; }
        _text.text = text;
        // 带宽度的 GetPreferredValues 只按宽度算高度，返回的宽度不受限：先取一行的宽度再封顶
        float width = Mathf.Min(_text.GetPreferredValues(text).x, MaxWidth);
        var size = new Vector2(width, _text.GetPreferredValues(text, width, 0).y) + Vector2.one * (Padding * 2 + 1);
        _rect.gameObject.SetActive(true);

        var list = Bounds(_list);
        var root = Bounds((RectTransform)_list.GetComponentInParent<Canvas>().rootCanvas.transform);
        float scale = _list.lossyScale.x;
        bool right = list.xMax + (Gap + size.x) * scale <= root.xMax;
        _rect.anchorMin = _rect.anchorMax = new Vector2(right ? 1 : 0, 1);
        _rect.pivot = new Vector2(right ? 0 : 1, 1);
        _rect.sizeDelta = size;
        float below = root.yMin - (list.yMax - size.y * scale);
        _rect.anchoredPosition = new Vector2(right ? Gap : -Gap, below > 0 ? below / scale : 0);
    }

    public void Hide() => _rect.gameObject.SetActive(false);

    private static Rect Bounds(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }
}

/// <summary>内容变化并稳定一小段时间后调用一次回调。用来刷新窗口里的一段文字，不用重建窗口。</summary>
public class ChangeWatcher : MonoBehaviour
{
    private const float Poll = 0.1f;
    private const float Settle = 0.3f;

    private Func<string> _key;
    private Action _changed;
    private string _last;
    private float _nextPoll, _changedAt = -1;

    public static void Attach(GameObject go, Func<string> key, Action changed)
    {
        if (go == null) return;
        var c = go.AddComponent<ChangeWatcher>();
        c._key = key;
        c._changed = changed;
        c._last = key();
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextPoll) return;
        _nextPoll = now + Poll;
        Ui.Guard(() =>
        {
            string k = _key();
            if (k != _last) { _last = k; _changedAt = now; return; }
            if (_changedAt >= 0 && now - _changedAt >= Settle)
            {
                _changedAt = -1;
                _changed();
            }
        });
    }
}
