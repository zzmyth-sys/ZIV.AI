using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Contracts.Execution;

public interface IExecutor
{
    Task<TaskState> ExecuteAsync(
        EditPlan plan,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Re-runs the edit that produced the DAG node <paramref name="nodeId"/> (Step 9C.8-A):
    /// the plan is rebuilt from the node's command / parent image / snapshot, then executed
    /// as a fresh task (new random seed; not a bit-exact reproduction). A node with no
    /// parent (the source-image root, or a T2I-first node) cannot be re-run.
    /// </summary>
    Task<TaskState> RerunAsync(
        string nodeId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    Task<bool> CancelAsync(string taskId, CancellationToken ct = default);
}
