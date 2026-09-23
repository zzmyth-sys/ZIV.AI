using ZivAiEditor.UI.Imaging;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.1 image-preview view-model tests: fit scaling (aspect ratio), zoom
/// anchoring (the image point under the cursor stays put) and pan bounds. Pure
/// logic only — no Avalonia, no UI thread, no GPU (Z29).
/// </summary>
public class ImageViewModelTests
{
    [Fact]
    public void Fit_Uses_Smaller_Axis_Ratio_And_Preserves_Aspect()
    {
        var landscape = new ImageViewModel();
        landscape.SetViewport(500, 500);
        landscape.SetImage(2000, 1000); // ratios 0.25 / 0.5 -> 25%

        Assert.Equal(25, landscape.ZoomPercent);
        Assert.Equal(500, landscape.ScaledWidth, 6);
        Assert.Equal(250, landscape.ScaledHeight, 6);
        Assert.True(landscape.IsAtFit);

        var portrait = new ImageViewModel();
        portrait.SetViewport(400, 400);
        portrait.SetImage(100, 200); // ratios 4 / 2 -> 200%

        Assert.Equal(200, portrait.ZoomPercent);
        Assert.Equal(200, portrait.ScaledWidth, 6);
        Assert.Equal(400, portrait.ScaledHeight, 6);
        Assert.True(portrait.IsAtFit);
    }

    [Fact]
    public void Fit_Recomputes_Once_The_Viewport_Is_Known()
    {
        var vm = new ImageViewModel();

        // Image set before first layout: no viewport yet, so the fit is pending.
        vm.SetImage(2000, 1000);
        Assert.Equal(ImageViewModel.ActualSizePercent, vm.ZoomPercent);

        vm.SetViewport(500, 500);
        Assert.Equal(25, vm.ZoomPercent);
        Assert.True(vm.IsAtFit);
    }

    [Fact]
    public void Fit_Stays_Applied_Across_Viewport_Resize()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(500, 500);
        vm.SetImage(2000, 1000);
        Assert.Equal(25, vm.ZoomPercent);

