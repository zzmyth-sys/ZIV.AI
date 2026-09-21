namespace ZivAiEditor.Contracts.Inference;

public sealed class OptimizationOptions
{
    public bool MagCache { get; init; }
    public double MagCacheThresh { get; init; } = 0.24;
}
