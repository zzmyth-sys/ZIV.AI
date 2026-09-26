using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ZivAiEditor.App.Controls.Modes;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>Carries the node and its new mask (or <c>null</c> when cleared) after a draw (Step 9C.7).</summary>
public sealed class MaskCompletedEventArgs : EventArgs
{
    public MaskCompletedEventArgs(string nodeId, MaskSpec? mask)
    {
        NodeId = nodeId;
        Mask = mask;
    }

    /// <summary>The node whose mask changed.</summary>
    public string NodeId { get; }

    /// <summary>The new mask, or <c>null</c> when the mask was cleared / emptied.</summary>
    public MaskSpec? Mask { get; }
}

/// <summary>
/// Mask-tool half of <see cref="ImagePreview"/> (Step 9C.7): the <see cref="MaskState"/>, the
/// <see cref="MaskOverlay"/>, the brush / eraser pointer interaction, and the off-thread PNG
/// export. Split out of the main file to keep each file under the Z8 line budget; the halves
/// share the same partial-class members.
///
/// <para>The mask is the node's intrinsic property: drawing updates the current node's mask
/// (via <see cref="MaskCompleted"/>) and never appends a node. Coordinates are relative to the
/// current <b>pipeline</b> image (the crop result when the node has one), so mask mode shows
/// that image.</para>
/// </summary>
public partial class ImagePreview
{
    private MaskState? _mask;
    private MaskOverlay? _maskOverlay;
    private MaskModePanel? _maskPanel;
    private string? _maskSessionId;
    private string? _maskNodeId;
    private MaskSpec? _nodeMask;
    private Task _maskWrite = Task.CompletedTask;
    private bool _maskPointerDown;
    // Suppression depth (R3.2): a counter, not a bool, so nested suppressions are safe. A bool
    // was cleared early by the inner RefreshMaskCanvas finally during ResetMask, which leaked a
    // Null (empty) mask commit on close and wiped the node's mask.
    private int _suppressMaskExportDepth;
    private int _maskLoadGeneration;
    private bool _maskModeActive;

    /// <summary>Raised after a mask draw / clear (App stores it on the node).</summary>
    public event EventHandler<MaskCompletedEventArgs>? MaskCompleted;

    /// <summary>
    /// Raised when the mask brush / eraser becomes active (E2=A): the App aligns the working
    /// node to the previewed node, so the node the user masks is the node the parser reads.
    /// Not raised again while a mask tool stays active.
    /// </summary>
    public event EventHandler? MaskToolEntered;

    /// <summary>
    /// Raised when a mask PNG export is scheduled, carrying the chained task (E2 / S2). The App
    /// keeps it at window level so a flush can await it even after this window closes.
    /// </summary>
    public event EventHandler<Task>? MaskExportScheduled;

    /// <summary>The mask state (diagnostics / tests).</summary>
    public MaskState? Mask => _mask;

    /// <summary>True when the active tool draws on the mask.</summary>
    private bool IsMaskActive => _tools.CurrentTool is ToolMode.MaskBrush or ToolMode.Eraser;

    /// <summary>Wires the mask overlay / toolbar actions. Called from <see cref="Init"/>.</summary>
    private void InitMask()
    {
        _mask = new MaskState();

        _maskOverlay = this.FindControl<MaskOverlay>("PART_MaskOverlay");
        if (_maskOverlay is not null)
        {
            _maskOverlay.Imaging = _imaging;
            _maskOverlay.Attach(_model);
            _maskOverlay.SetState(_mask);
        }

        // N4: the mask chrome is a dumb panel; wire its events once here.
        _maskPanel = this.FindControl<MaskModePanel>("PART_MaskPanel");
        if (_maskPanel is not null)
        {
            // Re-push state after the click: a click on the already-active toggle flips
            // IsChecked before Click fires, and SetTool is a no-op when the tool is unchanged.
            _maskPanel.BrushChanged += (_, _) =>
            {
                _tools.SetTool(ToolMode.MaskBrush);
                SyncMaskPanel();
            };
            _maskPanel.EraserChanged += (_, _) =>
            {
                _tools.SetTool(ToolMode.Eraser);
                SyncMaskPanel();
            };
            _maskPanel.ClearRequested += (_, _) => _mask?.Clear();
            _maskPanel.UndoRequested += (_, _) => _mask?.Undo();
            _maskPanel.ResetRequested += (_, _) => _mask?.Clear();
            _maskPanel.ReturnRequested += (_, _) => _tools.SetTool(ToolMode.None);
            _maskPanel.ConfirmRequested += (_, _) => Close();
            _maskPanel.BrushSizeChanged += OnToolbarBrushSizeChanged;
            _maskPanel.FeatherChanged += OnToolbarFeatherChanged;
        }

        // R1: Changed fires during a stroke (incremental repaint only); Committed fires once
        // per durable change (stroke end / clear / undo / load) and drives the export + the
        // full (feathered) rebuild.
        _mask.Changed += (_, _) => OnMaskStrokeChanged();
        _mask.Committed += (_, _) => OnMaskCommitted();

        _tools.StateChanged += (_, _) => UpdateMaskMode();
        UpdateMaskMode();
    }

