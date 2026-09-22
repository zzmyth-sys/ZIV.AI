using System.Collections.ObjectModel;
using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
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
    public EditNode Node { get; init; } = null!;

    public int Depth { get; init; }

    public bool IsCurrent { get; init; }
}

/// <summary>
/// Chat-session view model (Step 9A): it drives the in-memory <see cref="EditSession"/>
/// through the frozen Agent contracts only — <see cref="ICommandParser"/> to turn input
/// into an <c>EditPlan</c> and <see cref="IExecutor"/> to run it. It never touches
/// <c>IInferenceClient</c> (ARCHITECTURE.md §4: the UI talks to the Agent layer through
/// contracts; inference is the tool's job).
///
/// The class has no Avalonia dependency so the chat flow is unit-testable; the App
/// layer renders <see cref="Messages"/> / <see cref="History"/> into controls.
/// </summary>
public sealed class SessionViewModel
{
    private const int MaxTreeDepth = 4096;

    private readonly EditSession _session;
    private readonly ICommandParser _parser;
    private readonly IExecutor _executor;

    public SessionViewModel(EditSession session, ICommandParser parser, IExecutor executor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <summary>The underlying session (read-only for the UI except through this VM).</summary>
    public EditSession Session => _session;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ObservableCollection<HistoryItem> History { get; } = new();

    public bool IsBusy { get; private set; }

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
            _session.SetRoot(image);
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

        var parsed = await _parser.ParseAsync(text, _session, ct);
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
        try
        {
            var state = await _executor.ExecuteAsync(parsed.Plan, progress, ct);
            if (state.Status == TaskStatus.Succeeded && !string.IsNullOrWhiteSpace(state.OutputImagePath))
            {
                _session.AppendNode(parentId, state.OutputImagePath, text);
                Messages[pendingIndex] = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = "完成",
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
        if (!_session.NavigateTo(nodeId))
        {
            return false;
        }

        RefreshHistory();
        RebuildContext();
        return true;
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
                Depth = DepthOf(node),
                IsCurrent = string.Equals(node.NodeId, _session.CurrentNodeId, StringComparison.Ordinal),
            });
        }
    }

    /// <summary>Clears the chat and replays the path from the root image to the current node.</summary>
    private void RebuildContext()
    {
        Messages.Clear();

        if (_session.RootImagePath is { Length: > 0 } root)
        {
            Messages.Add(new ChatMessage { Role = ChatRole.System, Text = "起始图像", ImagePath = root });
        }

        foreach (var node in PathToCurrent())
        {
            Messages.Add(new ChatMessage { Role = ChatRole.User, Text = node.Command });
            Messages.Add(new ChatMessage { Role = ChatRole.Assistant, Text = "完成", ImagePath = node.ImagePath });
        }
    }

    private IReadOnlyList<EditNode> PathToCurrent()
    {
        var path = new List<EditNode>();
        var id = _session.CurrentNodeId;
        var guard = 0;
        while (!string.IsNullOrEmpty(id)
               && _session.Nodes.TryGetValue(id, out var node)
               && guard++ < MaxTreeDepth)
        {
            path.Add(node);
            id = node.ParentNodeId;
        }

        path.Reverse();
        return path;
    }

    private int DepthOf(EditNode node)
    {
        var depth = 0;
        var id = node.ParentNodeId;
        var guard = 0;
        while (!string.IsNullOrEmpty(id)
               && _session.Nodes.TryGetValue(id, out var parent)
               && guard++ < MaxTreeDepth)
        {
            depth++;
            id = parent.ParentNodeId;
        }

        return depth;
    }

    private static string BuildFailureMessage(TaskState state)
        => state.ErrorMessage
           ?? (state.Status == TaskStatus.Canceled ? "已取消。" : $"执行未成功（{state.Status}）。");
}
