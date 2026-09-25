namespace ZivAiEditor.Contracts.Session;

/// <summary>
/// The intrinsic crop state of one session node (Step 9C.6-B). A crop is <b>not</b> an
/// edit step: it is a property of the node, at most one per node, re-adjustable, and it
/// never creates a node. The rectangle is in the node image's original pixel coordinates
/// (SPEC §3.9); <see cref="ResultImagePath"/> points at the produced new file (Z24).
/// </summary>
public sealed class CropSpec
{
    /// <summary>Left edge, in source-image pixels.</summary>
    public int X { get; init; }

    /// <summary>Top edge, in source-image pixels.</summary>
    public int Y { get; init; }

    /// <summary>Crop width, in source-image pixels.</summary>
    public int Width { get; init; }

    /// <summary>Crop height, in source-image pixels.</summary>
    public int Height { get; init; }

    /// <summary>
    /// Width of the source image the rectangle is relative to (crop-tool outpaint detection).
    /// <c>0</c> = unknown (a crop persisted before this field existed); see <see cref="IsOutpaint"/>.
    /// </summary>
    public int SourceWidth { get; init; }

    /// <summary>Height of the source image; <c>0</c> = unknown (see <see cref="SourceWidth"/>).</summary>
    public int SourceHeight { get; init; }

    /// <summary>The cropped output file (always a new file — Z24); empty when not produced.</summary>
    public string ResultImagePath { get; init; } = "";

    private const double BoundaryEpsilon = 1e-6;

    /// <summary>
    /// True when the rectangle reaches outside the source image (a crop-tool <b>outpaint</b>),
    /// using the same rule / tolerance as the crop tool (<c>CropState.IsOutpaint</c>).
    /// Conservative when the source size is unknown (<see cref="SourceWidth"/> /
    /// <see cref="SourceHeight"/> = 0): returns <c>false</c> so a legacy crop is never treated
    /// as an outpaint.
    /// </summary>
    public bool IsOutpaint()
    {
        if (SourceWidth <= 0 || SourceHeight <= 0)
        {
            return false;
        }

        return X < -BoundaryEpsilon
               || Y < -BoundaryEpsilon
               || X + (double)Width > SourceWidth + BoundaryEpsilon
               || Y + (double)Height > SourceHeight + BoundaryEpsilon;
    }
}