    /// <summary>
    /// R3.3: the mask overlay is visible whenever the node has a mask (stored or just painted)
    /// <b>or</b> a mask tool is active — independent of the tool. It only decides whether the
    /// overlay is <b>shown</b>, never whether it can be edited (the toolbar stays tool-gated).
    /// Keeping it visible for a stored mask (even before its PNG loads) avoids a flash.
    /// </summary>
    private void UpdateMaskOverlayVisibility()
    {
        if (_maskOverlay is null)
        {
            return;
        }

        _maskOverlay.IsVisible = IsMaskActive
            || (_mask?.HasContent ?? false)
            || _nodeMask is not null;
    }

    /// <summary>Pushes tool / undo / clear state and the sliders into the mask panel (no events).</summary>
    private void SyncMaskPanel()
    {
        if (_mask is null || _maskPanel is null)
        {
            return;
        }

        _maskPanel.SetBrushSize(_mask.BrushDiameter);
        _maskPanel.SetFeather(_mask.FeatherPx);
        _maskPanel.SetToolState(_tools.CurrentTool, _tools.CanUndo, _tools.CanClearMask);
        _maskOverlay?.SetBrush(_mask.BrushDiameter);
    }

    private void OnToolbarBrushSizeChanged(object? sender, int diameterPx)
    {
        if (_mask is null)
        {
            return;
        }

        _mask.BrushDiameter = diameterPx;
        _maskOverlay?.SetBrush(_mask.BrushDiameter);

        // The brush circle follows the cursor; repaint even when no mask exists yet.
        _maskOverlay?.InvalidateVisual();
    }

    private void OnToolbarFeatherChanged(object? sender, int featherPx)
    {
        if (_mask is null)
        {
            return;
        }

        _mask.FeatherPx = featherPx;

        // Feather changes only display / export; force a bitmap rebuild and re-export.
        _maskOverlay?.MarkDirty();
        ScheduleMaskExport();
    }

