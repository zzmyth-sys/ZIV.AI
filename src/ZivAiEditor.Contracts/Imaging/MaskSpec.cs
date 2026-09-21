namespace ZivAiEditor.Contracts.Imaging;

public sealed class MaskSpec
{
    public string MaskImagePath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsBinary { get; init; } = true;
    public bool Invert { get; init; }
}
