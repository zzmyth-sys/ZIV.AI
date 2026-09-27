using System;
using System.Diagnostics;
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
    /// True when the current preview session produced at least one mask change (R3.1). The chat
    /// bubble is refreshed once when the preview closes, not per stroke.
    /// </summary>
    private bool _maskEditedInPreview;

    /// <summary>
    /// Stores a drawn mask (or its clear) on the node shown in the preview — no node is
    /// appended. The history refresh (inside the view model) reflects the updated node.
    ///
    /// <para><b>R3.1:</b> the chat stream is <b>not</b> rebuilt on a stroke end (that caused a
    /// visible stall). The node property is written, the PNG export is flushed, and the bubble
    /// overlay is refreshed once when the preview window closes.</para>
    /// </summary>
    private async void OnPreviewMaskCompleted(object? sender, MaskCompletedEventArgs e)
    {
        // B11: an `async void` handler has no caller to observe its exceptions — an unhandled
        // one crashes the process. Keep the `async void` signature (Avalonia event contract) but
        // observe failures here: log + surface a hint instead of throwing on the UI thread.
        // (The other MainWindow `async void` handlers are tracked in DEVLOG for a later pass.)
        try
        {
            MaskDiagnostics.Log(
                $"[mask] completed node={e.NodeId} spec={e.Mask?.MaskImagePath ?? "null"} current={_vm.Session.CurrentNodeId}");

            _vm.SetNodeMask(e.NodeId, e.Mask);
            _maskEditedInPreview = true;

            // Chain behind earlier strokes so the PNG is on disk before the close-time refresh.
            await FlushPendingMaskAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[mask] OnPreviewMaskCompleted failed: {ex}");
            MaskDiagnostics.Log($"[mask] OnPreviewMaskCompleted failed: {ex.GetType().Name}: {ex.Message}");
            _vm.AddHint($"遮罩处理失败：{ex.Message}");
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
