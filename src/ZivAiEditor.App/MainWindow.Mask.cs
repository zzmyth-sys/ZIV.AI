using ZivAiEditor.App.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Mask half of <see cref="MainWindow"/> (Step 9C.7): routes a completed mask draw from the
/// preview window into the session. A mask is a node property (no new node), so it is stored
/// on the node via <c>SetNodeMask</c>. Split out of the main file to keep each file under the
/// Z8 line budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Stores a drawn mask (or its clear) on the node shown in the preview — no node is
    /// appended. The history refresh (inside the view model) reflects the updated node.
    /// </summary>
    private void OnPreviewMaskCompleted(object? sender, MaskCompletedEventArgs e)
    {
        _vm.SetNodeMask(e.NodeId, e.Mask);
    }
}
