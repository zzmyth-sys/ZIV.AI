namespace ZivAiEditor.Contracts.Models;

/// <summary>
/// Resolution capabilities of one diffusion model. Introduced in Step 6.5 so
/// later models (SDXL / Flux) can be added without changing callers.
/// </summary>
public sealed class ModelProfile
{
    public string ModelId { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>Native training long edge.</summary>
    public int NativeSide { get; init; }

    /// <summary>Recommended safety ceiling for the long edge (OOM guard).</summary>
    public int SafeMaxSide { get; init; }

    /// <summary>
    /// Safety ceiling for the total pixel count (OOM guard). Independent of
    /// <see cref="SafeMaxSide"/>: the official 16:9 preset is 2752×1536 =
    /// 4,227,072 pixels, which exceeds SafeMaxSide² but stays under this limit.
    /// </summary>
    public int MaxPixels { get; init; } = 4_700_000;

    public int MinSide { get; init; }

    /// <summary>Dimensions must be a multiple of this value.</summary>
    public int MultipleOf { get; init; }

    /// <summary>Long edge per <see cref="ResolutionTier"/>.</summary>
    public IReadOnlyDictionary<ResolutionTier, int> TierSides { get; init; }
        = new Dictionary<ResolutionTier, int>();

    public IReadOnlyList<AspectPreset> Presets { get; init; } = Array.Empty<AspectPreset>();
}
