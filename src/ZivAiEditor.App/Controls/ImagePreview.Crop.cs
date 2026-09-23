using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>Carries the source and output paths of a completed crop (Step 9C.4).</summary>
public sealed class CropCompletedEventArgs : EventArgs
{
    public CropCompletedEventArgs(string sourceImagePath, string outputPath)
    {
        SourceImagePath = sourceImagePath;
        OutputPath = outputPath;
    }

    /// <summary>The image that was cropped (used to resolve the parent session node).</summary>
    public string SourceImagePath { get; }

    /// <summary>The newly written crop file (a new file — Z24).</summary>
    public string OutputPath { get; }
}

/// <summary>
/// Crop-tool half of <see cref="ImagePreview"/> (Step 9C.4): the <see cref="CropState"/>,
/// the <see cref="CropOverlay"/>, the pointer interaction (build / move / resize), the
/// confirm / cancel actions and the Esc priority. Split out of the main file to keep each
/// file under the Z8 line budget; the two halves share the same partial-class members
/// (the image model, the box, the tool state machine, the loaded path).
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

    /// <summary>Raised after a crop is written and loaded (App appends it to the session).</summary>
    public event EventHandler<CropCompletedEventArgs>? CropCompleted;

    /// <summary>The crop selection state (diagnostics / tests).</summary>
    public CropState? Crop => _crop;

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
    /// Reacts to the tool state: entering <see cref="ToolMode.Crop"/> (with an image)
    /// shows the overlay and seeds the full-image rectangle; leaving it hides the chrome.
    /// Compare mode is exited first so the two modes never overlap (裁决 1).
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

            _crop.SetImageBounds(_model.ImageWidth, _model.ImageHeight);
            _crop.Enter();
            _crop.SetFullRect();
            ShowCropChrome(true);
        }
        else if (!wantCrop && IsCropActive)
        {
            _crop.Exit();
            ShowCropChrome(false);
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
        if (_crop is null || !_cropPointerDown)
        {
            return;
        }

        _cropPointerDown = false;
        e.Pointer.Capture(null);
        _crop.EndDrag();
        _cropOverlay?.InvalidateVisual();
    }

    // --- Confirm / cancel ------------------------------------------------------

    private async Task ConfirmCropAsync()
    {
        if (_crop is null || _cropBusy || _path is null)
        {
            return;
        }

        if (!_crop.TryGetPixelRect(out var x, out var y, out var width, out var height))
        {
            ShowCropToast("请先框选裁切区域");
            return;
        }

        var source = _path;
        _cropBusy = true;
        SetCropActionsEnabled(false);
        try
        {
            // Crop + encode off the UI thread (Z11).
            var output = await Task.Run(() => ImageCropper.CropAsync(source, x, y, width, height));

            if (string.IsNullOrEmpty(output))
            {
                ShowCropToast("裁切失败：无法生成输出文件");
                return;
            }

            ExitCropMode();
            LoadImage(output);
            CropCompleted?.Invoke(this, new CropCompletedEventArgs(source, output));
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
    /// Re-seeds the crop rectangle to the full image after the preview loads a new image
    /// while the crop tool is active (the tool state does not change, so
    /// <see cref="UpdateCropMode"/> is not re-run by the tool-state machine).
    /// </summary>
    private void RefreshCropBounds()
    {
        if (_crop is null || !_crop.IsActive)
        {
            return;
        }

        _crop.SetImageBounds(_model.ImageWidth, _model.ImageHeight);
        _crop.SetFullRect();
        _cropPointerDown = false;
        _cropOverlay?.InvalidateVisual();
    }
}
