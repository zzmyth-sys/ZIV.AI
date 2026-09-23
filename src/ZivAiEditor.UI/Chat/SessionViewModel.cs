using System.Collections.ObjectModel;
using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.UI.Chat;

/// <summary>Who produced a chat message.</summary>
public enum ChatRole
{
    User,
    Assistant,
    System,
}

/// <summary>One rendered line in the chat stream (INTERACTION.md §3).</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }

    public string Text { get; init; } = "";

    /// <summary>Optional preview image (a user input or an edit output — Z24 new file).</summary>
    public string? ImagePath { get; init; }

    public bool IsError { get; init; }

    /// <summary>
    /// True for the in-flight "生成中…" bubble. The App layer renders live
    /// preview frames (0x02 IPC frames) into this bubble while the executor runs.
    /// </summary>
    public bool IsPending { get; init; }
}

/// <summary>One entry in the history list, carrying its tree depth for indentation.</summary>
public sealed class HistoryItem
{
    public IEditNode Node { get; init; } = null!;

    public int Depth { get; init; }

    public bool IsCurrent { get; init; }
}

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
public sealed class SessionViewModel
{
    private readonly IEditSession _session;
    private readonly IEditSessionWriter _writer;
    private readonly ICommandParser _parser;
    private readonly IExecutor _executor;

    public SessionViewModel(
        IEditSession session,
        IEditSessionWriter writer,
        ICommandParser parser,
        IExecutor executor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
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
        CancellationToken ct = default)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0 || IsBusy)
        {
            return false;
        }

        Messages.Add(new ChatMessage { Role = ChatRole.User, Text = text });

        var parsed = await _parser.ParseAsync(text, _session, Resolution, ct);
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

        // The pending bubble is replaced in place once the executor returns.
        var pendingIndex = Messages.Count;
        Messages.Add(new ChatMessage { Role = ChatRole.Assistant, Text = "生成中…", IsPending = true });
        IsBusy = true;

        var parentId = _session.CurrentNodeId;

        // End-to-end wall-clock: click-to-bubble-replacement (Step 9C.3 收尾 10). This is
        // intentionally distinct from the backend's InferenceResultDetail.DurationMs
        // (sampling + VAE decode only) and ToolResult.Duration (one IPC submit, incl.
        // lazy load / queue) — the three are not interchangeable (Step 9C.3-R #2).
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var plan = parsed.Plan;

        try
        {
            var state = await _executor.ExecuteAsync(plan, progress, ct);
            var elapsed = stopwatch.Elapsed;
            if (state.Status == TaskStatus.Succeeded && !string.IsNullOrWhiteSpace(state.OutputImagePath))
            {
                _writer.AppendNode(parentId, state.OutputImagePath, text);
                Messages[pendingIndex] = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = $"{elapsed.TotalSeconds:F1}秒 完成",
                    ImagePath = state.OutputImagePath,
                };
                RefreshHistory();
                return true;
            }

            Messages[pendingIndex] = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Text = BuildFailureMessage(state),
                IsError = true,
            };
            return false;
        }
        catch (OperationCanceledException)
        {
            Messages[pendingIndex] = new ChatMessage { Role = ChatRole.Assistant, Text = "已取消。", IsError = true };
            return false;
        }
        catch (Exception ex)
        {
            Messages[pendingIndex] = new ChatMessage { Role = ChatRole.Assistant, Text = ex.Message, IsError = true };
            return false;
        }
        finally
        {
            IsBusy = false;
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

    /// <summary>
    /// Sets the starting image from an import and resets the session (Step 9C.3):
    /// changing the root invalidates the existing node DAG, so nodes / current node are
    /// cleared and the chat is rebuilt. A blank path is a no-op. Unlike
    /// <see cref="ApplyRequest"/> this is an explicit in-session import, not a startup /
    /// second-instance handoff.
    /// </summary>
    public void SetRootImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        _writer.ResetToRoot(imagePath);
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

    /// <summary>Clears the chat and replays the path from the root image to the current node.</summary>
    private void RebuildContext()
    {
        Messages.Clear();

        if (_session.RootImagePath is { Length: > 0 } root)
        {
            Messages.Add(new ChatMessage { Role = ChatRole.System, Text = "起始图像", ImagePath = root });
        }

        foreach (var node in _session.GetPathToCurrent())
        {
            Messages.Add(new ChatMessage { Role = ChatRole.User, Text = node.Command });
            Messages.Add(new ChatMessage { Role = ChatRole.Assistant, Text = "完成", ImagePath = node.ImagePath });
        }
    }

    private static string BuildFailureMessage(TaskState state)
        => state.ErrorMessage
           ?? (state.Status == TaskStatus.Canceled ? "已取消。" : $"执行未成功（{state.Status}）。");
}
