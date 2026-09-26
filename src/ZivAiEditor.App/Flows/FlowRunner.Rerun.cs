using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Diagnostics;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Chat;

namespace ZivAiEditor.App.Flows;

/// <summary>
/// Re-run flow (module-boundary migration step 6): re-execute a historical node <b>in place</b>
/// — replace its output image (same <c>NodeId</c>), cascade-delete its descendant subtree, and
/// remove the stale output files. The node's <b>crop / mask are user edits</b> (9C.6-B / 9C.7),
/// not re-run by-products, so they are preserved across a re-run.
/// </summary>
internal sealed partial class FlowRunner
{
    /// <summary>
    /// Re-runs the edit that produced <paramref name="nodeId"/> <b>in place</b>
    /// (Step 9C.8-A2). The plan rebuild + execution is delegated to
    /// <see cref="IExecutor.RerunAsync"/>; on success the node's output image is replaced
    /// (same id), its descendant subtree is cascade-deleted, and the stale output files are
    /// removed. The node's crop / mask are user edits (9C.6-B / 9C.7) and are preserved — a
    /// re-run changes only the output image. On failure nothing is deleted and the node stays
    /// as it was.
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

        // P1 · /扩图 relocation: a re-run needs the parent node's crop-tool outpaint crop (the
        // parser gate remains authoritative). Fail early with a hint when the source lost it.
        var command = node.Command;
        var firstToken = (command ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.Equals(firstToken, "/扩图", StringComparison.Ordinal))
        {
            var parentCrop = _session.GetHistory()
                .FirstOrDefault(n => n.NodeId == node.ParentNodeId)?.Crop;
            if (DiagLog.IsEnabled)
            {
                // D5 diag (observation only): mirror the precheck inputs; the branch below is unchanged.
                DiagLog.Log($"D5 rerunPrecheck node={node.NodeId} parent={node.ParentNodeId} {DiagLog.DescribeCrop(parentCrop)}");
            }

            if (parentCrop is null || !parentCrop.IsOutpaint())
            {
                _vm.AddHint("「/扩图」重跑失败：源节点已无外扩裁切");
                return false;
            }
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
                    Text = TakeFailureText(ChatFlowRules.BuildFailureMessage(state)),
                    ImagePath = target.ImagePath,
                    IsError = true,
                    NodeId = nodeId,
                });
                return false;
            }

            var newOutput = state.OutputImagePath!;
            var oldImagePath = node.ImagePath;

            // Cascade-delete the descendants, then replace the node's output in place.
            var removed = _writer.RemoveSubtree(nodeId);
            _writer.ReplaceNodeImage(nodeId, newOutput);
            // Crop / mask are user edits (9C.6-B / 9C.7): a re-run must not clear them. Their
            // coordinates may no longer match the new output size; that is validated on the
            // next send (out of scope here) rather than silently dropped.

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
                // output image (its crop / mask / reference copies are kept for continued
                // re-runs — the crop / mask are user edits, not re-run by-products).
                if (descendantIds.Length > 0)
                {
                    cleaner?.Invoke(sessionId, descendantIds, true);
                }

                cleaner?.Invoke(sessionId, new[] { nodeId }, false);

                // App-owned temp / output files. External user reference images are never
                // deleted (Z24). The replaced node's crop / mask files are kept (preserved
                // user edits); only the old output image and the deleted subtree are removed.
                foreach (var child in removed)
                {
                    ChatFlowRules.DeleteArtifact(_session, child.ImagePath);
                    ChatFlowRules.DeleteArtifact(_session, child.Crop?.ResultImagePath);
                    ChatFlowRules.DeleteArtifact(_session, child.Mask?.MaskImagePath);
                }

                ChatFlowRules.DeleteArtifact(_session, oldImagePath);
            });

            var elapsed = stopwatch.Elapsed;
            _writer.SetNodeDurationMs(nodeId, (int)elapsed.TotalMilliseconds);

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
                Text = TakeFailureText("已取消。"),
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
                Text = TakeFailureText(ex.Message),
                ImagePath = target.ImagePath,
                IsError = true,
                NodeId = nodeId,
            });
            return false;
        }
        finally
        {
            _stuckRecoveryPending = false;
            _vm.SetBusy(false);
            if (ReferenceEquals(_inFlightCts, cts))
            {
                _inFlightCts = null;
            }
        }
    }
}
