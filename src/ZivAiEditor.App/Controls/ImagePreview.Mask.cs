using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private string? _maskSessionId;
    private string? _maskNodeId;
    private MaskSpec? _nodeMask;
    private Task _maskWrite = Task.CompletedTask;
    private bool _maskPointerDown;
    private bool _suppressMaskExport;
    private int _maskLoadGeneration;

    /// <summary>Raised after a mask draw / clear (App stores it on the node).</summary>
    public event EventHandler<MaskCompletedEventArgs>? MaskCompleted;

    /// <summary>The mask state (diagnostics / tests).</summary>
    public MaskState? Mask => _mask;

    /// <summary>Awaits the last scheduled mask export (called before a send reads the mask).</summary>
    public Task FlushMaskAsync() => _maskWrite;

    /// <summary>True when the active tool draws on the mask.</summary>
    private bool IsMaskActive => _tools.CurrentTool is ToolMode.MaskBrush or ToolMode.Eraser;

    /// <summary>Wires the mask overlay / toolbar actions. Called from <see cref="Init"/>.</summary>
    private void InitMask()
    {
        _mask = new MaskState();

        _maskOverlay = this.FindControl<MaskOverlay>("PART_MaskOverlay");
        _maskOverlay?.Attach(_model);
        _maskOverlay?.SetState(_mask);

        _mask.Changed += (_, _) => OnMaskChanged();

        if (_toolbar is not null)
        {
            _toolbar.ClearMaskRequested += (_, _) => _mask?.Clear();
            _toolbar.UndoRequested += (_, _) => _mask?.Undo();
        }

        _tools.StateChanged += (_, _) => UpdateMaskMode();
        UpdateMaskMode();
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

            if (_maskOverlay is not null)
            {
                _maskOverlay.IsVisible = true;
            }

            RefreshMaskCanvas();
        }
        else if (_maskOverlay is not null)
        {
            _maskOverlay.IsVisible = false;
        }

        _maskOverlay?.InvalidateVisual();
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

        if (!IsMaskActive && !_mask.HasImage)
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
            _suppressMaskExport = true;
            try
            {
                _mask.SetCanvas(width, height);
            }
            finally
            {
                _suppressMaskExport = false;
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

        _tools.NotifyUndoStackChanged(_mask.CanUndo);
        _tools.NotifyMaskChanged(_mask.CanClear);
    }

    private async Task LoadMaskAsync(string path, int width, int height)
    {
        var generation = ++_maskLoadGeneration;
        var loaded = await MaskExporter.TryLoadAsync(path);
        if (generation != _maskLoadGeneration || _mask is null || loaded is not { } data)
        {
            return;
        }

        if (data.Width != width || data.Height != height)
        {
            return;
        }

        _suppressMaskExport = true;
        try
        {
            _mask.LoadFrom(data.Pixels, data.Width, data.Height);
        }
        finally
        {
            _suppressMaskExport = false;
        }

        _maskOverlay?.MarkDirty();
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
            _suppressMaskExport = true;
            try
            {
                _mask.SetCanvas(0, 0);
            }
            finally
            {
                _suppressMaskExport = false;
            }

            _tools.NotifyUndoStackChanged(_mask.CanUndo);
            _tools.NotifyMaskChanged(_mask.CanClear);
        }

        _maskOverlay?.MarkDirty();
    }

    // --- Pointer routing (called by the main partial's handlers) ---------------

    private void MaskOnPressed(Point viewportPoint, PointerPressedEventArgs e)
    {
        if (_mask is null || !IsMaskActive || _box is null || !_mask.HasImage)
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
        e.Pointer.Capture(_box);
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
        if (_mask is null || !_maskPointerDown || _box is null)
        {
            return;
        }

        _maskPointerDown = false;
        e.Pointer.Capture(null);

        var point = e.GetPosition(_box);
        var (ix, iy) = _model.ViewportToImage(point.X, point.Y);
        _mask.ContinueStroke(ix, iy);
        _mask.EndStroke();
    }

    // --- Change / export -------------------------------------------------------

    private void OnMaskChanged()
    {
        _maskOverlay?.MarkDirty();

        if (_mask is not null)
        {
            _tools.NotifyUndoStackChanged(_mask.CanUndo);
            _tools.NotifyMaskChanged(_mask.CanClear);
        }

        ScheduleMaskExport();
    }

    /// <summary>
    /// Builds the node's <see cref="MaskSpec"/> (null when empty), notifies the App, and
    /// starts an off-thread PNG export of a buffer snapshot. Exports are chained so a stale
    /// frame can never overwrite a newer one on disk (the send pipeline awaits the last).
    /// </summary>
    private void ScheduleMaskExport()
    {
        if (_suppressMaskExport || _mask is null || _maskSessionId is null || _maskNodeId is null)
        {
            return;
        }

        MaskSpec? spec = null;
        if (_mask.HasContent)
        {
            spec = new MaskSpec
            {
                MaskImagePath = MaskExporter.ResolveMaskPath(_maskSessionId, _maskNodeId),
                Width = _mask.Width,
                Height = _mask.Height,
                IsBinary = true,
                Invert = false,
            };
        }

        _nodeMask = spec;
        MaskCompleted?.Invoke(this, new MaskCompletedEventArgs(_maskNodeId, spec));

        if (spec is not null)
        {
            var sessionId = _maskSessionId;
            var nodeId = _maskNodeId;
            var pixels = _mask.CopyPixels();
            var width = _mask.Width;
            var height = _mask.Height;
            _maskWrite = RunMaskExportAsync(_maskWrite, sessionId, nodeId, pixels, width, height);
        }
    }

    private static async Task RunMaskExportAsync(
        Task previous,
        string sessionId,
        string nodeId,
        byte[] pixels,
        int width,
        int height)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failed earlier export must not block the next one.
        }

        try
        {
            await MaskExporter.ExportAsync(sessionId, nodeId, pixels, width, height).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Swallow IO / encode failures so the chained task never faults: the flush
            // callers (send / save / close) await this task, and a missing mask file is
            // handled downstream (no mask sent; a reload drops it with a warning).
        }
    }

    /// <summary>Drops the live mask state and tool flags (empty image / close).</summary>
    private void ResetMask()
    {
        _maskLoadGeneration++;
        _maskPointerDown = false;

        if (_mask is not null)
        {
            _suppressMaskExport = true;
            try
            {
                _mask.SetCanvas(0, 0);
            }
            finally
            {
                _suppressMaskExport = false;
            }

            _tools.NotifyUndoStackChanged(_mask.CanUndo);
            _tools.NotifyMaskChanged(_mask.CanClear);
        }

        _maskOverlay?.MarkDirty();
    }
}
