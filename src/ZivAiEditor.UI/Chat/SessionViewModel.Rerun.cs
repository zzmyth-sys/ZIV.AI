using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Re-run / cancel half of <see cref="SessionViewModel"/> (Step 9C.8-A2/A3/B): re-executing a
/// historical node <b>in place</b> — the node's output image is replaced (same <c>NodeId</c>),
/// its descendant subtree is cascade-deleted, and the stale files are removed — plus the
/// in-flight cancellation source / <see cref="CancelCurrent"/>. It also hosts the shared
/// CUDA-OOM retry used by both <c>SubmitAsync</c> and <c>RerunNodeAsync</c>. Split out to keep
/// the main file within the Z8 budget.
/// </summary>
public sealed partial class SessionViewModel
{
    /// <summary>
    /// The in-flight run's cancellation source (Step 9C.8-B), owned by the view model so the
    /// UI can cancel the current submit / re-run through <see cref="CancelCurrent"/>.
    /// <c>null</c> when idle. Created and cleared on the UI thread (single-threaded).
    /// </summary>
    private CancellationTokenSource? _inFlightCts;

    /// <summary>
    /// True once a cancel was requested for the current run; makes <see cref="CancelCurrent"/>
    /// idempotent (a second click returns <c>false</c>). Reset when a run starts. Never read by
    /// the OOM retry — the token is the single source of truth for actual interruption.
    /// </summary>
    private bool _cancelRequested;

    /// <summary>
    /// Requests cancellation of the current in-flight submit / re-run (Step 9C.8-B). Returns
    /// <c>false</c> when there is nothing to cancel, when a cancel was already requested for
    /// this run, or when the source was already disposed; never throws. The cancellation is
    /// forwarded to the backend through the run's token (Z11).
    /// </summary>
    public bool CancelCurrent()
    {
        if (_cancelRequested || _inFlightCts is not { } cts)
        {
            return false;
        }

        try
        {
            cts.Cancel();
        }
        catch (Exception)
        {
            // Never throw (R1): a disposed / faulting source just means there is nothing to cancel.
            return false;
        }

        _cancelRequested = true;
        return true;
    }

    /// <summary>
    /// True when the last submit ended because it was canceled (Step 9C.8-B follow-up). The
    /// App uses it to revert the chat and put the prompt / attachments back into the input.
    /// Reset at the start of every submit.
    /// </summary>
    public bool LastRunCanceled { get; private set; }

    /// <summary>
    /// Whether the DAG node <paramref name="nodeId"/> can be re-run (it has a parent, so it
    /// has a source image). Used by the UI to show the bubble's "regenerate" button.
    /// </summary>
    public bool CanRerun(string nodeId)
    {
        var node = FindNode(nodeId);
        return node is not null && !string.IsNullOrEmpty(node.ParentNodeId);
    }

