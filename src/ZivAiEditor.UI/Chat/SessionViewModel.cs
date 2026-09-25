using System.Collections.ObjectModel;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Chat-session <b>UI state</b> holder (Step 9A; module-boundary migration step 6). It keeps the
/// observable chat / history / pending state and the user-facing gates, and forwards the
/// cross-domain orchestration (<see cref="SubmitAsync"/> / <see cref="RerunNodeAsync"/> /
/// <see cref="CancelCurrent"/>) to the injected <see cref="IEditFlowRunner"/> (the App's
/// <c>FlowRunner</c>). The pure rules/projections live in <see cref="ChatFlowRules"/>, shared by
/// both this view model and the flow runner so the two paths cannot diverge.
///
/// <para>No Avalonia dependency, so the UI state is unit-testable; the App layer renders
/// <see cref="Messages"/> / <see cref="History"/> into controls.</para>
/// </summary>
public sealed partial class SessionViewModel
{
    private readonly IEditSession _session;
    private readonly IEditSessionWriter _writer;

    /// <summary>
    /// Imaging-domain port (module-boundary migration step 4) for cleaning a session's crop /
    /// mask temp files. Injected by the App; <c>null</c> in tests that do not exercise temp-file
    /// cleanup, so no imaging-domain implementation type is referenced from the UI.
    /// </summary>
    private readonly IImagingService? _imaging;

    /// <summary>
    /// The orchestration port (module-boundary migration step 6), attached by the App after both
    /// this view model and the <c>FlowRunner</c> exist (two-phase wiring). Required before any
    /// <see cref="SubmitAsync"/> / <see cref="RerunNodeAsync"/> / <see cref="CancelCurrent"/> call.
    /// </summary>
    private IEditFlowRunner? _flowRunner;

    public SessionViewModel(
        IEditSession session,
        IEditSessionWriter writer,
        IImagingService? imaging = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _imaging = imaging;
    }

    /// <summary>
    /// Attaches the App's flow runner (module-boundary migration step 6). Two-phase wiring: the
    /// runner holds this view model, so it is constructed after it and attached here.
    /// </summary>
    public void AttachFlowRunner(IEditFlowRunner runner)
        => _flowRunner = runner ?? throw new ArgumentNullException(nameof(runner));

    private IEditFlowRunner Runner()
        => _flowRunner ?? throw new InvalidOperationException(
            "IEditFlowRunner is not attached; call AttachFlowRunner before submitting / re-running.");

    /// <summary>The underlying session, exposed read-only (Step 9C.5: no Agent type crosses).</summary>
    public IEditSession Session => _session;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ObservableCollection<HistoryItem> History { get; } = new();

    public bool IsBusy { get; private set; }

    /// <summary>Sets the busy flag (module-boundary migration step 6: written by the flow runner).</summary>
    public void SetBusy(bool busy) => IsBusy = busy;

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
            _imaging?.CleanupSession(_session.SessionId);
            _writer.SetRoot(image);
        }

        RefreshHistory();
        RebuildContext();
    }

    /// <summary>
    /// Runs one user input end to end (delegates to the flow runner, module-boundary migration
    /// step 6): parse → execute → append a session node → refresh history.
    /// </summary>
    public Task<bool> SubmitAsync(
        string input,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyList<string>? additionalImages = null,
        string? displayText = null)
        => Runner().SubmitAsync(input, progress, ct, additionalImages, displayText);

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
    /// Whether the send button should be enabled (Step 9C.6-C). Delegates to the shared
    /// <see cref="ChatFlowRules.CanSend"/> so the UI gate and the flow runner agree.
    /// </summary>
    public bool CanSend(string? input, int attachmentCount)
        => ChatFlowRules.CanSend(input, attachmentCount, Mode);

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
    /// displayed pipeline image is unchanged.
    ///
    /// <para><b>R3.1:</b> the chat stream is deliberately <b>not</b> rebuilt here. The bubble
    /// overlay is refreshed once when the preview window closes (the caller's job), so a stroke
    /// end never rebuilds the stream. A no-op when the node is unknown.</para>
    /// </summary>
    public void SetNodeMask(string nodeId, MaskSpec? mask)
    {
        _writer.SetNodeMask(nodeId, mask);
        RefreshHistory();
    }

    /// <summary>
    /// Aligns the working node to the node the preview is showing before a mask stroke (E2=A).
    /// The parser reads the <b>current</b> node's mask, so the previewed node must become
    /// current or a drawn mask would silently not reach the pipeline. Returns <c>true</c> when
    /// the selection moved; a no-op (<c>false</c>) when the id is empty / unknown or already
    /// current. On a move a chat hint tells the user which node is now the mask target.
    /// </summary>
    public bool AlignForMask(string? previewNodeId)
    {
        if (string.IsNullOrEmpty(previewNodeId)
            || string.Equals(previewNodeId, _session.CurrentNodeId, StringComparison.Ordinal))
        {
            return false;
        }

        // NavigateTo rebuilds the chat (root → node path) and returns false for an unknown id.
        if (!NavigateTo(previewNodeId))
        {
            return false;
        }

        var shortId = previewNodeId.Length <= 8 ? previewNodeId : previewNodeId[..8];
        AddHint($"已切换到节点 {shortId} 以绘制遮罩");
        return true;
    }

    /// <summary>
    /// Aligns the working node to the node the preview is showing after a confirmed crop-tool
    /// outpaint (P1). The parser reads the <b>current</b> node's crop for <c>/扩图</c>, so the
    /// previewed node must become current or the crop would not reach the pipeline. Returns
    /// <c>true</c> when the selection moved; a no-op (<c>false</c>) when the id is empty /
    /// unknown or already current. On a move a chat hint tells the user which node is now the
    /// outpaint target.
    /// </summary>
    public bool AlignForCrop(string? previewNodeId)
    {
        if (string.IsNullOrEmpty(previewNodeId)
            || string.Equals(previewNodeId, _session.CurrentNodeId, StringComparison.Ordinal))
        {
            return false;
        }

        // NavigateTo rebuilds the chat (root → node path) and returns false for an unknown id.
        if (!NavigateTo(previewNodeId))
        {
            return false;
        }

        var shortId = previewNodeId.Length <= 8 ? previewNodeId : previewNodeId[..8];
        AddHint($"已切换到节点 {shortId} 以进行裁切外扩");
        return true;
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
    public void RebuildContext()
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
            ImagePath = ChatFlowRules.PipelinePath(path[0]),
            ImagePaths = ChatFlowRules.BuildDisplayPack(path[0]),
            MaskPath = path[0].Mask?.MaskImagePath,
            MaskFeatherPx = path[0].Mask?.FeatherPx ?? 0,
            NodeId = path[0].NodeId,
        });

        foreach (var node in path.Skip(1))
        {
            Messages.Add(new ChatMessage { Role = ChatRole.User, Text = node.Command });
            Messages.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = "完成",
                ImagePath = ChatFlowRules.PipelinePath(node),
                ImagePaths = new[] { ChatFlowRules.PipelinePath(node) },
                MaskPath = node.Mask?.MaskImagePath,
                MaskFeatherPx = node.Mask?.FeatherPx ?? 0,
                NodeId = node.NodeId,
            });
        }
    }

    /// <summary>
    /// Replaces the pending "生成中" bubble (tracked by identity) with the final message. If
    /// the context was rebuilt while generating and the bubble is gone, the final message is
    /// appended instead of throwing on a stale index. Called by the flow runner (step 6).
    /// </summary>
    public void ReplacePending(ChatMessage pending, ChatMessage replacement)
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
}
