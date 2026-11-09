using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoMod.UI.Kit;

/// <summary>
/// 给下拉列表的行加一个“编辑”。下拉框每次展开都新建列表，所以挂在下拉框上，发现新列表时往行里加。
/// 点行本身是选择；点“编辑”只收起列表并调用回调，不改变选择。
/// </summary>
public class DropdownRowEdit : MonoBehaviour
{
    private const float ColumnWidth = 64f;
    private const float RightPadding = 10f;

    private TMP_Dropdown _dropdown;
    private string _label;
    private IReadOnlyList<Action> _edits;
    private GameObject _decorated;

    /// <param name="edits">和选项一一对应；null 的行没有“编辑”。</param>
    public static void Attach(TMP_Dropdown dropdown, string label, IReadOnlyList<Action> edits)
    {
        if (dropdown == null) return;
        var c = dropdown.gameObject.AddComponent<DropdownRowEdit>();
        c._dropdown = dropdown;
        c._label = label;
        c._edits = edits;
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
        // 列表里的模板行在展开后是隐藏的，只取显示着的行，顺序和选项一致
        var rows = list.GetComponentsInChildren<Toggle>(false);
        for (int i = 0; i < rows.Length && i < _edits.Count; i++)
            if (_edits[i] != null) AddButton(rows[i], _edits[i]);
    }

    private void AddButton(Toggle row, Action edit)
    {
        var text = row.GetComponentInChildren<TMP_Text>();
        if (text == null) return;

        var label = Instantiate(text, row.transform);
        label.name = "am_row_edit";
        label.text = _label;
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.raycastTarget = true;
        var rt = label.rectTransform;
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 0.5f);
        rt.offsetMin = new Vector2(-ColumnWidth - RightPadding, 0);
        rt.offsetMax = new Vector2(-RightPadding, 0);

        // 行文字让出这一栏，放不下时用省略号，不和“编辑”重叠
        text.rectTransform.offsetMax -= new Vector2(ColumnWidth + RightPadding, 0);
        text.enableAutoSizing = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;

        var button = label.gameObject.AddComponent<Button>();
        button.targetGraphic = label;
        var colors = button.colors;
        colors.normalColor = new Color(0.72f, 0.72f, 0.72f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = new Color(0.72f, 0.72f, 0.72f);
        colors.pressedColor = new Color(0.5f, 0.5f, 0.5f);
        button.colors = colors;
        button.onClick.AddListener(() => Ui.Guard(() =>
        {
            _dropdown.Hide();
            edit();
        }));
    }
}
