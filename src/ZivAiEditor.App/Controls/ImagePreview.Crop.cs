using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>Carries the node and its new crop after a confirmed crop (Step 9C.6-B).</summary>
public sealed class CropCompletedEventArgs : EventArgs
{
    public CropCompletedEventArgs(string nodeId, CropSpec crop)
    {
        NodeId = nodeId;
        Crop = crop;
    }

    /// <summary>The node whose intrinsic crop changed.</summary>
    public string NodeId { get; }

    /// <summary>The new crop (rectangle + result image).</summary>
    public CropSpec Crop { get; }
}

/// <summary>
/// Crop-tool half of <see cref="ImagePreview"/> (Step 9C.6-B): the <see cref="CropState"/>,
/// the <see cref="CropOverlay"/>, the pointer interaction (move + 8-handle resize), the
/// confirm / cancel actions and the Esc priority. Split out of the main file to keep each
/// file under the Z8 line budget; the two halves share the same partial-class members.
///
/// <para>The crop is the node's intrinsic property: confirming updates the current node's
/// crop (via <see cref="CropCompleted"/>) and never appends a node. The initial rectangle
/// is the node's previous crop when present, otherwise a 75% centered box. Crop coordinates
/// are relative to the node's original image, so crop mode shows that image.</para>
/// </summary>
public partial class ImagePreview
{
    private const double CropHandlePaddingPx = 8.0;

    private CropState? _crop;
    private CropOverlay? _cropOverlay;
    private Border? _cropActions;
    private Border? _cropToast;
    private TextBlock? _cropToastText;
    private Button? _cropConfirm;
    private Button? _cropCancel;

    private bool _cropPointerDown;
    private bool _cropBusy;
    private int _cropToastGeneration;

    /// <summary>The session owning the shown node (its crop temp directory is keyed by it).</summary>
    private string? _sessionId;

    /// <summary>The node currently shown (its crop is edited in crop mode).</summary>
    private string? _nodeId;

    /// <summary>The node's own image (crop coordinates are relative to this).</summary>
    private string? _nodeOriginalPath;

    /// <summary>The node's current crop, if any.</summary>
    private CropSpec? _nodeCrop;

    /// <summary>What the preview shows when compare is off: the crop result, else the original.</summary>
    private string? _displayPath;

    /// <summary>Raised after a crop is written (App stores it on the node).</summary>
    public event EventHandler<CropCompletedEventArgs>? CropCompleted;

    /// <summary>The crop selection state (diagnostics / tests).</summary>
    public CropState? Crop => _crop;

    /// <summary>The image currently displayed (the crop result when one exists), or <c>null</c>.</summary>
    public string? DisplayPath => _displayPath ?? _path;

    /// <summary>Wires the crop overlay / actions. Called from <see cref="Init"/>.</summary>
    private void InitCrop()
    {
        _crop = new CropState();

        _cropOverlay = this.FindControl<CropOverlay>("PART_CropOverlay");
        _cropOverlay?.Attach(_model);
        _cropOverlay?.SetState(_crop);

        _cropActions = this.FindControl<Border>("PART_CropActions");
        _cropToast = this.FindControl<Border>("PART_CropToast");
        _cropToastText = this.FindControl<TextBlock>("PART_CropToastText");
        _cropConfirm = this.FindControl<Button>("PART_BtnCropConfirm");
        _cropCancel = this.FindControl<Button>("PART_BtnCropCancel");

        if (_cropConfirm is not null)
        {
            _cropConfirm.Click += async (_, _) => await ConfirmCropAsync();
        }

        if (_cropCancel is not null)
        {
            _cropCancel.Click += (_, _) => ExitCropMode();
        }

        _tools.StateChanged += (_, _) => UpdateCropMode();
        UpdateCropMode();
    }

    private bool IsCropActive => _crop is { IsActive: true };

    /// <summary>
    /// Opens a node in the preview (Step 9C.6-B): remembers the node so crop edits target
    /// it, and shows the node's crop result when it has one. The node's original image is
    /// kept for cropping and for the compare "right" side.
    /// </summary>
    public void LoadNode(string? sessionId, string? nodeId, string originalPath, CropSpec? crop)
    {
        _sessionId = sessionId;
        _nodeId = nodeId;
        _nodeOriginalPath = originalPath;
        _nodeCrop = crop;
        _displayPath = crop is { ResultImagePath.Length: > 0 } ? crop.ResultImagePath : originalPath;

        // While cropping, show the original (crop coordinates are original-relative);
        // RefreshCropBounds runs after the decode and re-seeds the rectangle.
        if (IsCropActive && !string.IsNullOrWhiteSpace(originalPath))
        {
            LoadImage(originalPath);
        }
        else
        {
            LoadImage(_displayPath);
        }
    }

