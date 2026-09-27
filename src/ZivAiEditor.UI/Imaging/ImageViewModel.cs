namespace ZivAiEditor.UI.Imaging;

/// <summary>
/// Pure view-state for the image preview (Step 9C.1): the zoom level, the pan offset
/// and the viewport ↔ image coordinate mapping. It carries <b>no</b> Avalonia
/// dependency (Z3 / Z6) so the math is unit-testable without a UI thread; the App
/// layer renders it and feeds it pointer / wheel input.
///
/// Coordinate convention (identical to the renderer's offset model):
/// <c>viewportPoint = imagePoint * Zoom - Offset</c>. Lock vs pan is decided <b>per view,
/// not per axis</b> (B12-follow): only when <b>both</b> scaled axes fit inside the viewport
/// is the image locked and centered (offset 0, effective origin
/// <c>(ViewportSize - ScaledSize) / 2</c>). When <b>either</b> axis overflows, <b>both</b>
/// axes unlock and pan over the extended range <c>[-ViewportSize, ScaledSize]</c> (likewise
/// for Y; the image may be moved fully off the viewport, i.e. "露白" is allowed).
///
/// All dimensions are in device-independent pixels; <see cref="ZoomPercent"/> is an
/// integer percentage (100 = actual size) to match the renderer's integer zoom levels.
/// </summary>
public sealed class ImageViewModel
{
    /// <summary>Lowest zoom (percent) reachable by <b>manual</b> zoom-out (wheel / anchor).</summary>
    public const int MinZoomPercent = 5;

    /// <summary>
    /// Lowest zoom (percent) for a computed <b>fit</b>. Fit is not a manual zoom-out, so a very
    /// large image may fit far below <see cref="MinZoomPercent"/> (e.g. an 8K portrait fits near
    /// 4%); flooring it to the manual limit would overflow the viewport, unlock panning and
    /// left/top-align it. Kept at 1 so the integer-percent zoom can never round down to 0.
    /// </summary>
    public const int MinFitZoomPercent = 1;

    /// <summary>Highest zoom (percent), matching the renderer's default.</summary>
    public const int MaxZoomPercent = 6400;

    /// <summary>Actual-size zoom (percent).</summary>
    public const int ActualSizePercent = 100;

    /// <summary>True until the view has been fitted once the viewport is known.</summary>
    private bool _pendingFit = true;

    /// <summary>
    /// Display image pixel width; 0 when no image is loaded. This is the size of the bitmap
    /// actually drawn (the display proxy, ≤2.5K for a large image) — the single coordinate space the
    /// renderer and every overlay share. Not a request target (<c>ResolutionPolicy.Width</c>), a
    /// preset (<c>AspectPreset.Width</c>) nor a backend output size.
    /// </summary>
    public double ImageWidth { get; private set; }

    /// <summary>Display image pixel height; 0 when no image is loaded (see <see cref="ImageWidth"/>).</summary>
    public double ImageHeight { get; private set; }

    /// <summary>Visible content width in device pixels; 0 before first layout.</summary>
    public double ViewportWidth { get; private set; }

    /// <summary>Visible content height in device pixels; 0 before first layout.</summary>
    public double ViewportHeight { get; private set; }

    /// <summary>Current zoom as an integer percentage (100 = actual size).</summary>
    public int ZoomPercent { get; private set; } = ActualSizePercent;

    /// <summary>Current zoom as a scale factor (1.0 = actual size).</summary>
    public double Zoom => ZoomPercent / 100.0;

    /// <summary>Horizontal scroll offset (device pixels), always within bounds.</summary>
    public double OffsetX { get; private set; }

    /// <summary>Vertical scroll offset (device pixels), always within bounds.</summary>
    public double OffsetY { get; private set; }

    /// <summary>True when a source image with a non-zero size is loaded.</summary>
    public bool HasImage => ImageWidth > 0 && ImageHeight > 0;

    /// <summary>Rendered image width at the current zoom.</summary>
    public double ScaledWidth => ImageWidth * Zoom;

    /// <summary>Rendered image height at the current zoom.</summary>
    public double ScaledHeight => ImageHeight * Zoom;

    /// <summary>True when the scaled image is wider than the viewport (horizontal bars).</summary>
    public bool HasHorizontalScroll => ScaledWidth > ViewportWidth;

