namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Aspect-ratio half of <see cref="CropState"/> (N5). Kept in its own file so
/// <c>CropState.cs</c> stays under the Z8 line budget. The lock is pure geometry: it snaps
/// an existing rectangle around its center and constrains new drags, with no Avalonia
/// dependency (Z3/Z6).
/// </summary>
public sealed partial class CropState
{
    /// <summary>Active aspect lock; <see cref="CropAspectMode.Free"/> until the user picks one.</summary>
    public CropAspectMode Aspect { get; private set; } = CropAspectMode.Free;

    /// <summary>
    /// Sets the aspect lock. Choosing a fixed ratio with an existing rectangle snaps it to
    /// that ratio around its <b>center</b>, using the rectangle's <b>shorter edge</b> as the
    /// basis; the result is re-limited by the outpaint budget. With no rectangle only the
    /// mode is stored, so the next drag builds directly to it. <see cref="Exit"/> resets the
    /// lock to <see cref="CropAspectMode.Free"/>.
    /// </summary>
    public void SetAspect(CropAspectMode mode)
    {
        Aspect = mode;
        if (mode == CropAspectMode.Free || !HasImage || !HasRect)
        {
            return;
        }

        var centerX = _x + _width / 2.0;
        var centerY = _y + _height / 2.0;
        var (w, h) = AspectDimensions(mode, Math.Min(_width, _height));

        var x = centerX - w / 2.0;
        var y = centerY - h / 2.0;
        ClampToLimits(ref x, ref y, ref w, ref h);

        _x = x;
        _y = y;
        _width = w;
        _height = h;
        HasRect = _width >= MinSize && _height >= MinSize;
    }

    /// <summary>W/H ratio of a mode; <c>0</c> for <see cref="CropAspectMode.Free"/>.</summary>
    private static double AspectRatio(CropAspectMode mode) => mode switch
    {
        CropAspectMode.R16x9 => 16.0 / 9.0,
        CropAspectMode.R9x16 => 9.0 / 16.0,
        CropAspectMode.R1x1 => 1.0,
        _ => 0.0,
    };

    /// <summary>
    /// Dimensions for a fixed ratio on a <b>short-edge</b> basis: <paramref name="baseLength"/>
    /// is the shorter edge and the other edge is derived. Landscape ratios widen, portrait
    /// ratios heighten, so the box never exceeds the shorter drag axis.
    /// </summary>
    private static (double W, double H) AspectDimensions(CropAspectMode mode, double baseLength)
    {
        var ratio = AspectRatio(mode);
        if (ratio <= 0)
        {
            return (baseLength, baseLength);
        }

        return ratio >= 1.0
            ? (baseLength * ratio, baseLength)
            : (baseLength, baseLength / ratio);
    }
}
