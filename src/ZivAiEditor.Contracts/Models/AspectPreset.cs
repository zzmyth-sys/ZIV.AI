namespace ZivAiEditor.Contracts.Models;

/// <summary>A named aspect-ratio preset with concrete output dimensions.</summary>
public sealed class AspectPreset
{
    public string Name { get; init; } = "";

    /// <summary>
    /// Preset width (an aspect-ratio template offered by the model profile). Distinct
    /// from <c>ResolutionPolicy.Width</c> (a request target) and
    /// <c>InferenceResultDetail.Width</c> (the actual output).
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Preset height (aspect-ratio template). Distinct from the request target and the
    /// actual output height.
    /// </summary>
    public int Height { get; init; }
}
