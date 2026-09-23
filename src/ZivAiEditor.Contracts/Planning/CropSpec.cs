namespace ZivAiEditor.Contracts.Planning;

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

    /// <summary>The cropped output file (always a new file — Z24); empty when not produced.</summary>
    public string ResultImagePath { get; init; } = "";
}
