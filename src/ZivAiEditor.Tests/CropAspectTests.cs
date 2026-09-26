using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// N5 crop aspect-lock tests: snapping an existing rectangle keeps its center, uses the
/// shorter edge as the basis, stores the mode without a rectangle, and resets on Exit.
/// Pure logic only — no Avalonia, no UI thread (Z3/Z6/Z29).
/// </summary>
public class CropAspectTests
{
    private const double W = 1000;
    private const double H = 800;

    private static CropState StateWithRect(double x, double y, double w, double h)
    {
        var state = new CropState();
        state.SetImageBounds(W, H);
        state.Enter();
        state.SetRect(x, y, w, h);
        return state;
    }

    [Fact]
    public void SetAspect_16x9_Snaps_Around_Center_With_ShortEdge_Basis()
    {
        var state = StateWithRect(100, 100, 400, 300); // shorter edge = 300

        state.SetAspect(CropAspectMode.R16x9);

        Assert.Equal(300 * (16.0 / 9.0), state.Width, 6);
        Assert.Equal(300, state.Height, 6);
        // Center preserved at (300, 250).
        Assert.Equal(300, state.X + state.Width / 2.0, 6);
        Assert.Equal(250, state.Y + state.Height / 2.0, 6);
    }

    [Fact]
    public void SetAspect_9x16_Snaps_Around_Center_With_ShortEdge_Basis()
    {
        var state = StateWithRect(100, 100, 400, 300); // shorter edge = 300

        state.SetAspect(CropAspectMode.R9x16);

        Assert.Equal(300, state.Width, 6);
        Assert.Equal(300 / (9.0 / 16.0), state.Height, 6); // 533.33
        Assert.Equal(300, state.X + state.Width / 2.0, 6);
        Assert.Equal(250, state.Y + state.Height / 2.0, 6);
    }

    [Fact]
    public void SetAspect_1x1_Snaps_To_Square_Around_Center()
    {
        var state = StateWithRect(100, 100, 400, 300); // shorter edge = 300

        state.SetAspect(CropAspectMode.R1x1);

        Assert.Equal(300, state.Width, 6);
        Assert.Equal(300, state.Height, 6);
        Assert.Equal(300, state.X + state.Width / 2.0, 6);
        Assert.Equal(250, state.Y + state.Height / 2.0, 6);
    }

    [Fact]
    public void SetAspect_Uses_Shorter_Edge_Of_A_Tall_Rect()
    {
        var state = StateWithRect(100, 100, 200, 600); // shorter edge = 200 (width)

        state.SetAspect(CropAspectMode.R16x9);

        Assert.Equal(200 * (16.0 / 9.0), state.Width, 6);
        Assert.Equal(200, state.Height, 6);
    }

    [Fact]
    public void SetAspect_Free_Keeps_The_Rect()
    {
        var state = StateWithRect(100, 100, 400, 300);
        state.SetAspect(CropAspectMode.R16x9);
        var (width, height) = (state.Width, state.Height);

        state.SetAspect(CropAspectMode.Free);

        Assert.Equal(CropAspectMode.Free, state.Aspect);
        Assert.Equal(width, state.Width, 6);
        Assert.Equal(height, state.Height, 6);
    }

    [Fact]
    public void SetAspect_Without_Rect_Only_Stores_The_Mode()
    {
        var state = new CropState();
        state.SetImageBounds(W, H);
        state.Enter();

        state.SetAspect(CropAspectMode.R1x1);

        Assert.Equal(CropAspectMode.R1x1, state.Aspect);
        Assert.False(state.HasRect);
    }

    [Fact]
    public void Exit_Resets_Aspect_To_Free()
    {
        var state = StateWithRect(100, 100, 400, 300);
        state.SetAspect(CropAspectMode.R16x9);
        Assert.Equal(CropAspectMode.R16x9, state.Aspect);

        state.Exit();

        Assert.Equal(CropAspectMode.Free, state.Aspect);
    }
}