    /// <summary>
    /// Reacts to the tool state: entering <see cref="ToolMode.Crop"/> (with an image)
    /// shows the overlay on the node's <b>original</b> image (crop coordinates are relative
    /// to it) and seeds the rectangle (previous crop or 75%); leaving it hides the chrome
    /// and restores the displayed image. Compare mode is exited first so the two modes
    /// never overlap.
    /// </summary>
    private void UpdateCropMode()
    {
        if (_crop is null)
        {
            return;
        }

        var wantCrop = _tools.CurrentTool == ToolMode.Crop && _tools.HasImage;

        if (wantCrop && !IsCropActive)
        {
            if (_compareState.IsCompareMode)
            {
                _compareState.SetCompareMode(false);
            }

            _crop.Enter();
            _lastClickAt = DateTime.MinValue;

            // Crop on the node's ORIGINAL image so the rectangle is in original-image
            // coordinates and re-cropping never chains onto a previous crop result.
            if (!string.IsNullOrWhiteSpace(_nodeOriginalPath)
                && !string.Equals(_path, _nodeOriginalPath, StringComparison.OrdinalIgnoreCase))
            {
                // LoadAsync calls RefreshCropBounds once the original is decoded.
                LoadImage(_nodeOriginalPath);
            }
            else
            {
                RefreshCropBounds();
            }

            ShowCropChrome(true);
        }
        else if (!wantCrop && IsCropActive)
        {
            ExitCropMode();
        }

        _cropOverlay?.InvalidateVisual();
    }

