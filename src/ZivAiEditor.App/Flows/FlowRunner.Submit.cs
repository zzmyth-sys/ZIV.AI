using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Chat;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.App.Flows;

/// <summary>
/// Submit-edit flow (module-boundary migration step 6): parse → execute → append a session node
/// → refresh the UI. Moved verbatim from <c>SessionViewModel.SubmitAsync</c>; the view model is
/// reached through its public UI-state methods.
/// </summary>
internal sealed partial class FlowRunner
{
    /// <summary>
    /// Runs one user input end to end: parse → execute → append a session node → refresh
    /// history. Returns <c>true</c> when an output node was produced.
    /// </summary>
    public async Task<bool> SubmitAsync(
        string input,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyList<string>? additionalImages = null,
        string? displayText = null)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0 || _vm.IsBusy)
        {
            return false;
        }

        // Step 9C.8-B: arm the in-flight CTS before any await, so CancelCurrent() can
        // interrupt the parse / execute and the token reaches the backend (Z11).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _inFlightCts = cts;
        _cancelRequested = false;
        _vm.SetLastRunCanceled(false);
        _vm.SetBusy(true);

        // The pending bubble is replaced in place once the executor returns. It is tracked
        // by identity (not index) so a context rebuild while generating cannot desync it.
        var pending = new ChatMessage { Role = ChatRole.Assistant, Text = "生成中…", IsPending = true };
        var parentId = _session.CurrentNodeId;

        // End-to-end wall-clock: click-to-bubble-replacement (Step 9C.3 收尾 10). This is
        // intentionally distinct from the backend's InferenceResultDetail.DurationMs
        // (sampling + VAE decode only) and ToolResult.Duration (one IPC submit, incl.
        // lazy load / queue) — the three are not interchangeable (Step 9C.3-R #2).
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _vm.Messages.Add(new ChatMessage { Role = ChatRole.User, Text = displayText ?? text });

            // Step 9C.10: the pipeline consumes the current node's whole image pack. The main
            // image is the (crop-aware) pipeline image; the pack's extra images are references
            // and precede the attachment references (Step 9C.5-D). The combined extras cap at 3
            // (max 4 pipeline images, D4) and an over-limit send is truncated, never refused.
            // The parser interface stays untouched, so the plan is rebuilt (init-only).
            var attachmentRefs = ChatFlowRules.NormalizeAdditionalImages(additionalImages);

            // R1: variant selection counts the whole current pack plus the attachments.
            var imageCount = _vm.CurrentImageCount + attachmentRefs.Count;

            // The image count drives single / multi template selection in the parser.
            var parsed = await _parser.ParseAsync(text, _session, imageCount, _vm.Resolution, cts.Token);
            if (!parsed.Success || parsed.Plan is null)
            {
                _vm.Messages.Add(new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = parsed.ErrorMessage ?? "无法解析该输入，请输入斜杠命令或一句编辑指令。",
                    IsError = true,
                });
                return false;
            }

            // References only apply to an image-consuming plan (a non-blank main image): a
            // text-to-image plan (`/生成`, T2I) has no main and ignores references, so the pack
            // must not pollute it (Step 9C.10).
            var plan = parsed.Plan;
            if (!string.IsNullOrWhiteSpace(plan.MainImagePath))
            {
                var refs = ChatFlowRules.AssembleReferences(
                    ChatFlowRules.CurrentPackExtras(_session), attachmentRefs, MaxAdditionalImages, out var truncated);
                if (refs.Count > 0)
                {
                    plan = ChatFlowRules.WithAdditionalImages(plan, refs);
                }

                // R2: report the images actually entering the pipeline (main + refs, post-truncation).
                _vm.AddInfo($"本次使用 {1 + refs.Count} 张图");

                if (truncated)
                {
                    _vm.AddHint("最多支持 3 张参考图，多余的已忽略");
                }
            }

            _vm.Messages.Add(pending);

            var state = await RunWithOomRetryAsync(
                () => _executor.ExecuteAsync(plan, progress, cts.Token), progress, cts.Token);

            var elapsed = stopwatch.Elapsed;
            if (ChatFlowRules.IsSuccess(state))
            {
                var outputPath = state.OutputImagePath!;
                var appended = _writer.AppendNode(parentId, outputPath, text);

                // Step 9C.8-A: snapshot the two inputs the DAG cannot reconstruct, so the
                // node can be re-run later (the prompt / tool / steps are re-parsed).
                if (ChatFlowRules.BuildRerunSpec(plan.Resolution, plan.AdditionalImages) is { } snapshot)
                {
                    _writer.SetNodeRerun(appended.NodeId, snapshot);
                }

                // Step 9C.10: record the ordered pipeline images this edit consumed (main
                // first), so the @ / <imageN> mapping survives a reload.
                _writer.SetNodeUsedImages(appended.NodeId, ChatFlowRules.BuildUsedImages(plan));

                _vm.ReplacePending(pending, new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = $"{elapsed.TotalSeconds:F1}秒 完成",
                    ImagePath = outputPath,
                    ImagePaths = new[] { outputPath },
                    NodeId = appended.NodeId,
                });
                _vm.RefreshHistory();
                return true;
            }

            if (state.Status == TaskStatus.Canceled)
            {
                // Revert the chat to the pre-send state (no new node, no bubbles); the App
                // puts the prompt / attachments back into the input (Step 9C.8-B follow-up).
                _vm.SetLastRunCanceled(true);
                _vm.RebuildContext();
                // B: non-error pointer so a canceled submit is not confused with a historical
                // node's 重新生成 (which re-runs that node's own source).
                _vm.AddInfo("已取消。输入框内容已恢复，再次发送即可重试。");
                return false;
            }

            _vm.ReplacePending(pending, new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = ChatFlowRules.BuildFailureMessage(state),
                IsError = true,
            });
            return false;
        }
        catch (OperationCanceledException)
        {
            _vm.SetLastRunCanceled(true);
            _vm.RebuildContext();
            _vm.AddInfo("已取消。输入框内容已恢复，再次发送即可重试。");
            return false;
        }
        catch (Exception ex)
        {
            _vm.ReplacePending(pending, new ChatMessage { Role = ChatRole.Assistant, Text = ex.Message, IsError = true });
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