    /// <summary>True when the scaled image is taller than the viewport (vertical bars).</summary>
    public bool HasVerticalScroll => ScaledHeight > ViewportHeight;

    /// <summary>
    /// The zoom that makes the whole image visible inside the viewport, preserving the
    /// aspect ratio (the smaller of the two axis ratios). Truncated to an integer
    /// percent and clamped to <c>[MinFitZoomPercent, MaxZoomPercent]</c> — the fit floor is
    /// deliberately <b>not</b> <see cref="MinZoomPercent"/>, so a huge image fits (and centers)
    /// instead of being floored to 10% and overflowing. Returns <see cref="ActualSizePercent"/>
    /// when the image or viewport is not yet known.
    /// </summary>
    public int FitZoomPercent
    {
        get
        {
            if (!HasImage || ViewportWidth <= 0 || ViewportHeight <= 0)
            {
                return ActualSizePercent;
            }

            var scale = Math.Min(ViewportWidth / ImageWidth, ViewportHeight / ImageHeight);
            return ClampFit((int)(scale * 100.0));
        }
    }

    /// <summary>True when the current zoom equals the fit zoom.</summary>
    public bool IsAtFit => ZoomPercent == FitZoomPercent;

    /// <summary>Sets the source image size; the view fits as soon as the viewport is known.</summary>
    public void SetImage(double width, double height)
    {
        ImageWidth = width > 0 ? width : 0;
        ImageHeight = height > 0 ? height : 0;
        _pendingFit = true;
        ZoomPercent = FitZoomPercent;
        OffsetX = 0;
        OffsetY = 0;
    }

    /// <summary>Clears the image and resets the view state.</summary>
    public void ClearImage()
    {
        ImageWidth = 0;
        ImageHeight = 0;
        _pendingFit = true;
        ZoomPercent = ActualSizePercent;
        OffsetX = 0;
        OffsetY = 0;
    }

    /// <summary>
    /// Updates the visible content size. A pending fit (or a currently fitted view) is
    /// re-fitted so the image keeps filling the window across resizes; otherwise the
    /// offset is re-clamped.
    /// </summary>
    public void SetViewport(double width, double height)
    {
        var wasFit = _pendingFit || IsAtFit;
        ViewportWidth = width > 0 ? width : 0;
        ViewportHeight = height > 0 ? height : 0;

        if (wasFit)
        {
            Fit();
        }
        else
        {
            ClampOffset();
        }
    }

    /// <summary>Fits the whole image in the viewport (default view) and centers it.</summary>
    public void Fit()
    {
        ZoomPercent = FitZoomPercent;
        OffsetX = 0;
        OffsetY = 0;
        _pendingFit = false;
    }

    /// <summary>Shows the image at actual size (100%), centered / clamped.</summary>
    public void ActualSize()
    {
        ZoomPercent = ActualSizePercent;
        OffsetX = 0;
        OffsetY = 0;
        _pendingFit = false;
        ClampOffset();
    }

    /// <summary>
    /// Fits the image then scales by <paramref name="factor"/> (e.g. 0.5 to leave room for
    /// outpaint dragging), centered. Used by the crop tool (Step 9C.4-B); clears the pending
    /// fit so a later resize does not snap back to full fit.
    /// </summary>
    public void FitWithMargin(double factor)
    {
        if (factor <= 0)
        {
            factor = 1.0;
        }

        ZoomPercent = ClampFit((int)(FitZoomPercent * factor));
        OffsetX = 0;
        OffsetY = 0;
        _pendingFit = false;
        ClampOffset();
    }

    /// <summary>
    /// Restores a previously saved zoom / offset (Step 9C.4-B). Clears the pending fit and
    /// re-clamps the offset for the current image / viewport.
    /// </summary>
    public void RestoreView(int zoomPercent, double offsetX, double offsetY)
    {
        ZoomPercent = Clamp(zoomPercent);
        OffsetX = offsetX;
        OffsetY = offsetY;
        _pendingFit = false;
        ClampOffset();
    }

    /// <summary>Toggles between the fit view and actual size (double-click action).</summary>
    public void ToggleFitActual()
    {
        if (IsAtFit)
        {
            ActualSize();
        }
        else
        {
            Fit();
        }
    }

