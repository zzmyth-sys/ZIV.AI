using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9C.2-C swipe-compare state tests (pure logic, no UI / GPU).</summary>
public class CompareStateTests
{
    [Fact]
    public void Initial_State_Is_Off_And_Cannot_Compare()
    {
        var state = new CompareState();

        Assert.False(state.CanCompare);
        Assert.False(state.IsCompareMode);
        Assert.Equal(0.5, state.Divider);
    }

    [Fact]
    public void CanCompare_False_When_No_Parent()
    {
        var state = new CompareState();

        state.SetCanCompare(false);
        Assert.False(state.CanCompare);
    }

    [Fact]
    public void CanCompare_True_When_Parent_Exists()
    {
        var state = new CompareState();

        state.SetCanCompare(true);
        Assert.True(state.CanCompare);
    }

    [Fact]
    public void Enter_Compare_Is_Ignored_Without_Parent()
    {
        var state = new CompareState();

        var active = state.SetCompareMode(true);

        Assert.False(active);
        Assert.False(state.IsCompareMode);
    }

    [Fact]
    public void Enter_And_Exit_Compare_With_Parent()
    {
        var state = new CompareState();
        state.SetCanCompare(true);

        Assert.True(state.SetCompareMode(true));
        Assert.True(state.IsCompareMode);

        Assert.False(state.SetCompareMode(false));
        Assert.False(state.IsCompareMode);
    }

    [Fact]
    public void Toggle_Flips_Compare_Mode_When_Parent_Exists()
    {
        var state = new CompareState();
        state.SetCanCompare(true);

        Assert.True(state.Toggle());
        Assert.True(state.IsCompareMode);

        Assert.False(state.Toggle());
        Assert.False(state.IsCompareMode);
    }

    [Fact]
    public void Toggle_Does_Nothing_Without_Parent()
    {
        var state = new CompareState();

        Assert.False(state.Toggle());
        Assert.False(state.IsCompareMode);
    }

    [Fact]
    public void Losing_Parent_Exits_Compare_Mode()
    {
        var state = new CompareState();
        state.SetCanCompare(true);
        state.SetCompareMode(true);

        state.SetCanCompare(false);

        Assert.False(state.IsCompareMode);
        Assert.False(state.CanCompare);
    }

    [Fact]
    public void Divider_Is_Clamped_To_Unit_Range()
    {
        var state = new CompareState();

        state.SetDivider(-1.0);
        Assert.Equal(0.0, state.Divider);

        state.SetDivider(2.5);
        Assert.Equal(1.0, state.Divider);

        state.SetDivider(0.42);
        Assert.Equal(0.42, state.Divider);
    }

    [Fact]
    public void Exiting_Compare_Resets_Divider()
    {
        var state = new CompareState();
        state.SetCanCompare(true);
        state.SetCompareMode(true);
        state.SetDivider(0.9);

        state.SetCompareMode(false);

        Assert.Equal(0.5, state.Divider);
    }

    [Fact]
    public void Reset_Clears_Everything()
    {
        var state = new CompareState();
        state.SetCanCompare(true);
        state.SetCompareMode(true);
        state.SetDivider(0.8);

        state.Reset();

        Assert.False(state.CanCompare);
        Assert.False(state.IsCompareMode);
        Assert.Equal(0.5, state.Divider);
    }

    [Fact]
    public void Plan_A_Compare_Is_Independent_Of_Tool_Selection()
    {
        // Compare mode must not be a ToolMode and must not be cleared by tool changes.
        var compare = new CompareState();
        var tools = new ToolStateMachine();
        compare.SetCanCompare(true);
        compare.SetCompareMode(true);

        tools.SetTool(ToolMode.Crop);
        tools.SetTool(ToolMode.MaskBrush);

        Assert.True(compare.IsCompareMode);
        Assert.Equal(ToolMode.MaskBrush, tools.CurrentTool);
    }

    [Fact]
    public void StateChanged_Raised_On_Real_Changes_Only()
    {
        var state = new CompareState();
        var count = 0;
        state.StateChanged += (_, _) => count++;

        state.SetCanCompare(true);
        state.SetCompareMode(true);
        state.SetDivider(0.75);
        Assert.Equal(3, count);

        state.SetCanCompare(true);
        state.SetCompareMode(true);
        state.SetDivider(0.75);
        Assert.Equal(3, count);
    }
}