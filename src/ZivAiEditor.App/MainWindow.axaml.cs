using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.App.Controls;
using ZivAiEditor.App.Flows;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.Contracts.Project;
using ZivAiEditor.Contracts.Session;
using ZivAiEditor.UI;
using ZivAiEditor.UI.Chat;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App;

/// <summary>
/// The ZIV.AI editor window (Step 9A): a self-drawn chrome, a left history-node list
/// and a chat stream. It renders the <see cref="SessionViewModel"/> and drives it only
/// through the frozen Agent contracts (the VM owns the parser / executor / session).
/// All platform work (window chrome, folder picker, bitmaps) lives here (Z4).
/// </summary>
public partial class MainWindow : Window
{
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#DDDDDD"));
    private static readonly IBrush SecondaryTextBrush = new SolidColorBrush(Color.Parse("#AAAAAA"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#E06C4A"));
    private static readonly IBrush UserBubbleBrush = new SolidColorBrush(Color.Parse("#2E2E2E"));
    private static readonly IBrush AssistantBubbleBrush = new SolidColorBrush(Color.Parse("#232323"));

    private readonly List<Bitmap> _bitmaps = new();

    /// <summary>
    /// Bumped on every <see cref="RenderChat"/>; a chat image's async mask-overlay load checks
    /// it so a late continuation cannot paint into a stream generation that has been replaced.
    /// </summary>
    private int _chatGeneration;

    private SessionViewModel _vm = null!;
    private FlowRunner _flow = null!;
    private ISessionPersistence _store = null!;
    private IProjectService _projects = null!;
    private IImagingService _imaging = null!;
    private ShellService _shell = null!;
    private IModelProfileRegistry _modelProfiles = null!;
    private readonly PluginRegistry? _plugins;
    private readonly ICommandTemplateService? _commandTemplates;
    private readonly LoraRegistry? _loraRegistry;
    private readonly AppContext? _appContext;
    private IEditSession _session = null!;
    private IEditSessionWriter _writer = null!;
    private IReadOnlyList<CommandDefinition> _commands = Array.Empty<CommandDefinition>();
    private readonly IPromptExpander? _promptExpander;
    private readonly ILlmPreflight? _llmPreflight;

    /// <summary>The deterministic parser, reused by the in-editor quick path with a temp session.</summary>
    private readonly ICommandParser? _parser;

    /// <summary>Builds a temp-session executor sharing the process tools / queue / parser (bridge §7.3).</summary>
    private readonly Func<IEditSession, IEditSessionWriter, IExecutor>? _createExecutor;

    /// <summary>Held while an editor manual task runs so the viewer's lock probe sees "busy".</summary>
    private EngineLock? _engineLock;

    private ImagePreview? _imagePreview;
    private bool _closing;

    /// <summary>
    /// Z-018: true while a project switch is closing the preview window. The preview's
    /// close-time chat rebuild is skipped because the switch reloads the session itself.
    /// </summary>
    private bool _switchingProject;

    private bool _suppressHistorySelection;

    /// <summary>The live preview Image inside the pending bubble, if any.</summary>
    private Image? _pendingPreviewImage;

    /// <summary>The bitmap currently shown in the pending bubble (owned; disposed on replace).</summary>
    private Bitmap? _pendingPreviewBitmap;

    /// <summary>Designer-only constructor; the runtime path uses the injected one.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    internal MainWindow(
        IEditSession session,
        IEditSessionWriter sessionWriter,
        ICommandParser commandParser,
        IExecutor executor,
        ISessionPersistence sessionStore,
        IProjectService projects,
        IImagingService imaging,
        ShellService shell,
        IModelProfileRegistry modelProfiles,
        LaunchOptions? launchOptions = null,
        IReadOnlyList<CommandDefinition>? commands = null,
        IPromptExpander? promptExpander = null,
        ILlmPreflight? llmPreflight = null,
        Func<IEditSession, IEditSessionWriter, IExecutor>? createExecutor = null,
        PluginRegistry? plugins = null,
        ICommandTemplateService? commandTemplates = null,
        LoraRegistry? loraRegistry = null,
        AppContext? appContext = null)
    {
        _store = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _imaging = imaging ?? throw new ArgumentNullException(nameof(imaging));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _modelProfiles = modelProfiles ?? throw new ArgumentNullException(nameof(modelProfiles));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _writer = sessionWriter ?? throw new ArgumentNullException(nameof(sessionWriter));
        _commands = commands ?? Array.Empty<CommandDefinition>();
        _promptExpander = promptExpander;
        _llmPreflight = llmPreflight;
        _parser = commandParser;
        _createExecutor = createExecutor;
        _plugins = plugins;
        _commandTemplates = commandTemplates;
        _loraRegistry = loraRegistry;
        _appContext = appContext;

        // Step 9C.5: the same session instance is passed as both the read-only view and the
        // writer (it implements IEditSession / IEditSessionWriter); the UI view model never
        // references the Agent implementation type.
        //
        // Module-boundary migration step 6: the view model holds only UI state; the cross-domain
        // orchestration lives in the App's FlowRunner. Two-phase wiring (the runner holds the
        // view model, which holds the runner): build the view model, then the runner, then attach.
        _vm = new SessionViewModel(session, sessionWriter, _imaging);
        _flow = new FlowRunner(
            _vm, session, sessionWriter, commandParser, executor,
            _promptExpander, _llmPreflight, _store.DeleteNodeArtifacts, _imaging);
        _vm.AttachFlowRunner(_flow);

        InitializeComponent();
        _shell.ApplyChrome(this);

        InitChat();
        InitImport();
        InitSend();
        InitCommandList();
        InitLora();

        // Z-030 复议: mirror a runtime command reload into the UI snapshot (the parser swaps in
        // place, so the Executor already sees it; this keeps suggestions / the LoRA panel fresh).
        if (_appContext is { } context)
        {
            context.CommandsReloaded += OnCommandsReloaded;
        }

        // B1: coalesce — a burst of Messages changes (e.g. RebuildContext's Clear + one Add per
        // node) must produce ONE re-render, not one full rebuild per change (which was O(n^2)
        // with a per-change bitmap dispose / re-decode).
        _vm.Messages.CollectionChanged += (_, _) => QueueChatRender();
        _vm.History.CollectionChanged += (_, _) => RenderHistory();
        _vm.Start(launchOptions);
        InitProjects(launchOptions);

        if (launchOptions?.Prompt is { Length: > 0 } prompt && FindInput() is { } input)
        {
            input.Text = prompt;
        }

        Closing += OnClosing;
        Closed += (_, _) =>
        {
            DisposeBitmaps();
            _importBar?.Dispose();
        };
    }

    /// <summary>
    /// Handles a request forwarded by a second instance: brings the window forward and loads the
    /// new request. A quick request runs the in-editor quick path and returns immediately (P1-A).
    /// </summary>
    public void ApplyLaunchRequest(LaunchOptions request)
    {
        if (_vm is null || request is null)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // P1-A / R1: a quick request must NEVER reach _vm.ApplyRequest — that would
        // _writer.SetRoot(image) and destroy the editor's current DAG. It takes the in-editor
        // quick path (temp session) instead, and this method returns at once.
        if (request.IsQuick)
        {
            _ = RunQuickInEditorAsync(request);
            return;
        }

        _ = ApplyEditorRequestAsync(request);
    }

    /// <summary>
    /// Applies a non-quick forwarded request (bridge §7.3, P2-C): when it carries an image, first
    /// look for a saved project whose <c>source_image</c> matches; a hit opens that project, a miss
    /// keeps the legacy <c>ApplyRequest</c> behavior.
    /// </summary>
    private async Task ApplyEditorRequestAsync(LaunchOptions request)
    {
        if (request.ImagePath is { Length: > 0 } image)
        {
            ProjectSummary? match;
            try
            {
                match = await _projects.FindBySourceImageAsync(PathNormalizer.Normalize(image));
            }
            catch (Exception)
            {
                match = null;
            }

            if (match is not null)
            {
                await OpenProjectAsync(match.SessionId, askSave: false);
                if (request.Prompt is { Length: > 0 } startupPrompt && FindInput() is { } startupInput)
                {
                    startupInput.Text = startupPrompt;
                }

                SetStatus("已接收新的编辑请求");
                return;
            }
        }

        _vm.ApplyRequest(request);

        if (request.Prompt is { Length: > 0 } prompt && FindInput() is { } input)
        {
            input.Text = prompt;
        }

        SetStatus("已接收新的编辑请求");
    }

    /// <summary>Maps the picker's tier to a resolution policy on the session view model.</summary>
    private void ApplyResolution(ResolutionPicker picker)
    {
        _vm.Resolution = picker.Tier == ResolutionTier.Custom
            ? null
            : ResolutionResolver.FromTier(picker.Tier, _modelProfiles.Default);
    }

    private TextBox? FindInput() => this.FindControl<TextBox>("PART_Input");

    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressHistorySelection || _vm is null)
        {
            return;
        }

