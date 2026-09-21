using ZivAiEditor.Contracts.Planning;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Contracts.Execution;

public sealed class TaskState
{
    public string TaskId { get; init; } = "";
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public EditPlan Plan { get; init; } = new();
    public IReadOnlyList<StepState> StepStates { get; set; } = Array.Empty<StepState>();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? OutputImagePath { get; set; }
    public string? ErrorMessage { get; set; }
}
