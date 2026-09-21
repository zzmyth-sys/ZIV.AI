namespace ZivAiEditor.Contracts.Tools;

public sealed class StepProgress
{
    public string StepId { get; init; } = "";
    public double Fraction { get; init; }
    public string? Message { get; init; }
}
