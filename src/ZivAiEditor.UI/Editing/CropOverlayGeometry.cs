namespace ZivAiEditor.UI.Editing;

/// <summary>A rectangle in viewport coordinates (no Avalonia dependency).</summary>
public readonly record struct ViewportBand(double X, double Y, double Width, double Height);

/// <summary>
/// Pure viewport-space geometry for the crop overlay (Step 9C.4-B). Split out of the
/// Avalonia <c>CropOverlay</c> control so the gray-fill / darkening band math is
/// unit-testable without a UI thread (Z3 / Z6).
///
/// <para>The overlay paints two disjoint regions: the <b>gray fill</b> for the part of the
/// crop frame that lies outside the source image (the outpaint canvas), and the
/// <b>darkening</b> for everything outside the crop frame. Both are expressed as up to four
/// rectangles so the control can draw them with <c>FillRectangle</c> (no geometry clip).</para>
/// </summary>
public static class CropOverlayGeometry
{
    /// <summary>
    /// The gray bands: the visible crop frame minus the visible image rectangle. When the
    /// frame does not overlap the image at all, the whole visible frame is returned.
    /// </summary>
    public static ViewportBand[] GrayBands(
        double cropX, double cropY, double cropW, double cropH,
        double imageX, double imageY, double imageW, double imageH,
        double viewportW, double viewportH)
    {
        var frame = Intersect(cropX, cropY, cropW, cropH, 0, 0, viewportW, viewportH);
        if (frame is null)
        {
            return Array.Empty<ViewportBand>();
        }

        var f = frame.Value;
        var image = Intersect(imageX, imageY, imageW, imageH, 0, 0, viewportW, viewportH);
        if (image is null)
        {
            return new[] { f };
        }

        var i = image.Value;
        var inter = Intersect(f.X, f.Y, f.Width, f.Height, i.X, i.Y, i.Width, i.Height);
        if (inter is null)
        {
            return new[] { f };
        }

        var m = inter.Value;
        var bands = new[]
        {
            new ViewportBand(f.X, f.Y, f.Width, m.Y - f.Y),                       // above the image
            new ViewportBand(f.X, m.Y + m.Height, f.Width, f.Y + f.Height - (m.Y + m.Height)), // below
            new ViewportBand(f.X, m.Y, m.X - f.X, m.Height),                      // left of the image
            new ViewportBand(m.X + m.Width, m.Y, f.X + f.Width - (m.X + m.Width), m.Height), // right
        };

        return NonEmpty(bands);
    }

    /// <summary>The darkening bands: the viewport minus the visible crop frame.</summary>
    public static ViewportBand[] DarkenBands(
        double cropX, double cropY, double cropW, double cropH,
        double viewportW, double viewportH)
    {
        var frame = Intersect(cropX, cropY, cropW, cropH, 0, 0, viewportW, viewportH);
        if (frame is null)
        {
            return viewportW > 0 && viewportH > 0
                ? new[] { new ViewportBand(0, 0, viewportW, viewportH) }
                : Array.Empty<ViewportBand>();
        }

        var f = frame.Value;
        var bands = new[]
        {
            new ViewportBand(0, 0, viewportW, f.Y),                               // top
            new ViewportBand(0, f.Y + f.Height, viewportW, viewportH - (f.Y + f.Height)), // bottom
            new ViewportBand(0, f.Y, f.X, f.Height),                              // left
            new ViewportBand(f.X + f.Width, f.Y, viewportW - (f.X + f.Width), f.Height), // right
        };

        return NonEmpty(bands);
    }

    private static ViewportBand[] NonEmpty(ViewportBand[] bands)
    {
        var result = new List<ViewportBand>(4);
        foreach (var band in bands)
        {
            if (band.Width > 0 && band.Height > 0)
            {
                result.Add(band);
            }
        }

        return result.ToArray();
    }

    private static ViewportBand? Intersect(
        double ax, double ay, double aw, double ah,
        double bx, double by, double bw, double bh)
    {
        var left = Math.Max(ax, bx);
        var top = Math.Max(ay, by);
        var right = Math.Min(ax + aw, bx + bw);
        var bottom = Math.Min(ay + ah, by + bh);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new ViewportBand(left, top, right - left, bottom - top);
    }
}
