using System.Collections.Concurrent;
using System.Diagnostics;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Agent;

/// <summary>
/// Orchestrates an <see cref="EditPlan"/>: it runs each step in <c>Order</c>
/// through the injected <see cref="IToolRegistry"/>, chains each step's output
/// into the next step's input, reports progress, and records a
/// <see cref="TaskState"/>. It never talks to <c>IInferenceClient</c> directly —
/// inference is the tool's job (ARCHITECTURE.md §4). All work is serialized
/// through <see cref="ExecutionQueue"/> so the GPU is never used concurrently
/// (Z18).
/// </summary>
public sealed class Executor : IExecutor
{
    private readonly IToolRegistry _tools;
    private readonly ExecutionQueue _queue;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    public Executor(IToolRegistry tools, ExecutionQueue queue)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public async Task<TaskState> ExecuteAsync(
        EditPlan plan,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var ordered = plan.Steps.OrderBy(step => step.Order).ToArray();
        var state = new TaskState
        {
            TaskId = Guid.NewGuid().ToString("N"),
            Status = TaskStatus.Pending,
            Plan = plan,
            StepStates = ordered
                .Select(step => new StepState { StepId = step.StepId, Status = StepStatus.Pending })
                .ToArray(),
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _running[state.TaskId] = cts;
        Report(progress, state, -1, "queued");

        try
        {
            await _queue.RunAsync(
                async token =>
                {
                    await ExecuteCoreAsync(plan, ordered, state, progress, token).ConfigureAwait(false);
                    return true;
                },
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Canceled while queued (never entered ExecuteCoreAsync).
            MarkRemaining(state, 0, StepStatus.Canceled, "canceled");
            MarkCanceled(state, "canceled");
        }
        finally
        {
            _running.TryRemove(state.TaskId, out _);
        }

        return state;
    }

    public Task<TaskState> RerunAsync(
        string taskId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "RerunAsync requires task persistence (ITaskStore), scheduled for a later step.");

    public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return Task.FromResult(false);
        }

        if (!_running.TryGetValue(taskId, out var cts))
        {
            return Task.FromResult(false);
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private async Task ExecuteCoreAsync(
        EditPlan plan,
        IReadOnlyList<EditStep> ordered,
        TaskState state,
        IProgress<TaskProgress>? progress,
        CancellationToken ct)
    {
        state.StartedAt = DateTimeOffset.Now;
        state.Status = TaskStatus.Running;
        Report(progress, state, -1, "running");

        var statusByStepId = new Dictionary<string, StepStatus>(StringComparer.Ordinal);
        var currentImage = plan.MainImagePath;
        var workingDirectory = ResolveWorkingDirectory(plan.MainImagePath);
        var skipped = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var step = ordered[i];
            var stepState = state.StepStates[i];

            if (ct.IsCancellationRequested)
            {
                MarkRemaining(state, i, StepStatus.Canceled, "canceled");
                MarkCanceled(state, "canceled");
                return;
            }

            var unmet = step.DependsOn.FirstOrDefault(
                dependency => !statusByStepId.TryGetValue(dependency, out var status) || status != StepStatus.Succeeded);
            if (unmet is not null)
            {
                stepState.Status = StepStatus.Skipped;
                stepState.ErrorMessage = $"dependency '{unmet}' did not succeed.";
                statusByStepId[step.StepId] = StepStatus.Skipped;
                skipped = true;
                Report(progress, state, i, $"skipped: {step.StepId}");
                continue;
            }

            var tool = _tools.Get(step.ToolName);
            if (tool is null)
            {
                stepState.Status = StepStatus.Failed;
                stepState.ErrorMessage = $"no tool registered for '{step.ToolName}'.";
                statusByStepId[step.StepId] = StepStatus.Failed;
                FailTask(state, stepState.ErrorMessage, i);
                return;
            }

            stepState.Status = StepStatus.Running;
            Report(progress, state, i, $"running: {step.ToolName}");

            var input = new ToolInput
            {
                StepId = step.StepId,
                MainImagePath = currentImage,
                ReferenceImagePath = plan.ReferenceImagePath,
                AdditionalImages = plan.AdditionalImages,
                Mask = plan.Mask,
                Parameters = step.Parameters,
                WorkingDirectory = workingDirectory,
                Resolution = plan.Resolution,
            };

            var stepProgress = progress is null
                ? null
                : new StepProgressAdapter(progress, state, i, ordered.Count);

            ToolResult result;
            try
            {
                result = await tool.ExecuteAsync(input, stepProgress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                stepState.Status = StepStatus.Canceled;
                statusByStepId[step.StepId] = StepStatus.Canceled;
                MarkRemaining(state, i + 1, StepStatus.Canceled, "canceled");
                MarkCanceled(state, "canceled");
                return;
            }
            catch (Exception ex)
            {
                result = new ToolResult
                {
                    StepId = step.StepId,
                    Success = false,
                    ErrorMessage = ex.Message,
                };
            }

            stepState.Duration = result.Duration;
            stepState.OutputImagePath = result.OutputImagePath;

            if (!result.Success)
            {
                stepState.Status = StepStatus.Failed;
                stepState.ErrorMessage = result.ErrorMessage ?? "tool reported failure.";
                statusByStepId[step.StepId] = StepStatus.Failed;
                FailTask(state, stepState.ErrorMessage, i);
                return;
            }

            stepState.Status = StepStatus.Succeeded;
            statusByStepId[step.StepId] = StepStatus.Succeeded;
            if (!string.IsNullOrWhiteSpace(result.OutputImagePath))
            {
                currentImage = result.OutputImagePath;
                state.OutputImagePath = currentImage;
            }

            Report(progress, state, i, $"succeeded: {step.ToolName}");
        }

        state.FinishedAt = DateTimeOffset.Now;
        if (skipped)
        {
            state.Status = TaskStatus.Failed;
            state.ErrorMessage ??= "one or more steps were skipped because a dependency did not succeed.";
        }
        else
        {
            state.Status = TaskStatus.Succeeded;
        }

        Report(progress, state, ordered.Count - 1, state.Status == TaskStatus.Succeeded ? "succeeded" : "failed");
    }

    private static void FailTask(TaskState state, string message, int failedIndex)
    {
        state.Status = TaskStatus.Failed;
        state.ErrorMessage = message;
        MarkRemaining(state, failedIndex + 1, StepStatus.Skipped, "not executed.");
        state.FinishedAt = DateTimeOffset.Now;
    }

    private static void MarkCanceled(TaskState state, string message)
    {
        state.Status = TaskStatus.Canceled;
        state.ErrorMessage ??= message;
        state.FinishedAt = DateTimeOffset.Now;
    }

    private static void MarkRemaining(TaskState state, int fromIndex, StepStatus status, string message)
    {
        for (var i = fromIndex; i < state.StepStates.Count; i++)
        {
            var stepState = state.StepStates[i];
            if (stepState.Status == StepStatus.Pending)
            {
                stepState.Status = status;
                stepState.ErrorMessage = message;
            }
        }
    }

    private static string ResolveWorkingDirectory(string mainImagePath)
    {
        if (string.IsNullOrWhiteSpace(mainImagePath))
        {
            return "";
        }

        var directory = Path.GetDirectoryName(mainImagePath);
        return string.IsNullOrEmpty(directory) ? "" : directory;
    }

    private static void Report(IProgress<TaskProgress>? progress, TaskState state, int stepIndex, string message)
    {
        if (progress is null)
        {
            return;
        }

        var count = state.StepStates.Count;
        var fraction = count == 0 ? 0 : (double)Math.Clamp(stepIndex + 1, 0, count) / count;

        progress.Report(new TaskProgress
        {
            TaskId = state.TaskId,
            Status = state.Status,
            Fraction = fraction,
            StepIndex = stepIndex,
            StepCount = count,
            Message = message,
        });
    }

    /// <summary>Maps a step's <see cref="StepProgress"/> onto overall <see cref="TaskProgress"/>.</summary>
    private sealed class StepProgressAdapter : IProgress<StepProgress>
    {
        private readonly IProgress<TaskProgress> _inner;
        private readonly TaskState _state;
        private readonly int _index;
        private readonly int _count;

        public StepProgressAdapter(IProgress<TaskProgress> inner, TaskState state, int index, int count)
        {
            _inner = inner;
            _state = state;
            _index = index;
            _count = count;
        }

        public void Report(StepProgress value)
        {
            var stepFraction = Math.Clamp(value.Fraction, 0, 1);
            var overall = _count == 0 ? 0 : (_index + stepFraction) / _count;

            _inner.Report(new TaskProgress
            {
                TaskId = _state.TaskId,
                Status = TaskStatus.Running,
                Fraction = overall,
                StepIndex = _index,
                StepCount = _count,
                Message = value.Message,
            });
        }
    }
}
