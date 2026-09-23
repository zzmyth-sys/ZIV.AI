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

    /// <summary>Writes status text into the pending bubble; ignored when none is in flight.</summary>
    private void SetStatus(string text)
    {
        if (_pendingTextLabel is not null)
        {
            _pendingTextLabel.Text = text;
        }
    }
}