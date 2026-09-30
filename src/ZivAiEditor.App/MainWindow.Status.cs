using Avalonia.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Status half of <see cref="MainWindow"/> (Step 9C.3): there is no separate status row —
/// progress / status text is routed into the in-flight "生成中" chat bubble. Split out of
/// the main file to keep it under the Z8 line budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>Text label of the in-flight "生成中" bubble, if any.</summary>
    private TextBlock? _pendingTextLabel;

    /// <summary>
    /// B14: the most recent <see cref="SetStatus"/> text, remembered so a chat rebuild can re-apply
    /// it to the recreated pending bubble's label (the previous label control is destroyed by
    /// <c>RenderChat</c>). <c>null</c> when no status is set / no pending bubble exists.
    /// </summary>
    private string? _statusText;

    /// <summary>The pending bubble's current label text, for headless tests (test-only; B14).</summary>
    internal string? PendingStatusText => _pendingTextLabel?.Text;

    /// <summary>Drives <see cref="SetStatus"/> from headless tests (test-only seam; B14).</summary>
    internal void SetStatusForTest(string text) => SetStatus(text);

    /// <summary>
    /// Writes status text into the pending bubble; the value is also remembered so a chat rebuild
    /// can re-apply it to the recreated label. A no-op write (but still cached) when no bubble is
    /// in flight, because there is no separate status row to show it.
    /// </summary>
    private void SetStatus(string text)
    {
        _statusText = text;
        if (_pendingTextLabel is not null)
        {
            _pendingTextLabel.Text = text;
        }
    }
}