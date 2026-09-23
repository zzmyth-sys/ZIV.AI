namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Pure state for the swipe-compare overlay (Step 9C.2-C): whether a comparison can
/// start (<see cref="CanCompare"/>) and whether it is active
/// (<see cref="IsCompareMode"/>), plus the divider position (<see cref="Divider"/>)
/// clamped to <c>[0, 1]</c>. It carries <b>no</b> Avalonia dependency (Z3 / Z6), so
/// the gating / clamping math is unit-testable without a UI thread.
///
/// <para><b>Mode ownership (Plan A)</b>: comparison is an independent switch, not a
/// <see cref="ToolMode"/>. Enabling it does not clear / consume the active tool and
/// is therefore <b>not</b> mutually exclusive with crop / brush / eraser, and switching
/// a tool does not exit comparison.</para>
///
/// <para><b>Semantics</b>: the "reference" (left) image is the <b>parent node's</b>
/// output — the input this edit consumed. A root node has no parent, so
/// <see cref="CanCompare"/> stays <c>false</c> and the compare button is disabled.</para>
/// </summary>
public sealed class CompareState
{
    /// <summary>Raised whenever any state value changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>True when a reference (parent) image path is available for comparison.</summary>
    public bool CanCompare { get; private set; }

    /// <summary>True while the overlay is shown (the current image with the parent divider).</summary>
    public bool IsCompareMode { get; private set; }

    /// <summary>
    /// Divider position as a fraction of the viewport width: <c>0</c> = fully right
    /// (parent hidden), <c>1</c> = fully left (current hidden). Always within
    /// <c>[0, 1]</c>.
    /// </summary>
    public double Divider { get; private set; } = 0.5;

    /// <summary>
    /// Sets whether a parent (reference) image exists. Losing the reference also
    /// forces compare mode off.
    /// </summary>
    public void SetCanCompare(bool canCompare)
    {
        if (CanCompare == canCompare)
        {
            return;
        }

        CanCompare = canCompare;
        if (!canCompare && IsCompareMode)
        {
            IsCompareMode = false;
        }

        RaiseChanged();
    }

    /// <summary>
    /// Enters or exits compare mode. Entering is ignored (no state change) when
    /// <see cref="CanCompare"/> is <c>false</c>. Exiting resets the divider.
    /// </summary>
    /// <returns><c>true</c> when the resulting <see cref="IsCompareMode"/> is active.</returns>
    public bool SetCompareMode(bool enabled)
    {
        if (enabled && !CanCompare)
        {
            return false;
        }

        if (IsCompareMode == enabled)
        {
            return IsCompareMode;
        }

        IsCompareMode = enabled;
        if (!enabled)
        {
            Divider = 0.5;
        }

        RaiseChanged();
        return IsCompareMode;
    }

    /// <summary>Toggles compare mode; entering is ignored when there is no parent image.</summary>
    /// <returns><c>true</c> when the resulting <see cref="IsCompareMode"/> is active.</returns>
    public bool Toggle() => SetCompareMode(!IsCompareMode);

    /// <summary>
    /// Sets the divider position. The value is round-tripped even when compare mode is
    /// off (so the panel does not drift), but it is always clamped to <c>[0, 1]</c>.
    /// </summary>
    public void SetDivider(double fraction)
    {
        var clamped = Math.Clamp(fraction, 0.0, 1.0);
        if (Math.Abs(clamped - Divider) < double.Epsilon)
        {
            return;
        }

        Divider = clamped;
        RaiseChanged();
    }

    /// <summary>Clears all state (used when the preview image is emptied / reloaded).</summary>
    public void Reset()
    {
        var changed = CanCompare || IsCompareMode || Math.Abs(Divider - 0.5) >= double.Epsilon;
        CanCompare = false;
        IsCompareMode = false;
        Divider = 0.5;

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}