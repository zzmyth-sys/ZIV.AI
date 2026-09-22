namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Pure state for the editor toolbar (Step 9C.2): the active <see cref="ToolMode"/>
/// plus the context flags that drive button enablement. No Avalonia dependency, so it
/// is unit-testable (Z3/Z6). Mask / undo-stack inputs are fed by the App layer; this
/// step only wires stubs (drawing logic arrives in 9C.3 / 9C.4).
/// </summary>
public sealed class ToolStateMachine
{
    /// <summary>Raised whenever any state value changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Active tool; <see cref="ToolMode.None"/> until the user picks one.</summary>
    public ToolMode CurrentTool { get; private set; } = ToolMode.None;

    /// <summary>True once an image is loaded (gates Crop / brush / eraser / reset).</summary>
    public bool HasImage { get; private set; }

    /// <summary>True when there is something to undo.</summary>
    public bool CanUndo { get; private set; }

    /// <summary>True when a mask exists and can be cleared.</summary>
    public bool CanClearMask { get; private set; }

    /// <summary>True when the crop tool can be activated.</summary>
    public bool CanCrop => HasImage;

    /// <summary>Selects the active tool (mutually exclusive by construction).</summary>
    public void SetTool(ToolMode tool)
    {
        if (CurrentTool == tool)
        {
            return;
        }

        CurrentTool = tool;
        RaiseChanged();
    }

    /// <summary>Updates whether an image is loaded.</summary>
    public void NotifyImageChanged(bool hasImage)
    {
        if (HasImage == hasImage)
        {
            return;
        }

        HasImage = hasImage;
        RaiseChanged();
    }

    /// <summary>Updates whether the undo stack is non-empty.</summary>
    public void NotifyUndoStackChanged(bool canUndo)
    {
        if (CanUndo == canUndo)
        {
            return;
        }

        CanUndo = canUndo;
        RaiseChanged();
    }

    /// <summary>Updates whether a mask exists.</summary>
    public void NotifyMaskChanged(bool hasMask)
    {
        if (CanClearMask == hasMask)
        {
            return;
        }

        CanClearMask = hasMask;
        RaiseChanged();
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
