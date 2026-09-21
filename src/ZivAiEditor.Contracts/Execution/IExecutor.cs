using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Contracts.Execution;

public interface IExecutor
{
    Task<TaskState> ExecuteAsync(
        EditPlan plan,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    Task<TaskState> RerunAsync(
        string taskId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    Task<bool> CancelAsync(string taskId, CancellationToken ct = default);
}
