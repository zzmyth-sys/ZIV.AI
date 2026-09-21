using ZivAiEditor.Contracts.Enums;

namespace ZivAiEditor.Contracts.Execution;

public sealed class StepState
{
    public string StepId { get; init; } = "";
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? OutputImagePath { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
}
