using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

// Step 1 skeleton: no business logic. Executor / ExecutionQueue arrive in later steps.
internal sealed class PlaceholderExecutor : IExecutor
{
    public Task<TaskState> ExecuteAsync(
        EditPlan plan,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<TaskState> RerunAsync(
        string taskId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
