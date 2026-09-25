using System.Text.Json.Serialization;

namespace ZivAiEditor.Contracts.Imaging;

/// <summary>
/// How a <see cref="ResolutionPolicy"/> expresses its target size. Serialized by name in
/// data files (e.g. <c>commands.json</c> <c>fixed_resolution</c>); IPC uses its own
/// string-typed DTO, so this converter only affects what it is applied to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResolutionMode>))]
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
