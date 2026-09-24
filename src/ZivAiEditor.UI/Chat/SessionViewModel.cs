using System.Collections.ObjectModel;
using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Chat-session view model (Step 9A): it drives the in-memory session through the
/// frozen Agent contracts only — <see cref="ICommandParser"/> to turn input into an
/// <c>EditPlan</c> and <see cref="IExecutor"/> to run it. Step 9C.5: the session is
/// held as the Contracts <see cref="IEditSession"/> / <see cref="IEditSessionWriter"/>,
/// so no Agent implementation type crosses into the UI (V1). It never touches
/// <c>IInferenceClient</c> (ARCHITECTURE.md §4: the UI talks to the Agent layer through
/// contracts; inference is the tool's job).
///
/// The class has no Avalonia dependency so the chat flow is unit-testable; the App
/// layer renders <see cref="Messages"/> / <see cref="History"/> into controls.
/// </summary>
public sealed partial class SessionViewModel
{
    /// <summary>Delay before retrying a transient CUDA-OOM failure (Step 9C.6-D).</summary>
    private const int OomRetryDelayMs = 2000;

    /// <summary>At most 3 reference images (excluding the main) into the pipeline (Step 9C.5-D, D4).</summary>
    private const int MaxAdditionalImages = 3;

    private readonly IEditSession _session;
    private readonly IEditSessionWriter _writer;
    private readonly ICommandParser _parser;
    private readonly IExecutor _executor;

    /// <summary>
    /// Deletes a saved project's per-node artifacts (Step 9C.8-A2). Injected by the App so
    /// the UI view model stays free of the concrete <c>SessionStore</c> (V1); <c>null</c>
    /// in tests / when the project has never been saved.
    /// </summary>
    private readonly Action<string, IReadOnlyCollection<string>, bool>? _nodeArtifactsCleaner;

    public SessionViewModel(
        IEditSession session,
        IEditSessionWriter writer,
        ICommandParser parser,
        IExecutor executor,
        Action<string, IReadOnlyCollection<string>, bool>? nodeArtifactsCleaner = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _nodeArtifactsCleaner = nodeArtifactsCleaner;
    }

    /// <summary>The underlying session, exposed read-only (Step 9C.5: no Agent type crosses).</summary>
    public IEditSession Session => _session;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ObservableCollection<HistoryItem> History { get; } = new();

    public bool IsBusy { get; private set; }

    /// <summary>
    /// Resolution selected in the UI; applied to a plan that does not carry its own
    /// resolution (natural-language edits). <c>null</c> = leave it to the backend default.
    /// </summary>
    public ResolutionPolicy? Resolution { get; set; }

    public ImageEditMode Mode { get; set; } = ImageEditMode.Single;

    public LaunchOptions? LaunchOptions { get; private set; }

    /// <summary>
    /// Seeds the session from a launch request and rebuilds the UI state. A request
    /// with no image leaves the session empty (T2I-first / manual import).
    /// </summary>
    public void Start(LaunchOptions? options)
    {
        LaunchOptions = options;
        if (options is not null)
        {
            ApplyRequest(options);
        }
        else
        {
            RefreshHistory();
            RebuildContext();
        }
    }

    /// <summary>
    /// Applies a (new) launch request: sets the root image when present and rebuilds
    /// the chat / history. Used both at startup and when a second instance forwards a
    /// request to the running process.
    /// </summary>
    public void ApplyRequest(LaunchOptions options)
    {
        if (options.ImagePath is { Length: > 0 } image)
        {
            // Replacing the root drops the previous DAG, so its crop / mask temp files are orphans.
            ImageCropper.CleanupSession(_session.SessionId);
            MaskExporter.CleanupSession(_session.SessionId);
            _writer.SetRoot(image);
        }

        RefreshHistory();
        RebuildContext();
    }

