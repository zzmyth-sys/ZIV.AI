using Xunit;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.7 pure mask-editing tests: canvas allocation, brush / eraser stamping,
/// interpolation, undo (bounded), clear, boundary clamping and binary values. No UI / GPU.
/// </summary>
public class MaskStateTests
{
    private static MaskState Canvas(int width, int height)
    {
        var mask = new MaskState();
        mask.SetCanvas(width, height);
        return mask;
    }

    private static byte At(MaskState mask, int x, int y) => mask.CopyPixels()[y * mask.Width + x];

    [Fact]
    public void SetCanvas_Allocates_And_Resets_Undo()
    {
        var mask = Canvas(10, 20);

        Assert.Equal(10, mask.Width);
        Assert.Equal(20, mask.Height);
        Assert.True(mask.HasImage);
        Assert.False(mask.HasContent);
        Assert.False(mask.CanUndo);
        Assert.False(mask.CanClear);

        mask.BeginStroke(5, 5, erase: false);
        mask.EndStroke();
        Assert.True(mask.CanUndo);

        mask.SetCanvas(30, 40);
        Assert.False(mask.CanUndo);
        Assert.False(mask.HasContent);
    }

    [Fact]
    public void BeginStroke_Without_Canvas_Returns_False()
    {
        var mask = new MaskState();

        Assert.False(mask.BeginStroke(1, 1, erase: false));
    }

    [Fact]
    public void Brush_Sets_Pixels_And_Content()
    {
        var mask = Canvas(100, 100);

        mask.BeginStroke(50, 50, erase: false);
        mask.EndStroke();

        Assert.True(mask.HasContent);
        Assert.True(mask.CanClear);
        Assert.True(mask.CanUndo);
        Assert.Equal(MaskState.On, At(mask, 50, 50));
        Assert.Equal(MaskState.Off, At(mask, 0, 0));
    }

    [Fact]
    public void Eraser_Clears_Painted_Pixels()
    {
        var mask = Canvas(60, 60);
        mask.BeginStroke(30, 30, erase: false);
        mask.EndStroke();
        Assert.True(mask.HasContent);

        mask.BeginStroke(30, 30, erase: true);
        mask.EndStroke();

        Assert.False(mask.HasContent);
        Assert.Equal(MaskState.Off, At(mask, 30, 30));
    }

    [Fact]
    public void Interpolation_Fills_The_Segment_With_No_Gaps()
    {
        var mask = Canvas(200, 50);

        mask.BeginStroke(20, 25, erase: false);
        mask.ContinueStroke(180, 25);
        mask.EndStroke();

        // Sample the midpoint of every interpolation step (step = radius / 2 = 10 px).
        for (var x = 20; x <= 180; x += 5)
        {
            Assert.Equal(MaskState.On, At(mask, x, 25));
        }
    }

    [Fact]
    public void Undo_Restores_The_Previous_Buffer()
    {
        var mask = Canvas(60, 60);
        mask.BeginStroke(30, 30, erase: false);
        mask.EndStroke();
        Assert.True(mask.HasContent);

        mask.Undo();

        Assert.False(mask.HasContent);
        Assert.Equal(MaskState.Off, At(mask, 30, 30));
        Assert.False(mask.CanUndo);
    }

    [Fact]
    public void Undo_Stack_Is_Bounded_At_MaxUndo()
    {
        var mask = Canvas(1000, 50);

        // 25 disjoint-ish strokes: each extends the painted region by new pixels.
        for (var i = 0; i < 25; i++)
        {
            mask.BeginStroke(20 + i * 30, 25, erase: false);
            mask.EndStroke();
        }

        var undos = 0;
        while (mask.CanUndo)
        {
            mask.Undo();
            undos++;
        }

        Assert.Equal(MaskState.MaxUndo, undos);
        Assert.Equal(20, undos);
    }

    [Fact]
    public void Clear_Is_Undoable()
    {
        var mask = Canvas(60, 60);
        mask.BeginStroke(30, 30, erase: false);
        mask.EndStroke();
        var before = mask.CopyPixels();

        mask.Clear();
        Assert.False(mask.HasContent);
        Assert.True(mask.CanUndo);

        mask.Undo();
        Assert.True(mask.HasContent);
        Assert.Equal(before, mask.CopyPixels());
    }

    [Fact]
    public void Clear_On_Empty_Mask_Is_NoOp()
    {
        var mask = Canvas(10, 10);

        mask.Clear();

        Assert.False(mask.CanUndo);
        Assert.False(mask.HasContent);
    }

    [Fact]
    public void Boundary_Clamp_Does_Not_Throw_And_Only_Sets_InBounds_Pixels()
    {
        var mask = Canvas(50, 50);

        mask.BeginStroke(-100, -100, erase: false);
        mask.ContinueStroke(200, 200);
        mask.EndStroke();

        // The line y = x crosses the canvas; the center is painted.
        Assert.Equal(MaskState.On, At(mask, 25, 25));

        // A pixel far from the diagonal is untouched.
        Assert.Equal(MaskState.Off, At(mask, 49, 0));

        Assert.All(mask.CopyPixels(), value => Assert.True(value is MaskState.Off or MaskState.On));
    }

    [Fact]
    public void Every_Buffer_Byte_Is_Binary()
    {
        var mask = Canvas(120, 90);
        mask.BeginStroke(10, 10, erase: false);
        mask.ContinueStroke(110, 80);
        mask.BeginStroke(60, 40, erase: true);
        mask.EndStroke();

        Assert.All(mask.CopyPixels(), value => Assert.True(value == 0 || value == 255));
    }

