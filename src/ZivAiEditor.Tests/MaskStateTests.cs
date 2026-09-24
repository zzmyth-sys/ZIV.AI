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
}
