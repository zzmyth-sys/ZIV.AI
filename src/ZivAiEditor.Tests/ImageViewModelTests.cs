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
    public void Fit_Tall_8K_Image_Is_Not_Floored_To_MinZoom_And_Centers()
    {
        // 8K portrait left-align regression: the fit (~4%) must NOT be floored to MinZoomPercent,
        // which would overflow the viewport and break the fit-to-view centering.
        var vm = new ImageViewModel();
        vm.SetViewport(1000, 700);
        vm.SetImage(8192, 16384); // fit = 700 / 16384 ≈ 4.3% -> 4%

        Assert.Equal(4, vm.ZoomPercent);
        Assert.True(vm.ZoomPercent < ImageViewModel.MinZoomPercent);
        Assert.True(vm.IsAtFit);

        // Fitting image: the origin rests centered (offset 0), so origin X and Y are positive.
        var (x, y) = vm.ImageToViewport(0, 0);
        Assert.Equal((1000 - 8192 * 0.04) / 2.0, x, 3);
        Assert.Equal((700 - 16384 * 0.04) / 2.0, y, 3);
        Assert.True(x > 0 && y > 0);
    }

    [Fact]
    public void Manual_Zoom_Out_Still_Clamps_To_MinZoomPercent()
    {
        // The fit floor split must not loosen the manual zoom-out floor: a fit below the manual
        // limit is not itself limited, but a manual zoom-out below it clamps up to the limit.
        var vm = new ImageViewModel();
        vm.SetViewport(1000, 700);
        vm.SetImage(8192, 16384);
        vm.Fit(); // ~4%, below the manual floor
        Assert.True(vm.ZoomPercent < ImageViewModel.MinZoomPercent);

        vm.SetZoomAt(1, 500, 350);

        Assert.Equal(ImageViewModel.MinZoomPercent, vm.ZoomPercent);
    }

    [Fact]
    public void Manual_Zoom_Stops_At_Five_Percent()
    {
        // Manual zoom-out floors at 5% and stays there; 5% itself is a valid, quiescent state.
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(1000, 1000);
        vm.ActualSize(); // 100%

        vm.SetZoomAt(4, 200, 200);
        Assert.Equal(5, vm.ZoomPercent);

        // A further wheel-out from 5% must not go below 5%.
        vm.ZoomBy(0.5, 200, 200);
        Assert.Equal(5, vm.ZoomPercent);

        // And zooming back in from 5% behaves normally.
        vm.SetZoomAt(6, 200, 200);
        Assert.Equal(6, vm.ZoomPercent);
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
    public void ZoomAt_Keeps_The_Anchor_On_A_Single_Axis_Overflow()
    {
        // Free pan: a single overflowing axis does not lock the other; the anchor is kept on both.
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(800, 600); // fit 50% -> 400x300, both visible
        vm.Fit();

        // 60% -> 480x360: only the horizontal axis overflows.
        const double cursorX = 300;
        const double cursorY = 100;
        var beforeX = vm.ViewportToImage(cursorX, cursorY).X;
        var beforeY = vm.ViewportToImage(cursorX, cursorY).Y;
        vm.SetZoomAt(60, cursorX, cursorY);

        Assert.True(vm.HasHorizontalScroll);
        Assert.False(vm.HasVerticalScroll);
        // The anchor is kept on both axes (the fitted axis is not forced to zero).
        Assert.Equal(beforeX, vm.ViewportToImage(cursorX, cursorY).X, 6);
        Assert.Equal(beforeY, vm.ViewportToImage(cursorX, cursorY).Y, 6);
        Assert.NotEqual(0, vm.OffsetY);
    }

    [Fact]
    public void Pan_Clamps_To_KeepVisible_Bounds()
    {
        // Free pan (ZIV parity): viewport 400, scaled 1000 -> |offset| <= (400 + 0.9*1000)/2 = 650,
        // leaving a 5% (50 px) sliver at the extreme.
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(1000, 1000);
        vm.ActualSize(); // 100% -> scaled 1000x1000, centered at offset 0

        Assert.Equal(0, vm.OffsetX);
        Assert.Equal(0, vm.OffsetY);

        vm.PanBy(10_000, 10_000); // drag far right/down -> clamp at -maxPan
        Assert.Equal(-650, vm.OffsetX, 6);
        Assert.Equal(-650, vm.OffsetY, 6);

        vm.PanBy(-10_000, -10_000); // drag far left/up -> clamp at +maxPan
        Assert.Equal(650, vm.OffsetX, 6);
        Assert.Equal(650, vm.OffsetY, 6);

        vm.PanBy(-100, -100); // already at max, stays
        Assert.Equal(650, vm.OffsetX, 6);
        Assert.Equal(650, vm.OffsetY, 6);

        vm.PanBy(100, 100);
        Assert.Equal(550, vm.OffsetX, 6);
        Assert.Equal(550, vm.OffsetY, 6);
    }

    [Fact]
    public void Pan_Clamp_Is_Symmetric_Around_Center()
    {
        // Free pan: the clamp is symmetric (+/- maxPan), not the old asymmetric [-v, s] range.
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(1000, 1000);
        vm.ActualSize();

        vm.PanBy(1000, 1000); // -1000 requested -> clamp at -650
        Assert.Equal(-650, vm.OffsetX, 6);
        Assert.Equal(-650, vm.OffsetY, 6);

        vm.PanBy(-2000, -2000); // back past center -> +650 (not the old asymmetric 600)
        Assert.Equal(650, vm.OffsetX, 6);
        Assert.Equal(650, vm.OffsetY, 6);
    }

    [Fact]
    public void Fitting_Image_Can_Be_Panned_And_Stays_Where_Released()
    {
        // Regression: a fully fitting image used to be locked (offset forced to 0) and could not
        // be dragged. Free pan allows it, and there is no snap-back: the offset is retained.
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(100, 100); // fit 400% -> 400x400, exactly filling the viewport

        vm.PanBy(120, -80);

        Assert.Equal(-120, vm.OffsetX, 6);
        Assert.Equal(80, vm.OffsetY, 6);
    }

    [Fact]
    public void Pan_Survives_A_Same_Size_Viewport_Update()
    {
        // A wheel tick re-asserts the (unchanged) viewport size; once the user has panned a fitted
        // image the view must NOT re-fit — otherwise the pan snaps back to center.
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(100, 100); // fit 400%
        vm.PanBy(120, -80);

        vm.SetViewport(400, 400); // same size (e.g. OnWheel re-assert)

        Assert.Equal(-120, vm.OffsetX, 6);
        Assert.Equal(80, vm.OffsetY, 6);
    }

    [Fact]
    public void Pan_Survives_A_Viewport_Resize()
    {
        // A resize of a manually panned view only re-clamps; it must not recenter (no snap-back).
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(100, 100); // fit 400%
        vm.PanBy(120, -80);

        vm.SetViewport(500, 400);

        Assert.Equal(-120, vm.OffsetX, 6); // still within the new bound
        Assert.Equal(80, vm.OffsetY, 6);
    }

    [Fact]
    public void Pan_Clamp_Is_Per_Axis_With_Per_Axis_Bounds()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(2000, 500);
        vm.ActualSize(); // 100% -> 2000x500; maxX=(400+1800)/2=1100, maxY=(400+450)/2=425

        vm.PanBy(10_000, 10_000); // drag right/down -> minimum offsets
        Assert.Equal(-1100, vm.OffsetX, 6);
        Assert.Equal(-425, vm.OffsetY, 6);

        vm.PanBy(-10_000, -10_000); // drag left/up -> maximum offsets
        Assert.Equal(1100, vm.OffsetX, 6);
        Assert.Equal(425, vm.OffsetY, 6);
    }

    [Fact]
    public void Single_Axis_Overflow_Pans_Both_Axes_Freely()
    {
        // 2000x500 image / 800x600 viewport. At 50% -> 1000x250 only X overflows, yet both axes
        // pan freely with their own keep-visible bounds: maxX=(800+900)/2=850, maxY=(600+225)/2=412.5.
        var vm = new ImageViewModel();
        vm.SetViewport(800, 600);
        vm.SetImage(2000, 500);
        vm.SetZoomAt(50, 0, 0); // 1000x250: X overflows, Y fits

        Assert.True(vm.HasHorizontalScroll);
        Assert.False(vm.HasVerticalScroll);

        vm.PanBy(10_000, 10_000);
        Assert.Equal(-850, vm.OffsetX, 6);
        Assert.Equal(-412.5, vm.OffsetY, 6); // the fitted axis pans too

        vm.PanBy(-10_000, -10_000);
        Assert.Equal(850, vm.OffsetX, 6);
        Assert.Equal(412.5, vm.OffsetY, 6);
    }

    [Fact]
    public void Both_Axes_Fit_Still_Pans_Up_To_The_Sliver()
    {
        // Free pan: a both-fit image is no longer locked; it pans until only 5% stays visible.
        var vm = new ImageViewModel();
        vm.SetViewport(800, 600);
        vm.SetImage(400, 300); // fit 200% -> 800x600: both fit exactly

        Assert.False(vm.HasHorizontalScroll);
        Assert.False(vm.HasVerticalScroll);

        vm.PanBy(10_000, 10_000); // maxX=(800+720)/2=760, maxY=(600+540)/2=570
        Assert.Equal(-760, vm.OffsetX, 6);
        Assert.Equal(-570, vm.OffsetY, 6);
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
    public void Pan_Reaches_KeepVisible_Endpoints_With_Five_Percent_Sliver()
    {
        // Free pan: the extreme endpoints leave a 5% sliver visible (5% * 1000 = 50 px), never 0%.
        var vm = new ImageViewModel();
        vm.SetViewport(400, 300);
        vm.SetImage(1000, 1000);
        vm.ActualSize(); // scaled 1000x1000; maxX=(400+900)/2=650, maxY=(300+900)/2=600

        vm.PanBy(10_000, 10_000); // image pushed right / down
        Assert.Equal(-650, vm.OffsetX, 6);
        Assert.Equal(-600, vm.OffsetY, 6);

        vm.PanBy(-10_000, -10_000); // image pushed left / up
        Assert.Equal(650, vm.OffsetX, 6);
        Assert.Equal(600, vm.OffsetY, 6);
    }

    [Fact]
    public void MaxPanOffset_Leaves_Five_Percent_Visible()
    {
        // The keep-visible bound leaves exactly KeepVisibleRatio of the scaled axis on screen.
        const double viewport = 400;
        const double scaled = 1000;
        Assert.Equal((viewport + 0.9 * scaled) / 2.0, ImageViewModel.MaxPanOffset(viewport, scaled), 6);

        var vm = new ImageViewModel();
        vm.SetViewport(400, 400);
        vm.SetImage(1000, 1000);
        vm.ActualSize();
        vm.PanBy(double.MaxValue / 2, 0); // pin to the extreme

        var (originX, _) = vm.ImageToViewport(0, 0);
        var visible = Math.Min(originX + scaled, viewport) - Math.Max(originX, 0);
        Assert.Equal(ImageViewModel.KeepVisibleRatio * scaled, visible, 6);
    }

    [Fact]
    public void RestoreView_Clamps_Offset_To_Bounds()
    {
        var vm = new ImageViewModel();
        vm.SetViewport(400, 300);
        vm.SetImage(1000, 1000);

        vm.RestoreView(100, 99999, -99999);

        // Keep-visible bounds: X: [-650, 650]; Y: [-600, 600].
        Assert.Equal(650, vm.OffsetX, 6);
        Assert.Equal(-600, vm.OffsetY, 6);
    }
}
