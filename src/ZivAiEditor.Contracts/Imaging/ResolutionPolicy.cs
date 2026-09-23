namespace ZivAiEditor.Contracts.Imaging;

/// <summary>
/// User-selected output resolution, translated by the C# layer into absolute
/// numbers and sent over IPC (the Python backend is stateless — Z23). A
/// <see langword="null"/> policy means "use the backend's configured default".
/// </summary>
public sealed class ResolutionPolicy
{
    public ResolutionMode Mode { get; init; } = ResolutionMode.Side;

    /// <summary>Mode=<see cref="ResolutionMode.Side"/>: target longest edge.</summary>
    public int? Side { get; init; }

    /// <summary>Mode=<see cref="ResolutionMode.Area"/>: target total pixels.</summary>
    public int? Area { get; init; }

    /// <summary>Mode=<see cref="ResolutionMode.Scale"/>: multiple of the input's long edge.</summary>
    public float? Scale { get; init; }

    /// <summary>
    /// Mode=<see cref="ResolutionMode.Explicit"/>: explicit target width <b>requested</b>
    /// by the user / command. This is an input, not the produced size — the actual
    /// output dimensions are reported by the backend (see <c>InferenceResultDetail.Width</c>).
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Mode=<see cref="ResolutionMode.Explicit"/>: explicit target height <b>requested</b>
    /// by the user / command. Input, not output (see <c>InferenceResultDetail.Height</c>).
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Safety ceiling for the total pixel count (OOM guard). It is independent of
    /// the long-edge limit, so the official 16:9 preset (2752×1536 = 4,227,072)
    /// is not clamped. Default 4,700,000 (Step 7 MaxPixels fix).
    ///
    /// <para><b>Provenance (Step 9C.3-R #5)</b>: the authoritative value is
    /// <c>ModelProfile.MaxPixels</c>; callers populate this from the active profile.
    /// The default here exists only for compatibility when no profile is supplied.</para>
    /// </summary>
    public int MaxPixels { get; init; } = 4_700_000;
}