        vm.SetViewport(1000, 500); // now width-limited at 50%
        Assert.Equal(50, vm.ZoomPercent);
        Assert.True(vm.IsAtFit);
    }

    [Fact]
    public void ZoomAt_Keeps_The_Image_Point_Under_The_Cursor()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 300);
        vm.SetImage(1000, 500);
        vm.Fit(); // 40%

        const double cursorX = 200;
        const double cursorY = 150;
        var before = vm.ViewportToImage(cursorX, cursorY);

        vm.SetZoomAt(200, cursorX, cursorY);

        var after = vm.ViewportToImage(cursorX, cursorY);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(200, vm.ZoomPercent);
    }

    [Fact]
    public void ZoomBy_Preserves_The_Anchor_When_Zooming_In()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 300);
        vm.SetImage(800, 600); // fit 50% -> 400x300 exactly, nothing scrolls
        vm.Fit();
        Assert.Equal(50, vm.ZoomPercent);

        // Zoom in: both axes now overflow, anchor must be preserved.
        const double cursorX = 120;
        const double cursorY = 90;
        var before = vm.ViewportToImage(cursorX, cursorY);
        vm.ZoomBy(2.0, cursorX, cursorY);

        var after = vm.ViewportToImage(cursorX, cursorY);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void ZoomAt_Preserves_The_Scrolling_Axis_And_Centers_The_Other()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(800, 600); // fit 50% -> 400x300, both visible
        vm.Fit();

        // 60% -> 480x360: only the horizontal axis overflows.
        const double cursorX = 300;
        const double cursorY = 100;
        var beforeX = vm.ViewportToImage(cursorX, cursorY).X;
        vm.SetZoomAt(60, cursorX, cursorY);

        Assert.True(vm.HasHorizontalScroll);
        Assert.False(vm.HasVerticalScroll);
        Assert.Equal(beforeX, vm.ViewportToImage(cursorX, cursorY).X, 6);
        // The fully visible axis is centered, so its offset stays at zero.
        Assert.Equal(0, vm.OffsetY);
    }

    [Fact]
    public void Pan_Is_Clamped_To_The_Image_Bounds()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(1000, 1000);
        vm.ActualSize(); // 100% -> scaled 1000x1000, max offset 600

        Assert.Equal(0, vm.OffsetX);
        Assert.Equal(0, vm.OffsetY);

        vm.PanBy(10_000, 10_000); // drag far right/down -> clamp to 0
        Assert.Equal(0, vm.OffsetX);
        Assert.Equal(0, vm.OffsetY);

        vm.PanBy(-10_000, -10_000); // drag far left/up -> clamp to max
        Assert.Equal(600, vm.OffsetX);
        Assert.Equal(600, vm.OffsetY);

        vm.PanBy(-100, -100); // already at max, stays
        Assert.Equal(600, vm.OffsetX);
        Assert.Equal(600, vm.OffsetY);

        vm.PanBy(100, 100);
        Assert.Equal(500, vm.OffsetX);
        Assert.Equal(500, vm.OffsetY);
    }

    [Fact]
    public void Pan_Does_Nothing_When_The_Image_Fits()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(100, 100); // fit 400% -> 400x400, fully visible

        vm.PanBy(120, -80);

        Assert.Equal(0, vm.OffsetX);
        Assert.Equal(0, vm.OffsetY);
    }

    [Fact]
    public void ToggleFitActual_Switches_Between_Fit_And_100()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(500, 500);
        vm.SetImage(2000, 1000); // fit 25%

        vm.ToggleFitActual();
        Assert.Equal(ImageViewModel.ActualSizePercent, vm.ZoomPercent);

        vm.ToggleFitActual();
        Assert.Equal(25, vm.ZoomPercent);
        Assert.True(vm.IsAtFit);
    }

    [Fact]
    public void Viewport_Image_Mapping_RoundTrips()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 300);
        vm.SetImage(1000, 500);
        vm.SetZoomAt(150, 100, 80);

        var (imageX, imageY) = vm.ViewportToImage(123, 77);
        var (viewportX, viewportY) = vm.ImageToViewport(imageX, imageY);

        Assert.Equal(123, viewportX, 6);
        Assert.Equal(77, viewportY, 6);
    }

    [Fact]
    public void Zoom_Is_Clamped_To_Min_And_Max()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(1000, 1000);

        vm.SetZoomAt(100_000, 0, 0);
        Assert.Equal(ImageViewModel.MaxZoomPercent, vm.ZoomPercent);

        vm.SetZoomAt(1, 0, 0);
        Assert.Equal(ImageViewModel.MinZoomPercent, vm.ZoomPercent);
    }

    [Fact]
    public void FitWithMargin_Scales_Fit_And_Centers()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(500, 500);
        vm.SetImage(1000, 1000); // fit = 50%

        vm.FitWithMargin(0.65);

        Assert.Equal(32, vm.ZoomPercent); // truncate(50 * 0.65) = 32
        Assert.False(vm.IsAtFit);
        // Smaller than the viewport -> centered, so offset is zero.
        Assert.Equal(0, vm.OffsetX, 6);
        Assert.Equal(0, vm.OffsetY, 6);
    }

    [Fact]
    public void FitWithMargin_Clears_Pending_Fit_Across_Resize()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(500, 500);
        vm.SetImage(1000, 1000);
        vm.FitWithMargin(0.65);

        vm.SetViewport(1000, 1000); // must not snap back to fit

        Assert.Equal(32, vm.ZoomPercent);
    }

    [Fact]
    public void RestoreView_Restores_Zoom_And_Offset()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 300);
        vm.SetImage(1000, 1000);
        vm.SetZoomAt(150, 100, 80);

        var (zoom, ox, oy) = (vm.ZoomPercent, vm.OffsetX, vm.OffsetY);
        vm.FitWithMargin(0.65);
        vm.RestoreView(zoom, ox, oy);

        Assert.Equal(zoom, vm.ZoomPercent);
        Assert.Equal(ox, vm.OffsetX, 6);
        Assert.Equal(oy, vm.OffsetY, 6);
    }

    [Fact]
    public void RestoreView_Clamps_Offset_To_Bounds()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 300);
        vm.SetImage(1000, 1000);

        vm.RestoreView(100, 99999, -99999);

        Assert.Equal(1000 - 400, vm.OffsetX, 6);
        Assert.Equal(0, vm.OffsetY, 6);
    }
}
