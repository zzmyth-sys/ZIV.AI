namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Crop aspect-ratio lock (N5). <see cref="Free"/> is the default (no lock).
/// <see cref="R16x9"/> / <see cref="R9x16"/> / <see cref="R1x1"/> are the offered fixed ratios.
/// </summary>
public enum CropAspectMode
{
    /// <summary>No ratio lock; the rectangle is free-form.</summary>
    Free,

    /// <summary>Locked to 16:9 (landscape).</summary>
    R16x9,

    /// <summary>Locked to 9:16 (portrait).</summary>
    R9x16,

    /// <summary>Locked to 1:1 (square).</summary>
    R1x1,
}
