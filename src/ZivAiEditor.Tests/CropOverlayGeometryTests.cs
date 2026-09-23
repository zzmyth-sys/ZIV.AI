using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.4-B pure crop-overlay geometry tests: the grey outpaint bands (crop frame minus
/// the image) and the darkening bands (viewport minus the crop frame). No Avalonia / GPU.
/// </summary>
public class CropOverlayGeometryTests
{
    [Fact]
    public void GrayBands_Cover_Frame_Minus_Image()
    {
        // Viewport 100x100, image at (10,10,50,50), crop frame (0,0,70,70).
        var bands = CropOverlayGeometry.GrayBands(
            0, 0, 70, 70,
            10, 10, 50, 50,
            100, 100);

        Assert.Equal(4, bands.Length);
        Assert.Contains(new ViewportBand(0, 0, 70, 10), bands);   // top
        Assert.Contains(new ViewportBand(0, 60, 70, 10), bands);  // bottom
        Assert.Contains(new ViewportBand(0, 10, 10, 50), bands);  // left
        Assert.Contains(new ViewportBand(60, 10, 10, 50), bands); // right

        var area = bands.Sum(b => b.Width * b.Height);
        Assert.Equal(70 * 70 - 50 * 50, area); // frame area minus image overlap
    }

    [Fact]
    public void GrayBands_No_Image_Overlap_Returns_Whole_Frame()
    {
        // Crop frame visible but not touching the image -> all grey.
        var bands = CropOverlayGeometry.GrayBands(
            60, 60, 30, 30,
            10, 10, 20, 20,
            100, 100);

        Assert.Single(bands);
        Assert.Equal(new ViewportBand(60, 60, 30, 30), bands[0]);
    }

    [Fact]
    public void GrayBands_Frame_Off_Viewport_Is_Empty()
    {
        var bands = CropOverlayGeometry.GrayBands(
            200, 200, 30, 30,
            10, 10, 20, 20,
            100, 100);

        Assert.Empty(bands);
    }

    [Fact]
    public void GrayBands_Inner_Crop_Is_Empty()
    {
        // Crop frame entirely inside the image -> no outpaint area.
        var bands = CropOverlayGeometry.GrayBands(
            20, 20, 30, 30,
            10, 10, 50, 50,
            100, 100);

        Assert.Empty(bands);
    }

    [Fact]
    public void DarkenBands_Cover_Viewport_Minus_Frame()
    {
        var bands = CropOverlayGeometry.DarkenBands(20, 20, 40, 40, 100, 100);

        Assert.Equal(4, bands.Length);
        var area = bands.Sum(b => b.Width * b.Height);
        Assert.Equal(100 * 100 - 40 * 40, area);
    }

    [Fact]
    public void DarkenBands_Frame_Covers_Viewport_Is_Empty()
    {
        var bands = CropOverlayGeometry.DarkenBands(-10, -10, 200, 200, 100, 100);

        Assert.Empty(bands);
    }

    [Fact]
    public void DarkenBands_Frame_Off_Viewport_Darkens_All()
    {
        var bands = CropOverlayGeometry.DarkenBands(200, 200, 30, 30, 100, 100);

        Assert.Single(bands);
        Assert.Equal(new ViewportBand(0, 0, 100, 100), bands[0]);
    }
}
