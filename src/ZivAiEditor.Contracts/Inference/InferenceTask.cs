using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Contracts.Inference;

public sealed class InferenceTask
{
    public string TaskId { get; init; } = "";
    public TaskStatus State { get; init; }
    public double Progress { get; init; }
    public string? OutputPath { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
}
