namespace ZivAiEditor.Contracts.Inference;

public sealed class LoraOptions
{
    public string Path { get; init; } = "";
    public double StrengthModel { get; init; } = 1.0;
    public double StrengthClip { get; init; } = 1.0;
}
