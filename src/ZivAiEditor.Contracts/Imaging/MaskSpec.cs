namespace ZivAiEditor.Contracts.Imaging;

public sealed class MaskSpec
{
    public string MaskImagePath { get; init; } = "";

    /// <summary>
    /// Mask width in the <b>main image's original pixels</b> (SPEC §3.9): masks are kept
    /// in native source-image coordinates, independent of any UI zoom / pan. Not a
    /// request target, preset nor backend output size.
    /// </summary>
    public int Width { get; init; }

    /// <summary>Mask height in the main image's original pixels (SPEC §3.9).</summary>
    public int Height { get; init; }

    public bool IsBinary { get; init; } = true;
    public bool Invert { get; init; }

    /// <summary>
    /// User-explicit feather radius, in image pixels (Step 9C.7-B). <c>0</c> (default) means a
    /// hard 0 / 255 mask. The exported PNG is grayscale when positive (revised Z19); the
    /// editor never auto-dilates / smart-fills / morphs the mask. Persisted in
    /// <c>session.json</c> as <c>feather_px</c> (append-only; <c>FormatVersion</c> stays 1).
    /// Not an IPC field.
    /// </summary>
    public int FeatherPx { get; init; }
}