    /// <summary>Leaves crop mode (cancel / Esc / after a successful crop).</summary>
    private void ExitCropMode()
    {
        if (_crop is null)
        {
            return;
        }

        _crop.Exit();
        _cropPointerDown = false;
        ShowCropChrome(false);
        _cropOverlay?.InvalidateVisual();

        if (_tools.CurrentTool == ToolMode.Crop)
        {
            _tools.SetTool(ToolMode.None);
        }

        // Show the node's crop result (or its original when uncropped) again.
        if (!string.IsNullOrWhiteSpace(_displayPath)
            && !string.Equals(_path, _displayPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadImage(_displayPath);
        }
    }

    private void ShowCropChrome(bool visible)
    {
        if (_cropOverlay is not null)
        {
            _cropOverlay.IsVisible = visible;
        }

        if (_cropActions is not null)
        {
            _cropActions.IsVisible = visible;
        }

        if (!visible)
        {
            HideCropToast();
        }
    }

    /// <summary>Restores the node's previous crop rectangle, or seeds a 75% box.</summary>
    private void RestoreOrDefaultCrop()
    {
        if (_crop is null)
        {
            return;
        }

        if (_nodeCrop is { Width: > 0, Height: > 0 } stored)
        {
            _crop.SetRect(stored.X, stored.Y, stored.Width, stored.Height);
        }

        if (!_crop.HasRect)
        {
            _crop.SetDefaultRect();
        }
    }

    // --- Pointer routing (called by the main partial's handlers) ---------------

    private void CropOnPressed(Point viewportPoint, PointerPressedEventArgs e)
    {
        if (_crop is null || !_crop.IsActive || _box is null)
        {
            return;
        }

        var (ix, iy) = _model.ViewportToImage(viewportPoint.X, viewportPoint.Y);
        var tolerance = CropHandlePaddingPx / Math.Max(_model.Zoom, 0.0001);
        if (!_crop.BeginDrag(ix, iy, tolerance))
        {
            return;
        }

        _cropPointerDown = true;
        e.Pointer.Capture(_box);
        _cropOverlay?.InvalidateVisual();
    }

    private void CropOnMoved(Point viewportPoint)
    {
        if (_crop is null || !_cropPointerDown)
        {
            return;
        }

        var (ix, iy) = _model.ViewportToImage(viewportPoint.X, viewportPoint.Y);
        _crop.UpdateDrag(ix, iy);
        _cropOverlay?.InvalidateVisual();
    }

    private void CropOnReleased(PointerReleasedEventArgs e)
    {
        if (_crop is null || !_cropPointerDown || _box is null)
        {
            return;
        }

        _cropPointerDown = false;
        e.Pointer.Capture(null);

        var point = e.GetPosition(_box);
        var (ix, iy) = _model.ViewportToImage(point.X, point.Y);
        var tolerance = CropHandlePaddingPx / Math.Max(_model.Zoom, 0.0001);
        var onBox = _crop.HasRect && _crop.HitTest(ix, iy, tolerance) != CropHandle.None;

        _crop.EndDrag();
        _cropOverlay?.InvalidateVisual();

        // Double-click on the crop box confirms the crop (the same gesture that toggles
        // fit / 100% outside crop mode).
        var now = DateTime.UtcNow;
        if (now - _lastClickAt < DoubleClickWindow)
        {
            _lastClickAt = DateTime.MinValue;
            if (onBox)
            {
                _ = ConfirmCropAsync();
            }
        }
        else
        {
            _lastClickAt = now;
        }
    }

    // --- Confirm / cancel ------------------------------------------------------

    private async Task ConfirmCropAsync()
    {
        if (_crop is null || _cropBusy || _sessionId is null || _nodeId is null
            || string.IsNullOrWhiteSpace(_nodeOriginalPath))
        {
            return;
        }

        if (!_crop.TryGetPixelRect(out var x, out var y, out var width, out var height))
        {
            ShowCropToast("请先框选裁切区域");
            return;
        }

        var sessionId = _sessionId;
        var nodeId = _nodeId;
        var source = _nodeOriginalPath;
        _cropBusy = true;
        SetCropActionsEnabled(false);
        SetCropConfirmText("裁切中…");
        try
        {
            // Crop + encode run off the UI thread inside ImageCropper (Z11). The rectangle
            // is in the node's own image coordinates, so the source is the original —
            // never a previous crop. The temp file is overwritten per node.
            var output = await ImageCropper.CropAsync(sessionId, nodeId, source, x, y, width, height);

            if (string.IsNullOrEmpty(output))
            {
                ShowCropToast("裁切失败：无法生成输出文件");
                return;
            }

            var spec = new CropSpec
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                ResultImagePath = output,
            };

            _nodeCrop = spec;
            _displayPath = output;
            ExitCropMode();
            // Force: the crop temp file is overwritten in place, so the path is unchanged
            // but its content is new.
            LoadImage(output, force: true);
            CropCompleted?.Invoke(this, new CropCompletedEventArgs(nodeId, spec));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[crop] {ex.Message}");
            ExitCropMode();
            ShowCropToast($"裁切失败：{ex.Message}");
        }
        finally
        {
            _cropBusy = false;
            SetCropActionsEnabled(true);
            SetCropConfirmText("确认裁切");
        }
    }

    private void SetCropConfirmText(string text)
    {
        if (_cropConfirm is not null)
        {
            _cropConfirm.Content = text;
        }
    }

    private void SetCropActionsEnabled(bool enabled)
    {
        if (_cropConfirm is not null)
        {
            _cropConfirm.IsEnabled = enabled;
        }

        if (_cropCancel is not null)
        {
            _cropCancel.IsEnabled = enabled;
        }
    }

    private void ShowCropToast(string message)
    {
        if (_cropToastText is null || _cropToast is null)
        {
            return;
        }

        _cropToastText.Text = message;
        _cropToast.IsVisible = true;

        // Generation guard: a newer toast invalidates any earlier hide timer.
        var generation = ++_cropToastGeneration;
        _ = HideCropToastAfterDelayAsync(generation);
    }

    private async Task HideCropToastAfterDelayAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (generation == _cropToastGeneration)
        {
            HideCropToast();
        }
    }

    private void HideCropToast()
    {
        // Invalidate any in-flight hide timer so it cannot hide a later toast.
        _cropToastGeneration++;
        if (_cropToast is not null)
        {
            _cropToast.IsVisible = false;
        }
    }

    /// <summary>
    /// Re-seeds the crop rectangle after the preview loads a new image while crop mode is
    /// active (the tool state does not change, so <see cref="UpdateCropMode"/> is not
    /// re-run by the tool-state machine).
    /// </summary>
    private void RefreshCropBounds()
    {
        if (_crop is null || !_crop.IsActive)
        {
            return;
        }

        _crop.SetImageBounds(_model.ImageWidth, _model.ImageHeight);
        RestoreOrDefaultCrop();
        _cropPointerDown = false;
        _cropOverlay?.InvalidateVisual();
    }
}
