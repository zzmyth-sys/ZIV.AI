using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Self-drawn mask overlay (Step 9C.7): blits a display-only <see cref="WriteableBitmap"/>
/// of the mask buffer where painted pixels become semi-transparent red. Like
/// <see cref="CropOverlay"/> it draws through the shared <see cref="ImageViewModel"/>
/// transform, so the mask stays aligned through pan / zoom. It is
/// <c>IsHitTestVisible = false</c> — <see cref="ImagePreview"/> owns pointer input and just
/// tells this control to repaint. The bitmap is <b>not</b> the drawing engine (D3): it is
/// rebuilt from the buffer only when marked dirty, and disposed on state change / close (Z9).
///
/// <para><b>Feather (Step 9C.7-B)</b> is display-only: the live <see cref="MaskState"/> buffer
/// stays hard 0 / 255, and <see cref="IImagingService.FeatherMask"/> — the same pure function
/// the exporter uses — blooms it into the alpha of the overlay bitmap. The cached bitmap is
/// rebuilt when the mask is marked dirty (which the tool half also does on a feather change).</para>
///
/// <para><b>Brush circle</b>: the current brush / eraser is outlined at the pointer position
/// (following the cursor) with a radius of <c>brushDiameter * Zoom / 2</c>. The outline uses a
/// two-tone pen (dark halo + light core) so it stays visible on light and dark pixels, and it
/// draws even when the mask is empty.</para>
/// </summary>
public partial class MaskOverlay : UserControl
{
    private static readonly IPen HaloPen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x00, 0x00)), 3);
    private static readonly IPen CorePen = new Pen(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), 1);

    /// <summary>Long-side cap for the display-only overlay bitmap (same cap as the display proxy).</summary>
    private const int MaxOverlaySide = ZivAiEditor.App.Imaging.IDisplayImageLoader.MaxDisplaySide;

    private ImageViewModel? _model;
    private MaskState? _state;

    /// <summary>
    /// Imaging port for the display-only feather (module-boundary migration step 4). Set by
    /// <see cref="ImagePreview"/> before the overlay is shown; <c>null</c> falls back to the
    /// hard 0 / 255 buffer.
    /// </summary>
    public IImagingService? Imaging { get; set; }
    private WriteableBitmap? _bitmap;
    private bool _dirty = true;

    private int _brushDiameter = 40;
    private double _pointerX;
    private double _pointerY;
    private bool _hasPointer;

    public MaskOverlay()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        DetachedFromVisualTree += (_, _) => DisposeBitmap();
    }

    /// <summary>Shared zoom / pan model; must be the same instance as the image box.</summary>
    public void Attach(ImageViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        InvalidateVisual();
    }

    /// <summary>The mask state to render. Changing the state drops the cached bitmap.</summary>
    public void SetState(MaskState state)
    {
        if (!ReferenceEquals(_state, state))
        {
            _state = state;
            DisposeBitmap();
            _dirty = true;
        }

        InvalidateVisual();
    }

    /// <summary>Sets the brush / eraser diameter (image pixels) for the cursor outline.</summary>
    public void SetBrush(int diameterPx)
    {
        _brushDiameter = diameterPx;
        InvalidateVisual();
    }

    /// <summary>Sets the pointer position (viewport-space) so the brush circle follows the cursor.</summary>
    public void SetPointer(double viewportX, double viewportY)
    {
        _pointerX = viewportX;
        _pointerY = viewportY;
        _hasPointer = true;
        InvalidateVisual();
    }

    /// <summary>Clears the pointer so no brush circle is drawn (pointer left the surface).</summary>
    public void ClearPointer()
    {
        _hasPointer = false;
        InvalidateVisual();
    }

    /// <summary>Marks the cached bitmap stale so the next render rebuilds it (full + feather).</summary>
    public void MarkDirty()
    {
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Patches only the touched rectangle of the cached bitmap (R1: in-stroke incremental
    /// repaint). The bitmap is hard (un-feathered) while a stroke is active; the committed
    /// <see cref="MarkDirty"/> pass re-applies feather. A no-op when there is no target yet.
    /// </summary>
    public void PatchRegion(MaskState state, int x, int y, int width, int height)
    {
        if (state is null)
        {
            return;
        }

        // An oversize mask is stored downsampled, so a region patch would need matching scaled
        // coordinates. Rather than pay that cost on every in-stroke move, fall back to a full
        // (downsampled) rebuild; strokes on >2.5K masks are rare and stay correct.
        if (state.Width > MaxOverlaySide || state.Height > MaxOverlaySide)
        {
            _dirty = true;
            InvalidateVisual();
            return;
        }

        if (!EnsureHardBitmap(state))
        {
            return;
        }

        var values = state.CopyRegion(x, y, width, height);
        MaskOverlayBitmap.WriteRegion(_bitmap!, values, x, y, width, height);
        InvalidateVisual();
    }

    /// <summary>
    /// Ensures a hard (un-feathered) bitmap of the current state exists and is not pending a
    /// full rebuild. Builds one on first paint / size change / stale flag. Never applies feather
    /// (that is the committed pass) so a stroke stays cheap.
    /// </summary>
    private bool EnsureHardBitmap(MaskState state)
    {
        if (_bitmap is not null && !_dirty && MatchesDisplaySize(state))
        {
            return true;
        }

        DisposeBitmap();
        _bitmap = MaskOverlayBitmap.Build(state.CopyPixels(), state.Width, state.Height);
        _dirty = false;
        return _bitmap is not null;
    }

    /// <summary>Expected downsampled overlay size for the current mask (≤ <see cref="MaxOverlaySide"/>).</summary>
    private static (int Width, int Height) DisplaySize(MaskState state)
    {
        if (state.Width <= 0 || state.Height <= 0)
        {
            return (0, 0);
        }

        if (state.Width <= MaxOverlaySide && state.Height <= MaxOverlaySide)
        {
            return (state.Width, state.Height);
        }

        var scale = (double)MaxOverlaySide / Math.Max(state.Width, state.Height);
        return (Math.Max(1, (int)Math.Round(state.Width * scale)),
                Math.Max(1, (int)Math.Round(state.Height * scale)));
    }

    private bool MatchesDisplaySize(MaskState state)
    {
        var (width, height) = DisplaySize(state);
        return _bitmap is not null
            && _bitmap.PixelSize.Width == width
            && _bitmap.PixelSize.Height == height;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var model = _model;
        var state = _state;
        if (model is null || state is null || !model.HasImage || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        RenderMask(context, model, state);
        RenderBrushCircle(context, model);
    }

    private void RenderMask(DrawingContext context, ImageViewModel model, MaskState state)
    {
        if (!state.HasImage || !state.HasContent)
        {
            return;
        }

        var (left, top) = model.ImageToViewport(0, 0);
        var (right, bottom) = model.ImageToViewport(state.Width, state.Height);
        var dest = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        if (dest.Width <= 0 || dest.Height <= 0)
        {
            return;
        }

        if (_dirty || _bitmap is null || !MatchesDisplaySize(state))
        {
            Rebuild(state);
        }

        if (_bitmap is not null)
        {
            context.DrawImage(_bitmap, dest);
        }
    }

    /// <summary>Outlines the current brush / eraser at the pointer (radius = diameter * Zoom / 2).</summary>
    private void RenderBrushCircle(DrawingContext context, ImageViewModel model)
    {
        if (!_hasPointer || _brushDiameter <= 0)
        {
            return;
        }

        var radius = _brushDiameter * model.Zoom / 2.0;
        if (radius <= 0.5)
        {
            return;
        }

        var center = new Point(_pointerX, _pointerY);
        // Dark halo first, light core second: visible on both light and dark pixels.
        context.DrawEllipse(null, HaloPen, center, radius, radius);
        context.DrawEllipse(null, CorePen, center, radius, radius);
    }

    private void Rebuild(MaskState state)
    {
        DisposeBitmap();

        var pixels = state.CopyPixels();
        var width = state.Width;
        var height = state.Height;
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            _dirty = false;
            return;
        }

        // Display-only feather: the live buffer stays hard; the same pure function the
        // exporter uses blooms it into the alpha ramp (byte-identical on both surfaces).
        // The red BGRA composition is shared with the chat bubble (MaskOverlayBitmap).
        // The overlay bitmap's long side is capped so an 8K mask never becomes an 8K texture;
        // it is stretched back over the mask's original rectangle by RenderMask.
        var display = Imaging?.FeatherMask(pixels, width, height, state.FeatherPx) ?? pixels;
        _bitmap = MaskOverlayBitmap.BuildScaled(display, width, height, MaxOverlaySide, out _, out _);
        _dirty = false;
    }

    private void DisposeBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}