        if (sender is not ListBox list || list.SelectedItem is not ListBoxItem { Tag: string nodeId })
        {
            return;
        }

        // Navigating rebuilds the history list. Mutating the ListBox's items while its
        // selection model is still processing this selection change re-enters the model
        // and throws (ItemsSourceView out-of-range) -> app crash. Defer to the dispatcher
        // so the selection change completes first.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_vm is null)
                {
                    return;
                }

                // R5 (M-4): switching history is a context change — exit the mask tool so a
                // later stroke cannot land on the stale preview node while the parser reads the
                // newly-current node. AlignForMask's own NavigateTo is unaffected (this reset
                // lives only on the history-click path, not in SessionViewModel.NavigateTo).
                if (_imagePreview?.ToolState.CurrentTool is ToolMode.MaskBrush or ToolMode.Eraser)
                {
                    _imagePreview.ToolState.SetTool(ToolMode.None);
                }

                _vm.NavigateTo(nodeId);
                list.SelectedItem = null;
                ScrollToEnd();
            },
            DispatcherPriority.Background);
    }

    private void RenderChat()
    {
        // B1: this render serves every change queued since the last one. Clear the flag FIRST
        // so a change made *during* this rebuild still queues the next render (never dropped);
        // the flag is only ever set by QueueChatRender.
        _chatRenderQueued = false;

        if (_vm is null || this.FindControl<StackPanel>("PART_ChatStream") is not { } stream)
        {
            return;
        }

        _chatRenderCount++;
        ReleaseBitmaps();
        _chatGeneration++;
        stream.Children.Clear();
        var hasPending = false;
        foreach (var message in _vm.Messages)
        {
            if (message.IsPending)
            {
                hasPending = true;
            }

            stream.Children.Add(BuildMessage(message));
        }

        // B14: with no in-flight bubble there is no label to receive status text. Forget the stale
        // label (the Clear above detached it) and the cached text, so a later SetStatus does not
        // write into a removed control.
        if (!hasPending)
        {
            _pendingTextLabel = null;
            _statusText = null;
        }

        ScrollToEnd();
    }

    /// <summary>Chat-bubble image size (70% of the former 320; the text is not scaled).</summary>
    private const double BubbleImageSize = 224;

    private Control BuildMessage(ChatMessage message)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = 420 };

        var rerunNodeId = message.Role == ChatRole.Assistant ? message.NodeId : null;
        var canRerun = rerunNodeId is { Length: > 0 } id && _vm.CanRerun(id);
        var hasAction = message.IsPending || canRerun;

        // The delete-X is only on a completed AI bubble whose node still has a parent (root /
        // T2I-first bubbles, and the in-flight bubble, get none).
        var deleteButton = !message.IsPending && canRerun && rerunNodeId is { Length: > 0 }
            ? BuildDeleteButton(rerunNodeId)
            : null;

        var textBlock = string.IsNullOrWhiteSpace(message.Text)
            ? null
            : new TextBlock
            {
                Text = message.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = message.IsError ? ErrorBrush : TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };

        // Progress / status is routed into the in-flight "生成中" bubble. B14: re-apply the cached
        // status text so a rebuild does not reset the bubble to its placeholder ("生成中…").
        if (message.IsPending)
        {
            _pendingTextLabel = textBlock;
            if (textBlock is not null && _statusText is not null)
            {
                textBlock.Text = _statusText;
            }
        }

        if (!hasAction)
        {
            if (textBlock is not null)
            {
                panel.Children.Add(textBlock);
            }

            AddMessageImages(panel, message);

            return BuildBubbleBorder(message, panel, deleteButton);
        }

        // Image(s) first, then one row with the status text and the action button
        // (Step 9C.8-B2 follow-up: the info text moves down next to the ×/regenerate button).
        AddMessageImages(panel, message);

        if (message.IsPending)
        {
            // The live preview target; ShowPreview fills it as 0x02 frames arrive.
            var preview = new Image
            {
                MaxWidth = BubbleImageSize,
                MaxHeight = BubbleImageSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = false,
            };
            _pendingPreviewImage = preview;
            panel.Children.Add(preview);
        }

        var action = BuildBubbleAction(message, canRerun ? rerunNodeId : null);
        panel.Children.Add(BuildActionRow(textBlock, action));

        return BuildBubbleBorder(message, panel, deleteButton);
    }

    private void RenderHistory()
    {
        if (_vm is null || this.FindControl<ListBox>("PART_HistoryList") is not { } list)
        {
            return;
        }

        _suppressHistorySelection = true;
        try
        {
            list.Items.Clear();
            foreach (var item in _vm.History)
            {
                list.Items.Add(BuildHistoryItem(item));
            }
        }
        finally
        {
            _suppressHistorySelection = false;
        }
    }

    private ListBoxItem BuildHistoryItem(HistoryItem item)
    {
        var label = string.IsNullOrWhiteSpace(item.Node.Command)
            ? item.Node.NodeId[..Math.Min(8, item.Node.NodeId.Length)]
            : item.Node.Command;

        // Step 9C.10-P2 (Q5=B): a multi-image root is labelled "原图（N 张）" as plain text.
        if (item.Node.ImagePaths.Count > 1)
        {
            label = $"{label}（{item.Node.ImagePaths.Count} 张）";
        }

        var listItem = new ListBoxItem
        {
            Tag = item.Node.NodeId,
            Content = new TextBlock
            {
                Text = label,
                Margin = new Thickness(item.Depth * 12, 0, 0, 0),
                Foreground = item.IsCurrent ? Brushes.White : SecondaryTextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };

        AttachRerunMenu(listItem, item);
        return listItem;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (FindInput() is { } input)
        {
            input.IsEnabled = !busy;
        }

        UpdateSendEnabled();

        // No "就绪" write: SetStatus only updates the pending bubble, and when not busy
        // there is none, so it would be a no-op (Step 9C.3-R #6).
        if (busy)
        {
            SetStatus("处理中…");
        }
    }

    private void ScrollToEnd()
    {
        if (this.FindControl<ScrollViewer>("PART_ChatScroll") is not { } scroll)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => scroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>Disposes every decoded bitmap and forgets the pending preview target.</summary>
    private void ReleaseBitmaps()
    {
        foreach (var bitmap in _bitmaps)
        {
            try { bitmap.Dispose(); } catch { }
        }

        _bitmaps.Clear();
        _pendingPreviewImage = null;
        try { _pendingPreviewBitmap?.Dispose(); } catch { }
        _pendingPreviewBitmap = null;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing || _vm is null || _store is null)
        {
            return;
        }

        // Step 9C.7 / S2: a just-released mask stroke may still be exporting. Cancel this
        // close once, await the window-level export, then re-enter so the dirty check sees the
        // written file. Independent of whether the preview window is still open.
        if (_pendingMaskExport is { IsCompleted: false } pendingMask)
        {
            e.Cancel = true;
            try
            {
                await pendingMask;
            }
            catch
            {
                // Exports swallow their own IO errors; a flush must never block the close.
            }

            Close();
            return;
        }

        // Nothing to save when the session is empty or unchanged (Step 9C.6-E).
        var hasContent = _vm.History.Count > 0 || !string.IsNullOrEmpty(_session.RootImagePath);
        if (!hasContent || !IsDirty)
        {
            return;
        }

        e.Cancel = true;
        _closing = true;

        try
        {
            if (await _shell.ConfirmAsync(this, "保存本次项目？"))
            {
                await SaveCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[close] {ex.Message}");
        }
        finally
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[close] {ex.Message}");
            }
        }
    }

    private void DisposeBitmaps()
    {
        ReleaseBitmaps();
    }
}
