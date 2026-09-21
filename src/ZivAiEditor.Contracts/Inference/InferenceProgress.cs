namespace ZivAiEditor.Contracts.Inference;

public sealed class InferenceProgress
{
    public double Fraction { get; init; }
    public string? Message { get; init; }
}
