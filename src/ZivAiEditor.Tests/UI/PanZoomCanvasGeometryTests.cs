using Avalonia;
using Xunit;
using ZivAiEditor.App.Controls;

namespace ZivAiEditor.Tests;

/// <summary>
/// Pure renderer-math tests (8K fix / DPI): the draw rect is computed from the <b>source</b> pixel
/// size × zoom (not the proxy bitmap's own size), so a downscaled proxy stretches over the original
/// coordinates and a both-fit view is centered. No GPU, no UI thread (Z29).
/// </summary>
public sealed class PanZoomCanvasGeometryTests
{
    [Fact]
    public void Both_Fit_Uses_Source_Size_And_Centers()
    {
        // A tall 8K image at 5%: the drawn rect is source-sized and centered, never left-aligned.
        var rect = PanZoomCanvas.ComputeDrawRect(
            new Size(8192, 12288), zoomPercent: 5, viewport: new Size(1000, 700), offset: default);

        Assert.Equal(8192 * 0.05, rect.Width, 3);
        Assert.Equal(12288 * 0.05, rect.Height, 3);
        Assert.Equal((1000 - 8192 * 0.05) / 2.0, rect.X, 3);
        Assert.Equal((700 - 12288 * 0.05) / 2.0, rect.Y, 3);
    }

    [Fact]
    public void Overflow_Unlocks_Both_Axes_At_Negative_Offset()
    {
        var rect = PanZoomCanvas.ComputeDrawRect(
            new Size(1000, 1000), zoomPercent: 100, viewport: new Size(400, 400), offset: new Vector(10, 20));

        Assert.Equal(-10, rect.X, 3);
        Assert.Equal(-20, rect.Y, 3);
        Assert.Equal(1000, rect.Width, 3);
        Assert.Equal(1000, rect.Height, 3);
    }

    [Fact]
    public void Single_Axis_Overflow_Unlocks_Both()
    {
        // Source 2000x500 at 50% -> 1000x250: only X overflows, yet both draw at -offset.
        var rect = PanZoomCanvas.ComputeDrawRect(
            new Size(2000, 500), zoomPercent: 50, viewport: new Size(800, 600), offset: new Vector(30, 40));

        Assert.Equal(-30, rect.X, 3);
        Assert.Equal(-40, rect.Y, 3);
    }

    [Fact]
    public void Proxy_Size_Does_Not_Drive_The_Draw_Rect()
    {
        // The bitmap may be a 2560-wide proxy, but the rect is built from the 4000-wide source.
        var rect = PanZoomCanvas.ComputeDrawRect(
            new Size(4000, 1000), zoomPercent: 25, viewport: new Size(2000, 2000), offset: default);

        Assert.Equal(1000, rect.Width, 3); // 4000 * 0.25, not 2560 * 0.25
        Assert.Equal(250, rect.Height, 3);
    }
}