    [Fact]
    public void LoadFrom_Copies_Buffer_And_Recomputes_Content()
    {
        var mask = new MaskState();
        var pixels = new byte[12];
        pixels[0] = 255;
        pixels[7] = 255;

        mask.LoadFrom(pixels, 4, 3);

        Assert.True(mask.HasImage);
        Assert.True(mask.HasContent);
        Assert.Equal(pixels, mask.CopyPixels());
        Assert.False(mask.CanUndo);
    }

    [Fact]
    public void Changed_Is_Raised_On_Mutations()
    {
        var mask = Canvas(40, 40);
        var count = 0;
        mask.Changed += (_, _) => count++;

        mask.BeginStroke(20, 20, erase: false);
        mask.ContinueStroke(25, 25);
        mask.EndStroke();
        mask.Clear();
        mask.Undo();

        Assert.True(count >= 5);
    }

    [Fact]
    public void BrushDiameter_Defaults_And_Clamps()
    {
        var mask = new MaskState();
        Assert.Equal(40, mask.BrushDiameter);

        mask.BrushDiameter = 1;
        Assert.Equal(MaskState.MinBrushDiameter, mask.BrushDiameter);

        mask.BrushDiameter = 9999;
        Assert.Equal(MaskState.MaxBrushDiameter, mask.BrushDiameter);

        mask.BrushDiameter = 77;
        Assert.Equal(77, mask.BrushDiameter);
    }

    [Fact]
    public void Larger_Brush_Paints_A_Wider_Stamp()
    {
        var small = Canvas(120, 40);
        small.BrushDiameter = 10;
        small.BeginStroke(60, 20, erase: false);
        small.EndStroke();

        var large = Canvas(120, 40);
        large.BrushDiameter = 40;
        large.BeginStroke(60, 20, erase: false);
        large.EndStroke();

        // A pixel 15 px from the center is inside the 40 px stamp but outside the 10 px one.
        Assert.Equal(MaskState.Off, At(small, 75, 20));
        Assert.Equal(MaskState.On, At(large, 75, 20));
    }

    [Fact]
    public void FeatherPx_Defaults_And_Clamps()
    {
        var mask = new MaskState();
        Assert.Equal(0, mask.FeatherPx);
        Assert.Equal(15, MaskState.MaxFeatherPx); // E8: 25 -> 15

        mask.FeatherPx = -3;
        Assert.Equal(0, mask.FeatherPx);

        mask.FeatherPx = 100;
        Assert.Equal(MaskState.MaxFeatherPx, mask.FeatherPx);

        mask.FeatherPx = 7;
        Assert.Equal(7, mask.FeatherPx);
    }

    [Fact]
    public void IsStrokeActive_Tracks_The_Stroke_Lifetime()
    {
        var mask = Canvas(40, 40);
        Assert.False(mask.IsStrokeActive);

        mask.BeginStroke(20, 20, erase: false);
        Assert.True(mask.IsStrokeActive);

        mask.EndStroke();
        Assert.False(mask.IsStrokeActive);
    }

    [Fact]
    public void Stroke_Raises_Changed_During_But_Committed_Only_At_End()
    {
        var mask = Canvas(60, 60);
        var changed = 0;
        var committed = 0;
        mask.Changed += (_, _) => changed++;
        mask.Committed += (_, _) => committed++;

        mask.BeginStroke(20, 20, erase: false);
        mask.ContinueStroke(40, 40);

        Assert.True(mask.IsStrokeActive);
        Assert.True(changed >= 2);
        Assert.Equal(0, committed); // R1: nothing durable mid-stroke

        mask.EndStroke();

        Assert.False(mask.IsStrokeActive);
        Assert.Equal(1, committed); // one commit per stroke
    }

    [Fact]
    public void Clear_And_Undo_Raise_Committed()
    {
        var mask = Canvas(40, 40);
        mask.BeginStroke(20, 20, erase: false);
        mask.EndStroke();

        var committed = 0;
        mask.Committed += (_, _) => committed++;

        mask.Clear();
        Assert.Equal(1, committed);

        mask.Undo();
        Assert.Equal(2, committed);
    }

    [Fact]
    public void Dirty_Region_Tracks_Stamps_And_Is_Consumed()
    {
        var mask = Canvas(100, 100);

        // SetCanvas marks the whole canvas dirty; taking consumes it.
        var initial = mask.TakeDirtyRegion();
        Assert.NotNull(initial);
        Assert.Equal(100, initial!.Value.Width);
        Assert.Null(mask.TakeDirtyRegion());

        mask.BeginStroke(50, 50, erase: false);
        var region = mask.TakeDirtyRegion();
        Assert.NotNull(region);
        Assert.True(region!.Value.Width > 0 && region.Value.Height > 0);
        Assert.Null(mask.TakeDirtyRegion());

        // Clear marks the whole canvas dirty again.
        mask.EndStroke();
        mask.Clear();
        var full = mask.TakeDirtyRegion();
        Assert.NotNull(full);
        Assert.Equal(0, full!.Value.X);
        Assert.Equal(0, full.Value.Y);
        Assert.Equal(100, full.Value.Width);
        Assert.Equal(100, full.Value.Height);
    }

    [Fact]
    public void CopyRegion_Clips_And_Returns_Sub_Buffer()
    {
        var mask = Canvas(10, 10);
        mask.BeginStroke(5, 5, erase: false);
        mask.EndStroke();

        Assert.Equal(100, mask.CopyRegion(0, 0, 10, 10).Length);

        var outOfRange = mask.CopyRegion(5, 5, 10, 10);
        Assert.Equal(100, outOfRange.Length);
        Assert.All(outOfRange, value => Assert.Equal(0, value));
    }
}
