using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Chat;

namespace ZivAiEditor.App.Flows;

/// <summary>
/// Re-run flow (module-boundary migration step 6): re-execute a historical node <b>in place</b>
/// — replace its output image (same <c>NodeId</c>), cascade-delete its descendant subtree, and
/// remove the stale files. Moved verbatim from <c>SessionViewModel.RerunNodeAsync</c>; the view
/// model is reached through its public UI-state methods.
/// </summary>
internal sealed partial class FlowRunner
{
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
        if (_vm.IsBusy)
        {
            return false;
        }

        var node = ChatFlowRules.FindNode(_session, nodeId);
        if (node is null)
        {
            _vm.AddHint("未知节点，无法重跑");
            return false;
        }

        if (string.IsNullOrEmpty(node.ParentNodeId))
        {
            _vm.AddHint("该节点没有源图，无法重跑");
            return false;
        }

        // Step 9C.8-B: arm the in-flight CTS so CancelCurrent() can interrupt the re-run.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _inFlightCts = cts;
        _cancelRequested = false;
        _vm.SetLastRunCanceled(false);

        // Remember the current node so it can be re-pointed if it falls in N's subtree.
        var currentBefore = _session.CurrentNodeId;

        // Update the node's existing assistant bubble in place — no new messages (Step 9C.8-A3).
        // Fallback: when no bubble carries the node id (e.g. a node never rendered), append a
        // pending bubble so the re-run still surfaces its progress.
        var target = ChatFlowRules.FindAssistantMessage(_vm.Messages, nodeId);
        if (target is null)
        {
            target = new ChatMessage { Role = ChatRole.Assistant, Text = "重跑中…", NodeId = nodeId, IsPending = true };
            _vm.Messages.Add(target);
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
            _vm.ReplacePending(target, pending);
            target = pending;
        }

        _vm.SetBusy(true);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Run first: on failure nothing is deleted (N and its subtree stay intact).
            var state = await RunWithOomRetryAsync(
                () => _executor.RerunAsync(nodeId, progress, cts.Token), progress, cts.Token);

            if (!ChatFlowRules.IsSuccess(state))
            {
                // Same bubble, no new message: show the failure where the result used to be.
                _vm.ReplacePending(target, new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = ChatFlowRules.BuildFailureMessage(state),
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
                ChatFlowRules.DeleteArtifact(_session, oldCrop?.ResultImagePath);
                ChatFlowRules.DeleteArtifact(_session, oldMask?.MaskImagePath);
                foreach (var child in removed)
                {
                    ChatFlowRules.DeleteArtifact(_session, child.ImagePath);
                    ChatFlowRules.DeleteArtifact(_session, child.Crop?.ResultImagePath);
                    ChatFlowRules.DeleteArtifact(_session, child.Mask?.MaskImagePath);
                }

                ChatFlowRules.DeleteArtifact(_session, oldImagePath);
            });

            var elapsed = stopwatch.Elapsed;

            // Swap the image into the same bubble (same position) and refresh the history
            // list. Do NOT rebuild the chat: that would clear and re-add every bubble.
            _vm.ReplacePending(target, new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = $"{elapsed.TotalSeconds:F1}秒 完成",
                ImagePath = newOutput,
                NodeId = nodeId,
            });
            _vm.RefreshHistory();
            return true;
        }
        catch (OperationCanceledException)
        {
            _vm.ReplacePending(target, new ChatMessage
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
            _vm.ReplacePending(target, new ChatMessage
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
            _vm.SetBusy(false);
            if (ReferenceEquals(_inFlightCts, cts))
            {
                _inFlightCts = null;
            }
        }
    }
}
