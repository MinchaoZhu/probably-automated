using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AutoMod.UI.Kit;

/// <summary>
/// 给下拉列表的行加悬停提示。下拉框每次展开都新建列表，所以挂在下拉框上，发现新列表时往行里加。
/// 提示内容悬停时才取，避免展开时把每一行都算一遍。
/// </summary>
public class DropdownRowInfo : MonoBehaviour
{
    private TMP_Dropdown _dropdown;
    private Func<Func<int, string>> _open;
    private GameObject _decorated;

    /// <param name="open">每次展开调用一次，返回“第几行的提示”；返回 null 的行没有提示。</param>
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
        var text = _open();
        var rows = list.GetComponentsInChildren<Toggle>(false);
        for (int i = 0; i < rows.Length; i++)
        {
            int at = i;
            // 先挂提示内容，再挂游戏的提示组件：同一物体上的事件按组件顺序执行
            rows[i].gameObject.AddComponent<RowHover>().Text = () => text(at);
            rows[i].gameObject.AddComponent<CustomUITooltipTrigger>();
            rows[i].gameObject.AddComponent<RowLift>();
        }
    }

    private class RowHover : MonoBehaviour, IPointerEnterHandler
    {
        public Func<string> Text;

        public void OnPointerEnter(PointerEventData eventData) => Ui.Guard(() =>
        {
            var trigger = GetComponent<CustomUITooltipTrigger>();
            if (trigger != null) trigger.tooltipText = Text();
        });
    }
}

/// <summary>把刚显示的提示抬到下拉列表之上：下拉列表自带高层级画布，否则会盖住提示。</summary>
internal class RowLift : MonoBehaviour, IPointerEnterHandler
{
    private const int AboveDropdown = 30010;

    public void OnPointerEnter(PointerEventData eventData) => Ui.Guard(() =>
    {
        var tip = GameObject.Find("custom_ui_tooltip");
        if (tip == null) return;
        var canvas = tip.GetComponent<Canvas>() ?? tip.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = AboveDropdown;
    });
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
