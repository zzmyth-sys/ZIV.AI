using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Swipe-compare half of <see cref="ImagePreview"/> (Step 9C.2-C): the parent
/// (reference) bitmap, the <see cref="CompareState"/> and the divider interaction.
/// Split out of the main file to keep each file under the Z8 line budget; the two
/// halves share the same partial-class members (fields, the overlay, the image model).
/// </summary>
public partial class ImagePreview
{
    private const double DividerHitPadding = 8.0;

    private readonly CompareState _compareState = new();

    private CompareOverlay? _overlay;
    private ToggleButton? _compare;
    private Border? _compareInfo;
    private TextBlock? _compareInfoText;
    private Bitmap? _parentBitmap;
    private string? _parentPath;
    private int _parentGeneration;
    private bool _parentLoadRequested;
    private bool _draggingDivider;

    /// <summary>Last observed compare mode, so the box image only swaps on a transition.</summary>
    private bool _lastCompareMode;

    /// <summary>The preview's compare state (diagnostics / tests).</summary>
    public CompareState CompareState => _compareState;

    /// <summary>The reference (parent) image path, or <c>null</c> when none (diagnostics).</summary>
    public string? ParentPath => _parentPath;

    /// <summary>
    /// Sets (or clears) the <b>parent</b> image used as the swipe-compare reference
    /// (Step 9C.2-C). Kept separate from <see cref="LoadImage"/> because the parent
    /// path comes from the current node's <c>ParentNodeId</c> rather than the clicked
    /// image, so the call sites and lifetimes differ. A root node passes <c>null</c>, which
    /// disables comparison.
    ///
    /// <para><b>Lazy decode</b> (Step 9C.6-B perf): the bitmap is <b>not</b> decoded here —
    /// only the path is recorded, so opening a preview never pays for a reference image the
    /// user may not compare. It is decoded on the first compare-enter
    /// (<see cref="EnsureParentLoaded"/>) and disposed on path change / close (Z9).</para>
    /// </summary>
    public void SetCompareSource(string? parentPath)
    {
        if (string.Equals(parentPath, _parentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _parentPath = parentPath;
        _parentGeneration++;
        DisposeParentBitmap();

        var missing = string.IsNullOrWhiteSpace(parentPath) || !System.IO.File.Exists(parentPath);
        _compareState.SetCanCompare(!missing);
        UpdateCompareButton();
    }

    /// <summary>Decodes the parent bitmap on demand (first compare-enter). No-op if done.</summary>
    private void EnsureParentLoaded()
    {
        if (_parentBitmap is not null || _parentLoadRequested)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_parentPath) || !System.IO.File.Exists(_parentPath))
        {
            return;
        }

        _parentLoadRequested = true;
        _ = LoadParentAsync(_parentPath, ++_parentGeneration);
    }

    private async Task LoadParentAsync(string path, int generation)
    {
        // Same 8K-safe display path as the main image: a proxy bitmap plus the original size.
        var display = await _displayLoader.LoadDisplayAsync(path);

        if (generation != _parentGeneration)
        {
            display?.Bitmap.Dispose();
            return;
        }

        _parentLoadRequested = false;

        if (display is null)
        {
            _compareState.SetCanCompare(false);
            UpdateCompareButton();
            return;
        }

        _parentBitmap = display.Bitmap;
        _overlay?.SetParent(display.Bitmap);
        if (_compareInfoText is not null)
        {
            _compareInfoText.Text = $"原图 {display.OriginalPixelSize.Width} × {display.OriginalPixelSize.Height}";
        }

        _compareState.SetCanCompare(true);
        UpdateCompareButton();
    }

    /// <summary>Compare button: toggles the swipe overlay (ignored when there is no parent).</summary>
    private void OnCompareClick(object? sender, RoutedEventArgs e)
    {
        _compareState.Toggle();
    }

    /// <summary>Pushes compare state onto the overlay + button, and refreshes the cursor.</summary>
    private void OnCompareStateChanged()
    {
        // Step 9C.6-B: crop and compare are mutually exclusive, and the compare "right"
        // side is the current node's ORIGINAL image (the left/parent side is its pipeline
        // image). The box image is swapped only on an actual mode transition, so divider
        // drags (which also raise StateChanged) do not re-trigger a decode.
        var mode = _compareState.IsCompareMode;
        if (mode != _lastCompareMode)
        {
            _lastCompareMode = mode;
            if (mode)
            {
                if (IsCropActive)
                {
                    ExitCropMode();
                }

                EnsureParentLoaded();

                if (!string.IsNullOrWhiteSpace(_nodeOriginalPath))
                {
                    LoadImage(_nodeOriginalPath);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_displayPath))
            {
                LoadImage(_displayPath);
            }

            // B3: every compare-mode toggle re-fits the view (divider drags also raise
            // StateChanged, so this is gated on the mode transition).
            if (_model.HasImage)
            {
                _model.Fit();
                ApplyModel();
            }
        }

        if (_overlay is not null)
        {
            _overlay.Divider = _compareState.Divider;
            _overlay.IsVisible = _compareState.IsCompareMode;
            _overlay.InvalidateVisual();
        }

        if (_compareInfo is not null)
        {
            _compareInfo.IsVisible = _compareState.IsCompareMode && _parentBitmap is not null;
        }

        UpdateCompareButton();
        UpdateCursor();
    }

    /// <summary>Enables the compare button only when a parent image exists.</summary>
    private void UpdateCompareButton()
    {
        if (_compare is null)
        {
            return;
        }

        _compare.IsEnabled = _compareState.CanCompare;

        // Keep the toggle visually in sync without re-entering Click (state-driven, like the toolbar).
        _compare.IsChecked = _compareState.IsCompareMode;
    }

    /// <summary>True when a viewport X coordinate is within the divider's grab band.</summary>
    private bool IsNearDivider(double viewportX)
    {
        if (_overlay is null || _overlay.Bounds.Width <= 0)
        {
            return false;
        }

        var dividerX = Math.Clamp(_compareState.Divider, 0.0, 1.0) * _overlay.Bounds.Width;
        return Math.Abs(viewportX - dividerX) <= DividerHitPadding;
    }

    private void SetDividerFromViewport(double viewportX)
    {
        if (_overlay is null || _overlay.Bounds.Width <= 0)
        {
            return;
        }

        _compareState.SetDivider(viewportX / _overlay.Bounds.Width);
    }

    private void DisposeParentBitmap()
    {
        _overlay?.SetParent(null);
        _parentBitmap?.Dispose();
        _parentBitmap = null;
        _parentLoadRequested = false;
    }
}