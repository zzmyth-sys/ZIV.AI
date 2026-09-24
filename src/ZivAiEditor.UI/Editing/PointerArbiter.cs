namespace ZivAiEditor.UI.Editing;

/// <summary>Which pointer mode currently owns the input surface.</summary>
public enum PointerOwner
{
    /// <summary>No button is down / no mode is active.</summary>
    None,

    /// <summary>A draw stroke (left button on the mask) owns the pointer.</summary>
    Draw,

    /// <summary>A middle-button pan owns the pointer.</summary>
    Pan,
}

/// <summary>
/// Tiny pure arbiter for the R1 both-buttons rule (Step 9C.7-B): at most one pointer mode
/// owns the surface at a time. The first button to go down wins; while it is held the other
/// button is ignored. Each release only ends its own mode. No Avalonia dependency (Z3/Z6),
/// so the interaction rule is unit-testable.
/// </summary>
public sealed class PointerArbiter
{
    /// <summary>The mode that currently owns the pointer (<see cref="PointerOwner.None"/> when idle).</summary>
    public PointerOwner Owner { get; private set; } = PointerOwner.None;

    /// <summary>
    /// Claims the surface for a draw stroke. Returns <c>false</c> (and leaves the current
    /// owner untouched) when something else already owns it.
    /// </summary>
    public bool TryBeginDraw()
    {
        if (Owner != PointerOwner.None)
        {
            return false;
        }

        Owner = PointerOwner.Draw;
        return true;
    }

    /// <summary>
    /// Claims the surface for a middle-button pan. Returns <c>false</c> (and leaves the
    /// current owner untouched) when something else already owns it.
    /// </summary>
    public bool TryBeginPan()
    {
        if (Owner != PointerOwner.None)
        {
            return false;
        }

        Owner = PointerOwner.Pan;
        return true;
    }

    /// <summary>Ends a draw stroke; only a draw owner is cleared.</summary>
    public void EndDraw()
    {
        if (Owner == PointerOwner.Draw)
        {
            Owner = PointerOwner.None;
        }
    }

    /// <summary>Ends a pan; only a pan owner is cleared.</summary>
    public void EndPan()
    {
        if (Owner == PointerOwner.Pan)
        {
            Owner = PointerOwner.None;
        }
    }
}