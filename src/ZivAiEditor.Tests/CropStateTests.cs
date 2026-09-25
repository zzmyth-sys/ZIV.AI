using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.6-B pure crop-state tests: 75% default, restore via SetRect, move + 8-handle
/// resize, clamping and min size.
/// </summary>
public class CropStateTests
{
    private const double W = 1000;
    private const double H = 800;
    private const double Tol = 8;

    private static CropState NewState()
    {
        var state = new CropState();
        state.SetImageBounds(W, H);
        state.Enter();
        return state;
    }

    private static CropState StateWithRect(double x, double y, double w, double h)
    {
        var state = NewState();
        state.SetRect(x, y, w, h);
        return state;
    }

    [Fact]
    public void Enter_Starts_Active_Without_Rect()
    {
        var state = NewState();

        Assert.True(state.IsActive);
        Assert.False(state.HasRect);
        Assert.False(state.IsDragging);
    }

    [Fact]
    public void SetDefaultRect_Is_75_Percent_Centered()
    {
        var state = NewState();

        state.SetDefaultRect();

        Assert.True(state.HasRect);
        Assert.Equal(125, state.X);
        Assert.Equal(100, state.Y);
        Assert.Equal(750, state.Width);
        Assert.Equal(600, state.Height);
    }

    [Fact]
    public void SetRect_Allows_Negative_X()
    {
        var state = NewState();

        state.SetRect(-100, -50, 500, 400);

        Assert.True(state.HasRect);
        Assert.Equal(-100, state.X);
        Assert.Equal(-50, state.Y);
        Assert.Equal(500, state.Width);
        Assert.Equal(400, state.Height);
    }

    [Fact]
    public void SetRect_Clamps_To_MaxMargin()
    {
        var state = NewState();

        // Far beyond the image: limited to 3x each edge (D2 · relaxed).
        state.SetRect(-99999, -99999, 99999, 99999);

        Assert.True(state.HasRect);
        Assert.Equal(-W, state.X);
        Assert.Equal(-H, state.Y);
        Assert.Equal(W * 3, state.Width);
        Assert.Equal(H * 3, state.Height);
    }

    [Fact]
    public void SetRect_Clamps_To_MaxPixels()
    {
        var state = new CropState();
        state.SetImageBounds(4000, 4000);
        state.Enter();

        // 8000x8000 = 64 MP > 36 MP cap -> scaled by 0.75.
        state.SetRect(0, 0, 8000, 8000);

        Assert.Equal(6000, state.Width);
        Assert.Equal(6000, state.Height);
    }

    [Fact]
    public void SetRect_Inner_Crop_Not_Capped_By_Pixels()
    {
        var state = new CropState();
        state.SetImageBounds(6000, 6000); // 36 MP source, larger than the outpaint cap
        state.Enter();

        // Full-image inner crop must keep the source resolution (9C.4-B-P2).
        state.SetRect(0, 0, 6000, 6000);

        Assert.True(state.HasRect);
        Assert.Equal(0, state.X);
        Assert.Equal(0, state.Y);
        Assert.Equal(6000, state.Width);
        Assert.Equal(6000, state.Height);
    }

    [Fact]
    public void SetDefaultRect_Not_Capped_For_Large_Source()
    {
        var state = new CropState();
        state.SetImageBounds(6000, 6000); // 75% = 4500x4500 = 20.25 MP > 16 MP
        state.Enter();

        state.SetDefaultRect();

        Assert.Equal(4500, state.Width);
        Assert.Equal(4500, state.Height);
        Assert.Equal(750, state.X);
        Assert.Equal(750, state.Y);
    }

    [Fact]
    public void SetRect_Below_MinSize_Clears_HasRect()
    {
        var state = NewState();

        state.SetRect(10, 10, 5, 5);

        Assert.False(state.HasRect);
    }

    [Fact]
    public void Move_Drag_Translates_Rect()
    {
        var state = StateWithRect(100, 100, 400, 300);

        state.BeginDrag(300, 250, Tol); // interior → Move
        Assert.Equal(CropHandle.Move, state.DragHandle);
        state.UpdateDrag(360, 300);
        state.EndDrag();

        Assert.Equal(160, state.X);
        Assert.Equal(150, state.Y);
        Assert.Equal(400, state.Width);
        Assert.Equal(300, state.Height);
    }

    [Fact]
    public void Move_Is_Clamped_To_MaxMargin()
    {
        var state = StateWithRect(100, 100, 400, 300);

        state.BeginDrag(300, 250, Tol);
        state.UpdateDrag(9999, 9999);
        state.EndDrag();

        // The rect may sit past the image but must keep a positive overlap with it.
        Assert.Equal(W - 1, state.X);
        Assert.Equal(H - 1, state.Y);
    }

    [Fact]
    public void EndDrag_OverImage_ExpandsCanvas()
    {
        var state = NewState();

        // Start inside the image, drag out to the top-left -> negative origin (outpaint).
        state.BeginDrag(300, 200, Tol);
        state.UpdateDrag(-100, -50);
        state.EndDrag();

        Assert.True(state.HasRect);
        Assert.Equal(-100, state.X);
        Assert.Equal(-50, state.Y);
        Assert.Equal(400, state.Width);
        Assert.Equal(250, state.Height);
    }

    [Fact]
    public void BuildDrag_From_Margin_Across_Image_Keeps_Start()
    {
        var state = NewState();

        // Press in the gray margin (outside the image) and drag across it.
        state.BeginDrag(-80, -40, Tol);
        state.UpdateDrag(120, 60);
        state.EndDrag();

        Assert.Equal(-80, state.X);
        Assert.Equal(-40, state.Y);
        Assert.Equal(200, state.Width);
        Assert.Equal(100, state.Height);
    }

