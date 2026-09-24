using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.UI.Chat;

namespace ZivAiEditor.App.Flows;

/// <summary>
/// Edit-flow orchestration (module-boundary migration step 6): the cross-domain flows that were
/// previously inlined in <see cref="SessionViewModel"/> — submit-edit, re-run, cancel and
/// generate-T2I. It holds the domain ports and the view model (App → UI is allowed), and is the
/// App's implementation of the UI-side <see cref="IEditFlowRunner"/> port. The view model keeps
/// only UI state; the pure rules live in <see cref="ChatFlowRules"/> (shared, so the two paths
/// cannot diverge).
///
/// <para>Wiring is two-phase (the runner holds the view model, which holds the runner): the
/// App constructs the view model, then the runner, then calls
/// <see cref="SessionViewModel.AttachFlowRunner"/>.</para>
///
/// <para>UI interactions (dialogs / view effects) are injected per call, never through the
/// runner's constructor — the runner does not reference the shell facade or any view.</para>
/// </summary>
internal sealed partial class FlowRunner : IEditFlowRunner
{
    /// <summary>Delay before retrying a transient CUDA-OOM failure (Step 9C.6-D).</summary>
    private const int OomRetryDelayMs = 2000;

    /// <summary>At most 3 reference images (excluding the main) into the pipeline (Step 9C.5-D, D4).</summary>
    private const int MaxAdditionalImages = 3;

    private readonly SessionViewModel _vm;
    private readonly IEditSession _session;
    private readonly IEditSessionWriter _writer;
    private readonly ICommandParser _parser;
    private readonly IExecutor _executor;
    private readonly IPromptExpander? _promptExpander;
    private readonly ILlmPreflight? _llmPreflight;
    private readonly Action<string, IReadOnlyCollection<string>, bool>? _nodeArtifactsCleaner;

    public FlowRunner(
        SessionViewModel vm,
        IEditSession session,
        IEditSessionWriter writer,
        ICommandParser parser,
        IExecutor executor,
        IPromptExpander? promptExpander = null,
        ILlmPreflight? llmPreflight = null,
        Action<string, IReadOnlyCollection<string>, bool>? nodeArtifactsCleaner = null)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _promptExpander = promptExpander;
        _llmPreflight = llmPreflight;
        _nodeArtifactsCleaner = nodeArtifactsCleaner;
    }

    /// <summary>
    /// Runs <paramref name="run"/> and, when it fails with a transient CUDA-OOM error, retries
    /// once after a short pause (Step 9C.6-D). Shared by submit and re-run.
    /// </summary>
    private async Task<TaskState> RunWithOomRetryAsync(
        Func<Task<TaskState>> run,
        IProgress<TaskProgress>? progress,
        CancellationToken ct)
    {
        var state = await run();

        if (!ChatFlowRules.IsSuccess(state) && ChatFlowRules.IsOutOfMemory(state) && !ct.IsCancellationRequested)
        {
            progress?.Report(new TaskProgress
            {
                TaskId = state.TaskId,
                Status = ZivAiEditor.Contracts.Enums.TaskStatus.Running,
                Fraction = 0,
                StepIndex = 0,
                StepCount = 1,
                Message = "显存不足，正在重试…",
            });
            await Task.Delay(OomRetryDelayMs, ct);
            state = await run();
        }

        return state;
    }
}
