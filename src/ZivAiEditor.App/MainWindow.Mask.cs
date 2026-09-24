using System;
using System.Threading.Tasks;
using ZivAiEditor.App.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Mask half of <see cref="MainWindow"/> (Step 9C.7): routes a completed mask draw from the
/// preview window into the session. A mask is a node property (no new node), so it is stored
/// on the node via <c>SetNodeMask</c>. Split out of the main file to keep each file under the
/// Z8 line budget.
///
/// <para><b>E2 / S2:</b> the latest mask PNG export is held at <b>window level</b>
/// (<see cref="_pendingMaskExport"/>), not on <see cref="ImagePreview"/>, so a save / close /
/// send can await it even after the preview window has been closed. Entering a mask tool also
/// aligns the working node to the previewed node (<c>SessionViewModel.AlignForMask</c>),
/// because the parser reads the <b>current</b> node's mask.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Latest scheduled mask PNG export (E2 / S2); <c>null</c> before the first draw.</summary>
    private Task? _pendingMaskExport;

    /// <summary>
    /// Stores a drawn mask (or its clear) on the node shown in the preview — no node is
    /// appended. The history refresh (inside the view model) reflects the updated node.
    /// Step E1: the bubble shows the mask visualization, which reads the exported PNG, so the
    /// flush is awaited and the chat re-rendered once the file has landed.
    /// </summary>
    private async void OnPreviewMaskCompleted(object? sender, MaskCompletedEventArgs e)
    {
        _vm.SetNodeMask(e.NodeId, e.Mask);

        // The PNG export is chained behind earlier strokes; wait for it so the bubble overlay
        // can decode the finished file, then refresh the stream (the in-view-model rebuild may
        // have run before the file existed).
        await FlushPendingMaskAsync();
        if (!_vm.IsBusy)
        {
            _vm.RebuildContext();
        }
    }

    /// <summary>Keeps the latest chained export at window level so a flush can always await it (S2).</summary>
    private void OnPreviewMaskExportScheduled(object? sender, Task task)
        => _pendingMaskExport = task;

    /// <summary>
    /// Aligns the working node to the node being masked when the mask tool is activated (E2=A).
    /// The parser reads the current node's mask, so the previewed node must become current or a
    /// drawn mask would silently not reach the pipeline.
    /// </summary>
    private void OnPreviewMaskToolEntered(object? sender, EventArgs e)
    {
        if (_imagePreview?.NodeId is { Length: > 0 } nodeId)
        {
            _vm.AlignForMask(nodeId);
        }
    }
}
