namespace ZivAiEditor.Contracts.Models;

/// <summary>
/// Resolution capabilities of one diffusion model. Introduced in Step 6.5 so
/// later models (SDXL / Flux) can be added without changing callers.
///
/// <para><b>Consumer status (Step 9C.3-R #5)</b>: <see cref="NativeSide"/> /
/// <see cref="SafeMaxSide"/> / <see cref="MinSide"/> / <see cref="MultipleOf"/> /
/// <see cref="Presets"/> are capability metadata with <b>no production consumer</b>
/// yet — they are only populated at registration and exercised by tests. They will
/// be consumed when resolution validation / presets are wired in. Only
/// <see cref="TierSides"/> (UI tier picker) and <see cref="MaxPixels"/> are consumed
/// today.</para>
/// </summary>
public sealed class ModelProfile
{
    public string ModelId { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>Native training long edge. Capability metadata — no production consumer yet (Step 9C.3-R #5).</summary>
    public int NativeSide { get; init; }

    /// <summary>Recommended safety ceiling for the long edge (OOM guard). Capability metadata — no production consumer yet (Step 9C.3-R #5).</summary>
    public int SafeMaxSide { get; init; }

    /// <summary>
    /// Safety ceiling for the total pixel count (OOM guard). Independent of
    /// <see cref="SafeMaxSide"/>: the official 16:9 preset is 2752×1536 =
    /// 4,227,072 pixels, which exceeds SafeMaxSide² but stays under this limit.
    /// This is the <b>authoritative</b> source that populates
    /// <c>ResolutionPolicy.MaxPixels</c>.
    /// </summary>
    public int MaxPixels { get; init; } = 4_700_000;

    /// <summary>Minimum allowed long edge. Capability metadata — no production consumer yet (Step 9C.3-R #5).</summary>
    public int MinSide { get; init; }

    /// <summary>Dimensions must be a multiple of this value. Capability metadata — no production consumer yet (Step 9C.3-R #5).</summary>
    public int MultipleOf { get; init; }

    /// <summary>Long edge per <see cref="ResolutionTier"/> (consumed by the UI tier picker).</summary>
    public IReadOnlyDictionary<ResolutionTier, int> TierSides { get; init; }
        = new Dictionary<ResolutionTier, int>();

    /// <summary>Aspect-ratio presets. Capability metadata — no production consumer yet (Step 9C.3-R #5).</summary>
    public IReadOnlyList<AspectPreset> Presets { get; init; } = Array.Empty<AspectPreset>();
}
