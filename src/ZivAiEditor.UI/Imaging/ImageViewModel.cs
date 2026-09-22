namespace ZivAiEditor.UI.Imaging;

/// <summary>
/// Pure view-state for the image preview (Step 9C.1): the zoom level, the pan offset
/// and the viewport ↔ image coordinate mapping. It carries <b>no</b> Avalonia
/// dependency (Z3 / Z6) so the math is unit-testable without a UI thread; the App
/// layer renders it and feeds it pointer / wheel input.
///
/// Coordinate convention (identical to the renderer's offset model):
/// <c>viewportPoint = imagePoint * Zoom - Offset</c>, where <c>Offset</c> is the
/// scroll offset and is clamped to <c>[0, ScaledSize - ViewportSize]</c>. When the
/// scaled image is smaller than the viewport on an axis it is <b>centered</b> on that
/// axis (the renderer does the same), so the effective origin becomes
/// <c>(ViewportSize - ScaledSize) / 2</c> instead of <c>-Offset</c>.
///
/// All dimensions are in device-independent pixels; <see cref="ZoomPercent"/> is an
/// integer percentage (100 = actual size) to match the renderer's integer zoom levels.
/// </summary>
public sealed class ImageViewModel
{
    /// <summary>Lowest zoom (percent), matching the renderer's default.</summary>
    public const int MinZoomPercent = 10;

    /// <summary>Highest zoom (percent), matching the renderer's default.</summary>
    public const int MaxZoomPercent = 6400;

    /// <summary>Actual-size zoom (percent).</summary>
    public const int ActualSizePercent = 100;

    /// <summary>True until the view has been fitted once the viewport is known.</summary>
    private bool _pendingFit = true;

    /// <summary>Source image width in pixels; 0 when no image is loaded.</summary>
    public double ImageWidth { get; private set; }

    /// <summary>Source image height in pixels; 0 when no image is loaded.</summary>
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
    /// percent and clamped to <c>[MinZoomPercent, MaxZoomPercent]</c>. Returns
    /// <see cref="ActualSizePercent"/> when the image or viewport is not yet known.
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
            return Clamp((int)(scale * 100.0));
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

        // Preserve the anchor on each axis that now scrolls; a fully visible axis is
        // centered, so the anchor cannot be kept there (and need not be).
        OffsetX = HasHorizontalScroll ? imageX * Zoom - viewportX : 0;
        OffsetY = HasVerticalScroll ? imageY * Zoom - viewportY : 0;
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

    /// <summary>Clamps the pan offset to the legal range for the current zoom.</summary>
    public void ClampOffset()
    {
        OffsetX = Math.Clamp(OffsetX, 0, Math.Max(0, ScaledWidth - ViewportWidth));
        OffsetY = Math.Clamp(OffsetY, 0, Math.Max(0, ScaledHeight - ViewportHeight));
    }

    /// <summary>
    /// Viewport position of the image origin: negative of the scroll offset while the
    /// axis scrolls, otherwise the centering offset.
    /// </summary>
    private double OriginX => HasHorizontalScroll ? -OffsetX : (ViewportWidth - ScaledWidth) / 2.0;

    private double OriginY => HasVerticalScroll ? -OffsetY : (ViewportHeight - ScaledHeight) / 2.0;

    private static int Clamp(int zoomPercent)
        => Math.Clamp(zoomPercent, MinZoomPercent, MaxZoomPercent);
}
