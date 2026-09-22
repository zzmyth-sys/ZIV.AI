namespace ZivAiEditor.Contracts.Imaging;

/// <summary>How a <see cref="ResolutionPolicy"/> expresses its target size.</summary>
public enum ResolutionMode
{
    /// <summary>Target longest edge (QW21edit default).</summary>
    Side,

    /// <summary>Target total pixel count (internal scenes).</summary>
    Area,

    /// <summary>Multiple of the input image's long edge (upscale default).</summary>
    Scale,

    /// <summary>Explicit target width/height (outpaint default).</summary>
    Explicit
}