    [Theory]
    [InlineData(CropHandle.TopLeft, 50, 60, 50, 60, 450, 340)]
    [InlineData(CropHandle.Top, 300, 60, 100, 60, 400, 340)]
    [InlineData(CropHandle.TopRight, 550, 60, 100, 60, 450, 340)]
    [InlineData(CropHandle.Right, 550, 250, 100, 100, 450, 300)]
    [InlineData(CropHandle.BottomRight, 550, 450, 100, 100, 450, 350)]
    [InlineData(CropHandle.Bottom, 300, 450, 100, 100, 400, 350)]
    [InlineData(CropHandle.BottomLeft, 50, 450, 50, 100, 450, 350)]
    [InlineData(CropHandle.Left, 50, 250, 50, 100, 450, 300)]
    public void Each_Handle_Resizes_Correctly(
        CropHandle handle, double dragX, double dragY,
        double expectedX, double expectedY, double expectedW, double expectedH)
    {
        var state = StateWithRect(100, 100, 400, 300);
        var (startX, startY) = HandlePoint(handle, 100, 100, 400, 300);

        state.BeginDrag(startX, startY, Tol);
        Assert.Equal(handle, state.DragHandle);
        state.UpdateDrag(dragX, dragY);
        state.EndDrag();

        Assert.Equal(expectedX, state.X);
        Assert.Equal(expectedY, state.Y);
        Assert.Equal(expectedW, state.Width);
        Assert.Equal(expectedH, state.Height);
    }

    [Fact]
    public void Resize_Respects_MinSize()
    {
        var state = StateWithRect(100, 100, 400, 300);

        state.BeginDrag(100, 250, Tol); // Left handle
        state.UpdateDrag(9999, 250);
        state.EndDrag();

        Assert.Equal(CropState.MinSize, state.Width);
    }

    [Theory]
    [InlineData(100, 100, CropHandle.TopLeft)]
    [InlineData(300, 100, CropHandle.Top)]
    [InlineData(500, 100, CropHandle.TopRight)]
    [InlineData(500, 250, CropHandle.Right)]
    [InlineData(500, 400, CropHandle.BottomRight)]
    [InlineData(300, 400, CropHandle.Bottom)]
    [InlineData(100, 400, CropHandle.BottomLeft)]
    [InlineData(100, 250, CropHandle.Left)]
    [InlineData(300, 250, CropHandle.Move)]
    [InlineData(10, 10, CropHandle.None)]
    public void HitTest_Maps_Points_To_Handles(double x, double y, CropHandle expected)
    {
        var state = StateWithRect(100, 100, 400, 300);

        Assert.Equal(expected, state.HitTest(x, y, Tol));
    }

    [Fact]
    public void BuildDrag_Outside_Creates_Normalized_Rect()
    {
        var state = NewState();

        state.BeginDrag(300, 200, Tol); // no rect → build
        state.UpdateDrag(100, 50);
        state.EndDrag();

        Assert.True(state.HasRect);
        Assert.Equal(100, state.X);
        Assert.Equal(50, state.Y);
        Assert.Equal(200, state.Width);
        Assert.Equal(150, state.Height);
    }

    [Fact]
    public void BuildDrag_Below_MinSize_Is_Discarded()
    {
        var state = NewState();

        state.BeginDrag(300, 200, Tol);
        state.UpdateDrag(305, 205);
        state.EndDrag();

        Assert.False(state.HasRect);
    }

    [Fact]
    public void TryGetPixelRect_Returns_Integer_Rect()
    {
        var state = NewState();
        state.SetRect(100.4, 100.6, 400.2, 300.1);

        Assert.True(state.TryGetPixelRect(out var x, out var y, out var w, out var h));
        Assert.Equal(100, x);
        Assert.Equal(101, y);
        Assert.Equal(401, w);
        Assert.Equal(300, h);
    }

    [Fact]
    public void TryGetPixelRect_Without_Rect_Is_False()
    {
        var state = NewState();

        Assert.False(state.TryGetPixelRect(out _, out _, out _, out _));
    }

    [Fact]
    public void Exit_Clears_State()
    {
        var state = StateWithRect(100, 100, 400, 300);

        state.Exit();

        Assert.False(state.IsActive);
        Assert.False(state.HasRect);
        Assert.False(state.IsDragging);
        Assert.Equal(0, state.Width);
        Assert.Equal(0, state.Height);
    }

    [Fact]
    public void SetImageBounds_No_Image_Clears_Rect()
    {
        var state = NewState();
        state.SetDefaultRect();
        Assert.True(state.HasRect);

        state.SetImageBounds(0, 0);

        Assert.False(state.HasImage);
        Assert.False(state.HasRect);
    }

    private static (double X, double Y) HandlePoint(
        CropHandle handle, double x, double y, double w, double h)
    {
        var left = x;
        var top = y;
        var right = x + w;
        var bottom = y + h;
        var midX = (left + right) / 2.0;
        var midY = (top + bottom) / 2.0;

        return handle switch
        {
            CropHandle.TopLeft => (left, top),
            CropHandle.Top => (midX, top),
            CropHandle.TopRight => (right, top),
            CropHandle.Right => (right, midY),
            CropHandle.BottomRight => (right, bottom),
            CropHandle.Bottom => (midX, bottom),
            CropHandle.BottomLeft => (left, bottom),
            CropHandle.Left => (left, midY),
            CropHandle.Move => (midX, midY),
            _ => (midX, midY),
        };
    }
}