    /// <summary>
    /// Runs one user input end to end: parse → execute → append a session node →
    /// refresh history. Returns <c>true</c> when an output node was produced.
    /// </summary>
    public async Task<bool> SubmitAsync(
        string input,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyList<string>? additionalImages = null,
        string? displayText = null)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0 || IsBusy)
        {
            return false;
        }

        // Step 9C.8-B: arm the in-flight CTS before any await, so CancelCurrent() can
        // interrupt the parse / execute and the token reaches the backend (Z11).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _inFlightCts = cts;
        _cancelRequested = false;
        LastRunCanceled = false;
        IsBusy = true;

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
            Messages.Add(new ChatMessage { Role = ChatRole.User, Text = displayText ?? text });

            // Step 9C.10: the pipeline consumes the current node's whole image pack. The main
            // image is the (crop-aware) pipeline image; the pack's extra images are references
            // and precede the attachment references (Step 9C.5-D). The combined extras cap at 3
            // (max 4 pipeline images, D4) and an over-limit send is truncated, never refused.
            // The parser interface stays untouched, so the plan is rebuilt (init-only).
            var attachmentRefs = NormalizeAdditionalImages(additionalImages);

            // R1: variant selection counts the whole current pack plus the attachments.
            var imageCount = CurrentImageCount + attachmentRefs.Count;

            // The image count drives single / multi template selection in the parser.
            var parsed = await _parser.ParseAsync(text, _session, imageCount, Resolution, cts.Token);
            if (!parsed.Success || parsed.Plan is null)
            {
                Messages.Add(new ChatMessage
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
                var refs = AssembleReferences(
                    CurrentPackExtras(), attachmentRefs, MaxAdditionalImages, out var truncated);
                if (refs.Count > 0)
                {
                    plan = WithAdditionalImages(plan, refs);
                }

                // R2: report the images actually entering the pipeline (main + refs, post-truncation).
                AddInfo($"本次使用 {1 + refs.Count} 张图");

                if (truncated)
                {
                    AddHint("最多支持 3 张参考图，多余的已忽略");
                }
            }

            Messages.Add(pending);

            var state = await RunWithOomRetryAsync(
                () => _executor.ExecuteAsync(plan, progress, cts.Token), progress, cts.Token);

            var elapsed = stopwatch.Elapsed;
            if (IsSuccess(state))
            {
                var outputPath = state.OutputImagePath!;
                var appended = _writer.AppendNode(parentId, outputPath, text);

                // Step 9C.8-A: snapshot the two inputs the DAG cannot reconstruct, so the
                // node can be re-run later (the prompt / tool / steps are re-parsed).
                if (BuildRerunSpec(plan.Resolution, plan.AdditionalImages) is { } snapshot)
                {
                    _writer.SetNodeRerun(appended.NodeId, snapshot);
                }

                // Step 9C.10: record the ordered pipeline images this edit consumed (main
                // first), so the @ / <imageN> mapping survives a reload.
                _writer.SetNodeUsedImages(appended.NodeId, BuildUsedImages(plan));

                ReplacePending(pending, new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = $"{elapsed.TotalSeconds:F1}秒 完成",
                    ImagePath = outputPath,
                    ImagePaths = new[] { outputPath },
                    NodeId = appended.NodeId,
                });
                RefreshHistory();
                return true;
            }

            if (state.Status == TaskStatus.Canceled)
            {
                // Revert the chat to the pre-send state (no new node, no bubbles); the App
                // puts the prompt / attachments back into the input (Step 9C.8-B follow-up).
                LastRunCanceled = true;
                RebuildContext();
                return false;
            }

            ReplacePending(pending, new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = BuildFailureMessage(state),
                IsError = true,
            });
            return false;
        }
        catch (OperationCanceledException)
        {
            LastRunCanceled = true;
            RebuildContext();
            return false;
        }
        catch (Exception ex)
        {
            ReplacePending(pending, new ChatMessage { Role = ChatRole.Assistant, Text = ex.Message, IsError = true });
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

    /// <summary>
    /// Switches the working node and rebuilds the chat to that node's context
    /// (root → … → node). Later edits branch from here without overwriting old ones.
    /// </summary>
    public bool NavigateTo(string nodeId)
    {
        if (!_writer.NavigateTo(nodeId))
        {
            return false;
        }

        RefreshHistory();
        RebuildContext();
        return true;
    }

    /// <summary>Whether the session has a root node (and thus a current image).</summary>
    public bool HasRootImage => _session.RootImagePath is { Length: > 0 };

    /// <summary>
    /// Whether the send button should be enabled (Step 9C.6-C). Blank text is never
    /// sendable; with attachments the mode must match the count (Single &lt;= 1,
    /// Multi &gt;= 2). With no attachments the mode does not gate the send, so
    /// natural-language text-to-image stays available.
    /// </summary>
    public bool CanSend(string? input, int attachmentCount)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (attachmentCount == 0)
        {
            return true;
        }

        return Mode == ImageEditMode.Single ? attachmentCount <= 1 : attachmentCount >= 2;
    }

    /// <summary>
    /// Resolves the pending attachments for a submit (Step 9C.6-C). Mutates the session
    /// when an attachment becomes the root. Returns <see cref="AttachmentPreparation.NoImage"/>
    /// only for a slash command with no image and no attachment (natural language may
    /// still build a text-to-image plan).
    /// </summary>
    public AttachmentPreparation PrepareAttachments(string input, IReadOnlyList<string>? attachments)
    {
        if (attachments is { Count: > 0 })
        {
            if (HasRootImage)
            {
                return AttachmentPreparation.NeedsDecision;
            }

            SetRootImage(attachments[0]);
            return AttachmentPreparation.Ready;
        }

        if (HasRootImage)
        {
            return AttachmentPreparation.Ready;
        }

        return (input ?? "").TrimStart().StartsWith('/')
            ? AttachmentPreparation.NoImage
            : AttachmentPreparation.Ready;
    }

    /// <summary>
    /// Applies the "new session" choice: the first attachment becomes the new root and
    /// the existing DAG is reset (Step 9C.6-C). No-op when the list is empty.
    /// </summary>
    public void StartNewSessionFrom(IReadOnlyList<string> attachments)
    {
        if (attachments is { Count: > 0 })
        {
            SetRootImage(attachments[0]);
        }
    }

    /// <summary>Appends a non-blocking system hint to the chat stream (Step 9C.6-C).</summary>
    public void AddHint(string text)
        => Messages.Add(new ChatMessage { Role = ChatRole.System, Text = text, IsError = true });

    /// <summary>
    /// Sets (or clears) the intrinsic crop of one node (Step 9C.6-B) and refreshes the
    /// history. A crop is a node property, not an edit step: no node is added. A no-op when
    /// the node is unknown.
    ///
    /// <para>Step 9C.7 (D2): a crop change clears the node's mask; when that happens a hint
    /// is appended to the chat after any context rebuild.</para>
    /// </summary>
    public void SetNodeCrop(string nodeId, CropSpec? crop)
    {
        var hadMask = FindNodeMask(nodeId) is not null;

        _writer.SetNodeCrop(nodeId, crop);
        RefreshHistory();

        // Step 9C.6-B: the chat shows each node's pipeline image, so a crop change must
        // re-render the stream (the crop result replaces the node's image in the bubbles).
        // Skipped while generating, to avoid clearing the in-flight bubble.
        if (!IsBusy)
        {
            RebuildContext();
        }

        if (hadMask && FindNodeMask(nodeId) is null)
        {
            AddHint("裁切已改，遮罩已重置");
        }
    }

    /// <summary>
    /// Sets (or clears) the hand-drawn mask of one node (Step 9C.7) and refreshes the
    /// history. A mask is a node property, not an edit step: no node is added, and the
    /// displayed pipeline image is unchanged, so the chat stream is not rebuilt. A no-op
    /// when the node is unknown.
    /// </summary>
    public void SetNodeMask(string nodeId, MaskSpec? mask)
    {
        _writer.SetNodeMask(nodeId, mask);
        RefreshHistory();
    }

    private MaskSpec? FindNodeMask(string nodeId)
    {
        foreach (var node in _session.GetHistory())
        {
            if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            {
                return node.Mask;
            }
        }

        return null;
    }

    /// <summary>
    /// Rebuilds the chat stream and history from the current session state (Step 9C.6-E).
    /// Used after an in-place project restore so the UI reflects the opened project without
    /// rebuilding the view model.
    /// </summary>
    public void Reload()
    {
        RefreshHistory();
        RebuildContext();
    }

    /// <summary>Rebuilds <see cref="History"/> from the session's node set.</summary>
    public void RefreshHistory()
    {
        History.Clear();
        foreach (var node in _session.GetHistory())
        {
            History.Add(new HistoryItem
            {
                Node = node,
                Depth = _session.GetDepth(node),
                IsCurrent = string.Equals(node.NodeId, _session.CurrentNodeId, StringComparison.Ordinal),
            });
        }
    }

    /// <summary>
    /// The parent (reference) image path for a chat image, used by the swipe-compare
    /// overlay (Step 9C.2-C). Delegates to the session DAG query (V2) — the traversal
    /// lives in <see cref="IEditSession.GetParentImagePath"/>.
    /// </summary>
    public string? GetParentImagePath(string? imagePath) => _session.GetParentImagePath(imagePath);

    /// <summary>
    /// The parent (reference) pipeline image path for a chat image, used by swipe-compare
    /// (Step 9C.6-B): the parent node's crop result, else its output. Delegates to
    /// <see cref="IEditSession.GetParentPipelineImagePath"/>.
    /// </summary>
    public string? GetParentPipelineImagePath(string? imagePath)
        => _session.GetParentPipelineImagePath(imagePath);

    /// <summary>
    /// Clears the chat and replays the path from the root node to the current node. Each
    /// bubble shows the node's <b>pipeline</b> image (Step 9C.6-B): its crop result when it
    /// has one, otherwise its output — so a crop is reflected in the chat stream.
    /// </summary>
    private void RebuildContext()
    {
        Messages.Clear();

        var path = _session.GetPathToCurrent();
        if (path.Count == 0)
        {
            // No node path (e.g. T2I-first before any node): show the bare root if set.
            if (_session.RootImagePath is { Length: > 0 } root)
            {
                Messages.Add(new ChatMessage { Role = ChatRole.System, Text = "起始图像", ImagePath = root });
            }

            return;
        }

        // The first node is the root ("原图"), already rendered as the "起始图像" bubble. A
        // multi-image root carries its whole pack so the App renders the thumbnail row (Q4).
        Messages.Add(new ChatMessage
        {
            Role = ChatRole.System,
            Text = "起始图像",
            ImagePath = PipelinePath(path[0]),
            ImagePaths = BuildDisplayPack(path[0]),
            NodeId = path[0].NodeId,
        });

        foreach (var node in path.Skip(1))
        {
            Messages.Add(new ChatMessage { Role = ChatRole.User, Text = node.Command });
            Messages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = "完成",
                ImagePath = PipelinePath(node),
                ImagePaths = new[] { PipelinePath(node) },
                NodeId = node.NodeId,
            });
        }
    }

    /// <summary>
    /// Replaces the pending "生成中" bubble (tracked by identity) with the final message. If
    /// the context was rebuilt while generating and the bubble is gone, the final message is
    /// appended instead of throwing on a stale index.
    /// </summary>
    private void ReplacePending(ChatMessage pending, ChatMessage replacement)
    {
        var index = Messages.IndexOf(pending);
        if (index >= 0)
        {
            Messages[index] = replacement;
        }
        else
        {
            Messages.Add(replacement);
        }
    }

    /// <summary>The image a node shows in the chat: its crop result, else its output.</summary>
    private static string PipelinePath(IEditNode node)
        => node.Crop is { ResultImagePath.Length: > 0 } crop ? crop.ResultImagePath : node.ImagePath;
}