    /// <summary>
    /// Runs <paramref name="run"/> and, when it fails with a transient CUDA-OOM error,
    /// retries once after a short pause (Step 9C.6-D). The backend can hit OOM while the
    /// previous run's memory is still settling; the next attempt usually succeeds.
    /// </summary>
    private async Task<TaskState> RunWithOomRetryAsync(
        Func<Task<TaskState>> run,
        IProgress<TaskProgress>? progress,
        CancellationToken ct)
    {
        var state = await run();

        if (!IsSuccess(state) && IsOutOfMemory(state) && !ct.IsCancellationRequested)
        {
            progress?.Report(new TaskProgress
            {
                TaskId = state.TaskId,
                Status = TaskStatus.Running,
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

    /// <summary>
    /// Builds the node's re-run snapshot (Step 9C.8-A), or <c>null</c> when the edit
    /// carried neither a UI resolution nor reference images (so <c>session.json</c> stays
    /// clean and the node's <c>Rerun</c> is <c>null</c>).
    /// </summary>
    private static RerunSpec? BuildRerunSpec(
        ResolutionPolicy? resolution,
        IReadOnlyList<string>? additionalImages)
    {
        if (resolution is null && (additionalImages is null || additionalImages.Count == 0))
        {
            return null;
        }

        return new RerunSpec
        {
            Resolution = resolution,
            AdditionalImages = additionalImages ?? Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Re-runs the edit that produced <paramref name="nodeId"/> <b>in place</b>
    /// (Step 9C.8-A2). The plan rebuild + execution is delegated to
    /// <see cref="IExecutor.RerunAsync"/>; on success the node's output image is replaced
    /// (same id), its descendant subtree is cascade-deleted, the stale crop / mask are
    /// cleared, and the app-owned files are removed. On failure nothing is deleted and the
    /// node stays as it was.
    /// </summary>
    public async Task<bool> RerunNodeAsync(
        string nodeId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return false;
        }

        var node = FindNode(nodeId);
        if (node is null)
        {
            AddHint("未知节点，无法重跑");
            return false;
        }

        if (string.IsNullOrEmpty(node.ParentNodeId))
        {
            AddHint("该节点没有源图，无法重跑");
            return false;
        }

        // Step 9C.8-B: arm the in-flight CTS so CancelCurrent() can interrupt the re-run.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _inFlightCts = cts;
        _cancelRequested = false;
        LastRunCanceled = false;

        // Remember the current node so it can be re-pointed if it falls in N's subtree.
        var currentBefore = _session.CurrentNodeId;

        // Update the node's existing assistant bubble in place — no new messages (Step 9C.8-A3).
        // Fallback: when no bubble carries the node id (e.g. a node never rendered), append a
        // pending bubble so the re-run still surfaces its progress.
        var target = FindAssistantMessage(nodeId);
        if (target is null)
        {
            target = new ChatMessage { Role = ChatRole.Assistant, Text = "重跑中…", NodeId = nodeId, IsPending = true };
            Messages.Add(target);
        }
        else
        {
            var pending = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = "重跑中…",
                ImagePath = target.ImagePath,
                NodeId = nodeId,
                IsPending = true,
            };
            ReplacePending(target, pending);
            target = pending;
        }

        IsBusy = true;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Run first: on failure nothing is deleted (N and its subtree stay intact).
            var state = await RunWithOomRetryAsync(
                () => _executor.RerunAsync(nodeId, progress, cts.Token), progress, cts.Token);

            if (!IsSuccess(state))
            {
                // Same bubble, no new message: show the failure where the result used to be.
                ReplacePending(target, new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = BuildFailureMessage(state),
                    ImagePath = target.ImagePath,
                    IsError = true,
                    NodeId = nodeId,
                });
                return false;
            }

            var newOutput = state.OutputImagePath!;
            var oldImagePath = node.ImagePath;
            var oldCrop = node.Crop;
            var oldMask = node.Mask;

            // Cascade-delete the descendants, then replace the node's output in place.
            var removed = _writer.RemoveSubtree(nodeId);
            _writer.ReplaceNodeImage(nodeId, newOutput);
            _writer.SetNodeCrop(nodeId, null);
            _writer.SetNodeMask(nodeId, null);

            // If the current node was inside the subtree, re-point it at the replaced node.
            if (string.Equals(currentBefore, nodeId, StringComparison.Ordinal)
                || removed.Any(r => string.Equals(r.NodeId, currentBefore, StringComparison.Ordinal)))
            {
                _writer.NavigateTo(nodeId);
            }

            var descendantIds = removed.Select(r => r.NodeId).ToArray();
            var sessionId = _session.SessionId;
            var cleaner = _nodeArtifactsCleaner;
            await Task.Run(() =>
            {
                // Project copies: descendants fully (incl. refs), the replaced node's stale
                // image / crop / mask (its reference copies are kept for continued re-runs).
                if (descendantIds.Length > 0)
                {
                    cleaner?.Invoke(sessionId, descendantIds, true);
                }

                cleaner?.Invoke(sessionId, new[] { nodeId }, false);

                // App-owned temp / output files. External user reference images are never
                // deleted (Z24): only the project copies above are removed.
                DeleteArtifact(oldCrop?.ResultImagePath);
                DeleteArtifact(oldMask?.MaskImagePath);
                foreach (var child in removed)
                {
                    DeleteArtifact(child.ImagePath);
                    DeleteArtifact(child.Crop?.ResultImagePath);
                    DeleteArtifact(child.Mask?.MaskImagePath);
                }

                DeleteArtifact(oldImagePath);
            });

            var elapsed = stopwatch.Elapsed;

            // Swap the image into the same bubble (same position) and refresh the history
            // list. Do NOT rebuild the chat: that would clear and re-add every bubble.
            ReplacePending(target, new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = $"{elapsed.TotalSeconds:F1}秒 完成",
                ImagePath = newOutput,
                NodeId = nodeId,
            });
            RefreshHistory();
            return true;
        }
        catch (OperationCanceledException)
        {
            ReplacePending(target, new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = "已取消。",
                ImagePath = target.ImagePath,
                IsError = true,
                NodeId = nodeId,
            });
            return false;
        }
        catch (Exception ex)
        {
            ReplacePending(target, new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = ex.Message,
                ImagePath = target.ImagePath,
                IsError = true,
                NodeId = nodeId,
            });
            return false;
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(_inFlightCts, cts))
            {
                _inFlightCts = null;
            }
        }
    }

    /// <summary>The image a node shows in the chat: its crop result, else its output.</summary>
    private static bool IsSuccess(TaskState state)
        => state.Status == TaskStatus.Succeeded && !string.IsNullOrWhiteSpace(state.OutputImagePath);

    /// <summary>
    /// True when a failed task looks like a CUDA out-of-memory error (Step 9C.6-D). The
    /// backend surfaces <c>AcceleratorError: CUDA error: out of memory</c> / torch's
    /// <c>CUDA out of memory</c> as the task error message.
    /// </summary>
    private static bool IsOutOfMemory(TaskState state)
    {
        var message = state.ErrorMessage ?? "";
        return message.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
               || message.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Human-readable failure text (Step 9C.8-B): a canceled task is always reported as
    /// "已取消。" regardless of the internal error code the executor writes ("canceled").
    /// </summary>
    private static string BuildFailureMessage(TaskState state)
        => state.Status == TaskStatus.Canceled
            ? "已取消。"
            : state.ErrorMessage ?? $"执行未成功（{state.Status}）。";

    /// <summary>
    /// The ordered pipeline images an edit consumed (Step 9C.10): the main image (when
    /// present) followed by the reference images, mirroring the pipeline's
    /// <c>image1</c>..<c>imageN</c>. Blanks are dropped.
    /// </summary>
    private static IReadOnlyList<string> BuildUsedImages(EditPlan plan)
    {
        var images = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.MainImagePath))
        {
            images.Add(plan.MainImagePath);
        }

        foreach (var path in plan.AdditionalImages)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                images.Add(path);
            }
        }

        return images;
    }

    /// <summary>
    /// Drops blank entries from the UI-supplied reference images and keeps their order
    /// (Step 9C.5-D). A <c>null</c> / empty list yields an empty list.
    /// </summary>
    private static IReadOnlyList<string> NormalizeAdditionalImages(IReadOnlyList<string>? additionalImages)
    {
        if (additionalImages is null || additionalImages.Count == 0)
        {
            return Array.Empty<string>();
        }

        var images = new List<string>(additionalImages.Count);
        foreach (var path in additionalImages)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                images.Add(path);
            }
        }

        return images;
    }

    /// <summary>
    /// Rebuilds <paramref name="plan"/> with <see cref="EditPlan.AdditionalImages"/> set
    /// (Step 9C.5-D). <see cref="EditPlan"/> is init-only, so the plan is copied rather
    /// than mutated; the parser interface is untouched.
    /// </summary>
    private static EditPlan WithAdditionalImages(EditPlan plan, IReadOnlyList<string> additionalImages)
        => new()
        {
            PlanId = plan.PlanId,
            SourcePrompt = plan.SourcePrompt,
            MainImagePath = plan.MainImagePath,
            ReferenceImagePath = plan.ReferenceImagePath,
            AdditionalImages = additionalImages,
            Mask = plan.Mask,
            Steps = plan.Steps,
            CreatedAt = plan.CreatedAt,
            Resolution = plan.Resolution,
        };

    /// <summary>
    /// Deletes one app-owned artifact file, never throwing. The session's source image is
    /// never deleted (Z24), even defensively.
    /// </summary>
    private void DeleteArtifact(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (string.Equals(path, _session.RootImagePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A locked / missing file must not abort the re-run.
        }
    }

    /// <summary>
    /// The assistant bubble rendered for <paramref name="nodeId"/>, or <c>null</c> when none
    /// (Step 9C.8-A3). Used by a re-run to update that bubble in place.
    /// </summary>
    private ChatMessage? FindAssistantMessage(string nodeId)
    {
        foreach (var message in Messages)
        {
            if (message.Role == ChatRole.Assistant
                && string.Equals(message.NodeId, nodeId, StringComparison.Ordinal))
            {
                return message;
            }
        }

        return null;
    }

    private IEditNode? FindNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return null;
        }

        foreach (var node in _session.GetHistory())
        {
            if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }
}
