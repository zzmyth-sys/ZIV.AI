namespace ZivAiEditor.Contracts.Models;

/// <summary>A named aspect-ratio preset with concrete output dimensions.</summary>
public sealed class AspectPreset
{
    public string Name { get; init; } = "";

    public int Width { get; init; }

    public int Height { get; init; }
}