    /// <summary>
    /// Sets the zoom so that the image point currently under the viewport position
    /// <paramref name="viewportX"/>, <paramref name="viewportY"/> stays under it.
    /// </summary>
    public void SetZoomAt(int zoomPercent, double viewportX, double viewportY)
    {
        _pendingFit = false;
        if (!HasImage)
        {
            return;
        }

        var target = Clamp(zoomPercent);
        if (target == ZoomPercent)
        {
            return;
        }

        // Image point under the anchor before the zoom.
        var (imageX, imageY) = ViewportToImage(viewportX, viewportY);

        ZoomPercent = target;

        // Preserve the anchor on both axes when the view is unlocked; a locked (both-fit) view
        // is centered, so the anchor cannot be kept there (and need not be).
        if (IsViewLocked)
        {
            OffsetX = 0;
            OffsetY = 0;
        }
        else
        {
            OffsetX = imageX * Zoom - viewportX;
            OffsetY = imageY * Zoom - viewportY;
        }

        ClampOffset();
    }

    /// <summary>Multiplies the current zoom by <paramref name="factor"/>, anchored at a point.</summary>
    public void ZoomBy(double factor, double viewportX, double viewportY)
    {
        if (!HasImage || factor <= 0)
        {
            return;
        }

        SetZoomAt((int)Math.Round(ZoomPercent * factor), viewportX, viewportY);
    }

    /// <summary>
    /// Pans the image by a viewport-space drag delta (the image follows the pointer),
    /// then clamps the offset so no gap is exposed while the axis scrolls.
    /// </summary>
    public void PanBy(double deltaX, double deltaY)
    {
        _pendingFit = false;
        OffsetX -= deltaX;
        OffsetY -= deltaY;
        ClampOffset();
    }

    /// <summary>Converts a viewport position to image pixel coordinates (unclamped).</summary>
    public (double X, double Y) ViewportToImage(double viewportX, double viewportY)
    {
        if (!HasImage)
        {
            return (0, 0);
        }

        return ((viewportX - OriginX) / Zoom, (viewportY - OriginY) / Zoom);
    }

    /// <summary>Converts image pixel coordinates to a viewport position.</summary>
    public (double X, double Y) ImageToViewport(double imageX, double imageY)
    {
        return (imageX * Zoom + OriginX, imageY * Zoom + OriginY);
    }

    /// <summary>
    /// Clamps the pan offset for the current zoom (B12-follow: lock is per view, not per axis).
    /// When <b>either</b> axis overflows, <b>both</b> axes unlock and may pan until the image is
    /// fully off the viewport, the extended range <c>[−ViewportSize, ScaledSize]</c> (panning
    /// past the edge is allowed, i.e. white "露白"). Only when <b>both</b> scaled axes fit is the
    /// image locked: both offsets are forced to 0 (centered).
    /// </summary>
    public void ClampOffset()
    {
        if (IsViewLocked)
        {
            OffsetX = 0;
            OffsetY = 0;
            return;
        }

        OffsetX = Math.Clamp(OffsetX, -ViewportWidth, ScaledWidth);
        OffsetY = Math.Clamp(OffsetY, -ViewportHeight, ScaledHeight);
    }

    /// <summary>
    /// True when <b>neither</b> scaled axis overflows the viewport, so the image is locked and
    /// centered. As soon as <b>either</b> axis overflows, both axes unlock (B12-follow).
    /// </summary>
    private bool IsViewLocked => ScaledWidth <= ViewportWidth && ScaledHeight <= ViewportHeight;

    /// <summary>
    /// Viewport position of the image origin: negative of the scroll offset while the view is
    /// unlocked, otherwise the centering offset.
    /// </summary>
    private double OriginX => IsViewLocked ? (ViewportWidth - ScaledWidth) / 2.0 : -OffsetX;

    private double OriginY => IsViewLocked ? (ViewportHeight - ScaledHeight) / 2.0 : -OffsetY;

    private static int Clamp(int zoomPercent)
        => Math.Clamp(zoomPercent, MinZoomPercent, MaxZoomPercent);

    /// <summary>Clamp for a computed fit / fit-derived zoom (floor <see cref="MinFitZoomPercent"/>).</summary>
    private static int ClampFit(int zoomPercent)
        => Math.Clamp(zoomPercent, MinFitZoomPercent, MaxZoomPercent);
}
