using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Contracts.Inference;

public sealed class InferenceTaskHandle
{
    public string TaskId { get; init; } = "";
    public TaskStatus Status { get; init; }
    public int QueuePosition { get; init; }
}
