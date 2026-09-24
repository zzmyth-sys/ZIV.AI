using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Re-run / cancel observable state of <see cref="SessionViewModel"/> (Step 9C.8-A2/A3/B;
/// module-boundary migration step 6). The orchestration itself moved to the App's
/// <c>FlowRunner</c>; this file keeps the state the App reads (<see cref="LastRunCanceled"/>,
/// <see cref="CanRerun"/>) plus the write-back used by the flow runner.
/// </summary>
public sealed partial class SessionViewModel
{
    /// <summary>
    /// True when the last submit ended because it was canceled (Step 9C.8-B follow-up). The App
    /// uses it to revert the chat and put the prompt / attachments back into the input. Reset at
    /// the start of every submit; written by the flow runner through
    /// <see cref="SetLastRunCanceled"/>.
    /// </summary>
    public bool LastRunCanceled { get; private set; }

    /// <summary>Sets <see cref="LastRunCanceled"/> (module-boundary migration step 6: written by the flow runner).</summary>
    public void SetLastRunCanceled(bool value) => LastRunCanceled = value;

    /// <summary>
    /// Whether the DAG node <paramref name="nodeId"/> can be re-run (it has a parent, so it
    /// has a source image). Used by the UI to show the bubble's "regenerate" button. Delegates
    /// to the shared <see cref="ChatFlowRules.CanRerun"/>.
    /// </summary>
    public bool CanRerun(string nodeId) => ChatFlowRules.CanRerun(_session, nodeId);

    /// <summary>
    /// Re-runs the edit that produced <paramref name="nodeId"/> (delegates to the flow runner,
    /// module-boundary migration step 6).
    /// </summary>
    public Task<bool> RerunNodeAsync(
        string nodeId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
        => Runner().RerunNodeAsync(nodeId, progress, ct);

    /// <summary>
    /// Requests cancellation of the current in-flight submit / re-run (delegates to the flow
    /// runner, module-boundary migration step 6).
    /// </summary>
    public bool CancelCurrent() => Runner().CancelCurrent();
}
