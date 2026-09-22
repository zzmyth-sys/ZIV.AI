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

    /// <summary>Mode=<see cref="ResolutionMode.Explicit"/>: explicit target width.</summary>
    public int? Width { get; init; }

    /// <summary>Mode=<see cref="ResolutionMode.Explicit"/>: explicit target height.</summary>
    public int? Height { get; init; }

    /// <summary>Safety ceiling against UI misconfiguration (OOM guard); default 2048².</summary>
    public int MaxPixels { get; init; } = 4_194_304;
}
