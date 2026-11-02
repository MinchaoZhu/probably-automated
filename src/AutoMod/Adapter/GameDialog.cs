namespace AutoMod.Adapter;

/// <summary>
/// 游戏的通用对话框（GenericConfirmationUIManager）：机器、物品做不了时用它提示，危险操作用它要玩家确认。
/// 调用前记下它开没开；调用后新开了就读出文字、用游戏自己的 ClosePanel 关掉（不按确认）。
/// </summary>
public sealed class GameDialog
{
    private readonly bool _wasOpen;

    private GameDialog(bool wasOpen) => _wasOpen = wasOpen;

    private static GenericConfirmationUIManager M => GenericConfirmationUIManager.Instance;

    private static bool IsOpen => M != null && M.panel != null && M.panel.activeInHierarchy;

    public static GameDialog Snapshot() => new(IsOpen);

    /// <summary>调用后新开的对话框：文字、是不是要确认的（两个按钮）；已关掉。没有新开返回 null。</summary>
    public (string text, bool question)? Opened()
    {
        if (_wasOpen || !IsOpen) return null;
        string text = M.bodyTMP != null ? M.bodyTMP.text : "";
        bool question = M.cancelButton != null && M.cancelButton.activeSelf;
        M.ClosePanel();
        return (string.IsNullOrWhiteSpace(text) ? Domain.L.T("gamedialog.no_text") : text.Trim(), question);
    }
}
