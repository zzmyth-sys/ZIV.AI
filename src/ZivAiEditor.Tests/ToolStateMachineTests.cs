using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9C.2 tool state machine tests (pure logic, no UI/GPU).</summary>
public class ToolStateMachineTests
{
    [Fact]
    public void Initial_State_Is_Empty()
    {
        var state = new ToolStateMachine();

        Assert.Equal(ToolMode.None, state.CurrentTool);
        Assert.False(state.HasImage);
        Assert.False(state.CanUndo);
        Assert.False(state.CanClearMask);
        Assert.False(state.CanCrop);
    }

    [Fact]
    public void SetTool_Switches_Current_Tool()
    {
        var state = new ToolStateMachine();

        state.SetTool(ToolMode.Crop);
        Assert.Equal(ToolMode.Crop, state.CurrentTool);

        state.SetTool(ToolMode.MaskBrush);
        Assert.Equal(ToolMode.MaskBrush, state.CurrentTool);
    }

    [Fact]
    public void Brush_And_Eraser_Are_Mutually_Exclusive()
    {
        var state = new ToolStateMachine();

        state.SetTool(ToolMode.MaskBrush);
        Assert.Equal(ToolMode.MaskBrush, state.CurrentTool);

        state.SetTool(ToolMode.Eraser);
        Assert.Equal(ToolMode.Eraser, state.CurrentTool);
        Assert.NotEqual(ToolMode.MaskBrush, state.CurrentTool);
    }

    [Fact]
    public void SetTool_None_Deactivates_All_Tools()
    {
        var state = new ToolStateMachine();

        state.SetTool(ToolMode.Crop);
        state.SetTool(ToolMode.None);

        Assert.Equal(ToolMode.None, state.CurrentTool);
    }

    [Fact]
    public void Only_One_Tool_Is_Active_Across_Switches()
    {
        var state = new ToolStateMachine();

        state.SetTool(ToolMode.Crop);
        state.SetTool(ToolMode.MaskBrush);
        Assert.Equal(ToolMode.MaskBrush, state.CurrentTool);
        Assert.NotEqual(ToolMode.Crop, state.CurrentTool);

        state.SetTool(ToolMode.Eraser);
        Assert.Equal(ToolMode.Eraser, state.CurrentTool);
        Assert.NotEqual(ToolMode.MaskBrush, state.CurrentTool);
    }

    [Fact]
    public void Undo_Enablement_Follows_Notify()
    {
        var state = new ToolStateMachine();

        state.NotifyUndoStackChanged(true);
        Assert.True(state.CanUndo);

        state.NotifyUndoStackChanged(false);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public void ClearMask_Enablement_Follows_Notify()
    {
        var state = new ToolStateMachine();

        state.NotifyMaskChanged(true);
        Assert.True(state.CanClearMask);

        state.NotifyMaskChanged(false);
        Assert.False(state.CanClearMask);
    }

    [Fact]
    public void CanCrop_Follows_HasImage()
    {
        var state = new ToolStateMachine();

        Assert.False(state.CanCrop);

        state.NotifyImageChanged(true);
        Assert.True(state.HasImage);
        Assert.True(state.CanCrop);

        state.NotifyImageChanged(false);
        Assert.False(state.HasImage);
        Assert.False(state.CanCrop);
    }

    [Fact]
    public void StateChanged_Raised_On_Changes()
    {
        var state = new ToolStateMachine();
        var count = 0;
        state.StateChanged += (_, _) => count++;

        state.SetTool(ToolMode.Crop);
        state.NotifyImageChanged(true);
        state.NotifyUndoStackChanged(true);
        state.NotifyMaskChanged(true);
        Assert.Equal(4, count);

        state.SetTool(ToolMode.Crop);
        state.NotifyImageChanged(true);
        state.NotifyUndoStackChanged(true);
        state.NotifyMaskChanged(true);
        Assert.Equal(4, count);
    }
}
