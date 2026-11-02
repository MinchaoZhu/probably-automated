using UnityEngine;

namespace AutoMod.UI.Kit;

/// <summary>系统剪贴板（排班的导入导出用；不读写文件）。</summary>
public static class Clipboard
{
    public static string Text
    {
        get { try { return GUIUtility.systemCopyBuffer ?? ""; } catch { return ""; } }
        set { try { GUIUtility.systemCopyBuffer = value ?? ""; } catch { } }
    }
}
