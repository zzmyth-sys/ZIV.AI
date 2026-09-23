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
    private bool _draggingDivider;

    /// <summary>The preview's compare state (diagnostics / tests).</summary>
    public CompareState CompareState => _compareState;

    /// <summary>The reference (parent) image path, or <c>null</c> when none (diagnostics).</summary>
    public string? ParentPath => _parentPath;

    /// <summary>
    /// Sets (or clears) the <b>parent</b> image used as the swipe-compare reference
    /// (Step 9C.2-C). Kept separate from <see cref="LoadImage"/> because the parent
    /// path comes from the current node's <c>ParentNodeId</c> rather than the clicked
    /// image, so the call sites and lifetimes differ. Decoded off the UI thread (Z11)
    /// and disposed on replace / close (Z9). A root node passes <c>null</c>, which
    /// disables comparison.
    /// </summary>
    public void SetCompareSource(string? parentPath)
    {
        // Skip only when nothing would change: same path AND either the parent is already
        // decoded, the path is empty, or the file is still missing. A same-path call where
        // the file has since appeared (and was not decoded) retries the load.
        var samePath = string.Equals(parentPath, _parentPath, StringComparison.OrdinalIgnoreCase);
        var missing = string.IsNullOrWhiteSpace(parentPath) || !System.IO.File.Exists(parentPath);
        if (samePath && (_parentBitmap is not null || missing))
        {
            return;
        }

        _parentPath = parentPath;
        var generation = ++_parentGeneration;
        DisposeParentBitmap();

        if (string.IsNullOrWhiteSpace(parentPath) || !System.IO.File.Exists(parentPath))
        {
            _compareState.SetCanCompare(false);
            UpdateCompareButton();
            return;
        }

        _ = LoadParentAsync(parentPath, generation);
    }

    private async Task LoadParentAsync(string path, int generation)
    {
        Bitmap? bitmap = null;
        var failed = false;

        await Task.Run(() =>
        {
            try
            {
                bitmap = new Bitmap(path);
            }
            catch (Exception ex)
            {
                failed = true;
                System.Diagnostics.Debug.WriteLine($"[compare] {ex.Message}");
            }
        });

        if (generation != _parentGeneration)
        {
            bitmap?.Dispose();
            return;
        }

        if (failed || bitmap is null)
        {
            _compareState.SetCanCompare(false);
            UpdateCompareButton();
            return;
        }

        _parentBitmap = bitmap;
        _overlay?.SetParent(bitmap);
        if (_compareInfoText is not null)
        {
            _compareInfoText.Text = $"原图 {bitmap.Size.Width} × {bitmap.Size.Height}";
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
    }
}