    /// <summary>
    /// Reacts to the tool state: entering brush / eraser (with an image) shows the mask on
    /// the current pipeline image; leaving hides it. Compare mode is exited first so the two
    /// modes never overlap.
    /// </summary>
    private void UpdateMaskMode()
    {
        if (_mask is null)
        {
            return;
        }

        var wantMask = IsMaskActive && _tools.HasImage;
        var wasMaskActive = _maskModeActive;
        _maskModeActive = wantMask;

        if (wantMask)
        {
            if (_compareState.IsCompareMode)
            {
                _compareState.SetCompareMode(false);
            }

            // The mask lives in the pipeline image's coordinates, so show that image.
            if (!string.IsNullOrWhiteSpace(_displayPath)
                && !string.Equals(_path, _displayPath, StringComparison.OrdinalIgnoreCase))
            {
                LoadImage(_displayPath);
            }

            if (_maskPanel is not null)
            {
                _maskPanel.IsVisible = true;
            }

            SyncMaskPanel();
            RefreshMaskCanvas();
        }
        else
        {
            if (_maskPanel is not null)
            {
                _maskPanel.IsVisible = false;
            }

            _maskOverlay?.ClearPointer();
        }

        // R3.3: overlay visibility no longer follows the tool; it follows mask presence.
        UpdateMaskOverlayVisibility();
        _maskOverlay?.InvalidateVisual();

        // E2=A: entering a mask tool tells the App to make the previewed node current.
        if (wantMask && !wasMaskActive)
        {
            MaskToolEntered?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sizes the mask canvas to the loaded image and, when the size changes, loads the node's
    /// stored mask. Called from <c>LoadAsync</c> after the decode (and on mask-enter).
    /// </summary>
    private void RefreshMaskCanvas()
    {
        if (_mask is null || !_model.HasImage)
        {
            return;
        }

        // R3.3: still size / load when the node carries a stored mask, even if no tool is active.
        if (!IsMaskActive && !_mask.HasImage && _nodeMask is null)
        {
            return;
        }

        var width = (int)Math.Round(_model.ImageWidth);
        var height = (int)Math.Round(_model.ImageHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_mask.Width != width || _mask.Height != height)
        {
            // Restore the node's user-explicit feather before resizing (defaults to 0).
            _mask.FeatherPx = _nodeMask?.FeatherPx ?? 0;

            _suppressMaskExportDepth++;
            try
            {
                _mask.SetCanvas(width, height);
            }
            finally
            {
                _suppressMaskExportDepth--;
            }

            var stored = _nodeMask;
            if (stored is not null
                && stored.Width == width && stored.Height == height
                && !string.IsNullOrWhiteSpace(stored.MaskImagePath)
                && File.Exists(stored.MaskImagePath))
            {
                _ = LoadMaskAsync(stored.MaskImagePath, width, height);
            }
        }

        SyncMaskPanel();
        _tools.NotifyUndoStackChanged(_mask.CanUndo);
        _tools.NotifyMaskChanged(_mask.CanClear);
        UpdateMaskOverlayVisibility();
    }

    private async Task LoadMaskAsync(string path, int width, int height)
    {
        var generation = ++_maskLoadGeneration;
        var loaded = await _imaging.LoadMaskAsync(path);
        if (generation != _maskLoadGeneration || _mask is null || loaded is not { } data)
        {
            return;
        }

        if (data.Width != width || data.Height != height)
        {
            return;
        }

        _suppressMaskExportDepth++;
        try
        {
            _mask.LoadFrom(data.Pixels, data.Width, data.Height);
        }
        finally
        {
            _suppressMaskExportDepth--;
        }

        _maskOverlay?.MarkDirty();
        UpdateMaskOverlayVisibility();
    }

    /// <summary>
    /// Remembers a node's stored mask and resets the live canvas for a node switch. The
    /// canvas is re-sized / re-loaded by <see cref="RefreshMaskCanvas"/> once the node's image
    /// is decoded.
    /// </summary>
    public void SetNodeMaskSource(MaskSpec? mask)
    {
        _nodeMask = mask;
        _maskLoadGeneration++;

        if (_mask is not null)
        {
            // Restore the node's stored feather (0 when the node has no mask / old project).
            _mask.FeatherPx = mask?.FeatherPx ?? 0;

            _suppressMaskExportDepth++;
            try
            {
                _mask.SetCanvas(0, 0);
            }
            finally
            {
                _suppressMaskExportDepth--;
            }

            _tools.NotifyUndoStackChanged(_mask.CanUndo);
            _tools.NotifyMaskChanged(_mask.CanClear);
        }

        SyncMaskPanel();
        _maskOverlay?.MarkDirty();
        UpdateMaskOverlayVisibility();
    }

    // --- Pointer routing (called by the main partial's handlers) ---------------

    private void MaskOnPressed(Point viewportPoint, PointerPressedEventArgs e)
    {
        if (_mask is null || !IsMaskActive || _canvas is null || !_mask.HasImage)
        {
            return;
        }

        var (ix, iy) = _model.ViewportToImage(viewportPoint.X, viewportPoint.Y);
        var erase = _tools.CurrentTool == ToolMode.Eraser;
        if (!_mask.BeginStroke(ix, iy, erase))
        {
            return;
        }

        _maskPointerDown = true;
        e.Pointer.Capture(_canvas);
        _maskOverlay?.MarkDirty();
    }

    private void MaskOnMoved(Point viewportPoint)
    {
        if (_mask is null || !_maskPointerDown)
        {
            return;
        }

        var (ix, iy) = _model.ViewportToImage(viewportPoint.X, viewportPoint.Y);
        _mask.ContinueStroke(ix, iy);
    }

    private void MaskOnReleased(PointerReleasedEventArgs e)
    {
        if (_mask is null || !_maskPointerDown || _canvas is null)
        {
            return;
        }

        _maskPointerDown = false;
        e.Pointer.Capture(null);

        var point = e.GetPosition(_canvas);
        var (ix, iy) = _model.ViewportToImage(point.X, point.Y);
        _mask.ContinueStroke(ix, iy);
        _mask.EndStroke();
    }

    // --- Change / export -------------------------------------------------------

    /// <summary>
    /// Intermediate stroke mutation (R1): patch only the touched rectangle of the overlay.
    /// Deliberately does <b>not</b> rebuild the whole bitmap, apply feather, export, or touch
    /// the session — so drawing never blocks the UI thread or storms the chat.
    /// </summary>
    private void OnMaskStrokeChanged()
    {
        if (_mask is null || _maskOverlay is null)
        {
            return;
        }

        if (_mask.IsStrokeActive && _mask.TakeDirtyRegion() is { } region)
        {
            _maskOverlay.PatchRegion(_mask, region.X, region.Y, region.Width, region.Height);
        }

        UpdateMaskOverlayVisibility();
    }

    /// <summary>
    /// Durable mask change (R1): full feathered rebuild, tool-flag refresh and one PNG export
    /// (which in turn raises <c>MaskCompleted</c> → one <c>SetNodeMask</c> per stroke).
    /// </summary>
    private void OnMaskCommitted()
    {
        _maskOverlay?.MarkDirty();

        if (_mask is not null)
        {
            _tools.NotifyUndoStackChanged(_mask.CanUndo);
            _tools.NotifyMaskChanged(_mask.CanClear);
        }

        ScheduleMaskExport();
        UpdateMaskOverlayVisibility();
    }

    /// <summary>
    /// Builds the node's <see cref="MaskSpec"/> (null when empty), notifies the App, and
    /// starts an off-thread PNG export of a buffer snapshot. Exports are chained so a stale
    /// frame can never overwrite a newer one on disk (the send pipeline awaits the last).
    /// </summary>
    private void ScheduleMaskExport()
    {
        if (_suppressMaskExportDepth > 0 || _mask is null || _maskSessionId is null || _maskNodeId is null)
        {
            return;
        }

        MaskSpec? spec = null;
        if (_mask.HasContent)
        {
            spec = new MaskSpec
            {
                MaskImagePath = _imaging.ResolveMaskPath(_maskSessionId, _maskNodeId),
                Width = _mask.Width,
                Height = _mask.Height,
                IsBinary = true,
                Invert = false,
                FeatherPx = _mask.FeatherPx,
            };
        }

        _nodeMask = spec;

        // Start the export <b>before</b> raising MaskCompleted (S2): the App captures the
        // chained task from MaskExportScheduled, then its MaskCompleted handler can await it
        // (bubble overlay + save / close flush) without depending on this window staying open.
        if (spec is not null)
        {
            var sessionId = _maskSessionId;
            var nodeId = _maskNodeId;
            var pixels = _mask.CopyPixels();
            var width = _mask.Width;
            var height = _mask.Height;
            var featherPx = _mask.FeatherPx;
            _maskWrite = RunMaskExportAsync(_maskWrite, sessionId, nodeId, pixels, width, height, featherPx);
            MaskExportScheduled?.Invoke(this, _maskWrite);
        }

        MaskCompleted?.Invoke(this, new MaskCompletedEventArgs(_maskNodeId, spec));
    }

    private async Task RunMaskExportAsync(
        Task previous,
        string sessionId,
        string nodeId,
        byte[] pixels,
        int width,
        int height,
        int featherPx)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed earlier export must not block the next one (it was already logged).
            MaskDiagnostics.Log($"[mask] previous export faulted: {ex.Message}");
        }

        try
        {
            var output = await _imaging
                .ExportMaskAsync(sessionId, nodeId, pixels, width, height, featherPx)
                .ConfigureAwait(false);
            MaskDiagnostics.Log($"[mask] export ok node={nodeId} feather={featherPx} path={output}");
        }
        catch (Exception ex)
        {
            // Do not swallow (R1/D): record the failure, then let the chained task fault.
            // Every flush awaiter catches, and the next export skips a faulted predecessor.
            MaskDiagnostics.Log($"[mask] export FAILED node={nodeId} feather={featherPx}: {ex}");
            throw;
        }
    }

    /// <summary>Drops the live mask state and tool flags (empty image / close).</summary>
    private void ResetMask()
    {
        _maskLoadGeneration++;
        _maskPointerDown = false;

        if (_mask is not null)
        {
            _suppressMaskExportDepth++;
            try
            {
                _mask.SetCanvas(0, 0);
            }
            finally
            {
                _suppressMaskExportDepth--;
            }

            _tools.NotifyUndoStackChanged(_mask.CanUndo);
            _tools.NotifyMaskChanged(_mask.CanClear);
        }

        _maskOverlay?.MarkDirty();
        UpdateMaskOverlayVisibility();
    }
}
