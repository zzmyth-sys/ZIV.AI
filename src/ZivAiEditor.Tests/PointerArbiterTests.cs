using Xunit;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.7-B pointer-arbiter tests: the R1 both-buttons rule (first press wins, each
/// release ends only its own mode). Pure, no UI.
/// </summary>
public class PointerArbiterTests
{
    [Fact]
    public void Starts_Idle()
    {
        var arbiter = new PointerArbiter();

        Assert.Equal(PointerOwner.None, arbiter.Owner);
    }

    [Fact]
    public void Draw_First_Blocks_Pan_Until_Draw_Ends()
    {
        var arbiter = new PointerArbiter();

        Assert.True(arbiter.TryBeginDraw());
        Assert.Equal(PointerOwner.Draw, arbiter.Owner);

        // Middle press while the draw stroke owns the surface is ignored.
        Assert.False(arbiter.TryBeginPan());
        Assert.Equal(PointerOwner.Draw, arbiter.Owner);

        arbiter.EndDraw();
        Assert.Equal(PointerOwner.None, arbiter.Owner);

        Assert.True(arbiter.TryBeginPan());
        Assert.Equal(PointerOwner.Pan, arbiter.Owner);
    }

    [Fact]
    public void Pan_First_Blocks_Draw_Until_Pan_Ends()
    {
        var arbiter = new PointerArbiter();

        Assert.True(arbiter.TryBeginPan());
        Assert.Equal(PointerOwner.Pan, arbiter.Owner);

        // Left press while the pan owns the surface is ignored.
        Assert.False(arbiter.TryBeginDraw());
        Assert.Equal(PointerOwner.Pan, arbiter.Owner);

        arbiter.EndPan();
        Assert.Equal(PointerOwner.None, arbiter.Owner);

        Assert.True(arbiter.TryBeginDraw());
        Assert.Equal(PointerOwner.Draw, arbiter.Owner);
    }

    [Fact]
    public void Each_Release_Only_Ends_Its_Own_Mode()
    {
        var arbiter = new PointerArbiter();

        Assert.True(arbiter.TryBeginDraw());
        // A pan release must not clear a draw owner.
        arbiter.EndPan();
        Assert.Equal(PointerOwner.Draw, arbiter.Owner);

        arbiter.EndDraw();
        Assert.Equal(PointerOwner.None, arbiter.Owner);

        Assert.True(arbiter.TryBeginPan());
        // A draw release must not clear a pan owner.
        arbiter.EndDraw();
        Assert.Equal(PointerOwner.Pan, arbiter.Owner);

        arbiter.EndPan();
        Assert.Equal(PointerOwner.None, arbiter.Owner);
    }

    [Fact]
    public void Ending_When_Idle_Is_NoOp()
    {
        var arbiter = new PointerArbiter();

        arbiter.EndDraw();
        arbiter.EndPan();

        Assert.Equal(PointerOwner.None, arbiter.Owner);
    }
}