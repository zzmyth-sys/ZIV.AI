using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Contracts.Execution;

public sealed class TaskProgress
{
    public string TaskId { get; init; } = "";
    public TaskStatus Status { get; init; } = TaskStatus.Pending;
    public double Fraction { get; init; }
    public int StepIndex { get; init; }
    public int StepCount { get; init; }
    public string? Message { get; init; }
}
