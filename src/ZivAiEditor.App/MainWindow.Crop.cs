using ZivAiEditor.App.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Crop half of <see cref="MainWindow"/> (Step 9C.6-B): routes a confirmed crop from the
/// preview window into the session. A crop is a node property (no new node), so it is
/// stored on the node via <c>SetNodeCrop</c>. Split out of the main file to keep each file
/// under the Z8 line budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Stores a confirmed crop (Step 9C.6-B) on the node shown in the preview — no node is
    /// appended. The history refresh (inside the view model) reflects the updated node.
    /// </summary>
    private void OnPreviewCropCompleted(object? sender, CropCompletedEventArgs e)
    {
        _vm.SetNodeCrop(e.NodeId, e.Crop);
        // P1 · /扩图 relocation: only an outpaint crop needs the previewed node to become
        // current (the parser reads the current node's crop for /扩图); an inner crop must not
        // move the selection or claim to be an outpaint. Either way the send gate refreshes,
        // since an outpaint crop may now exist.
        if (e.Crop.IsOutpaint())
        {
            _vm.AlignForCrop(e.NodeId);
        }

        UpdateSendEnabled();
    }
}
