using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZivAiEditor.Agent;
using ZivAiEditor.App.Controls;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.UI;
using ZivAiEditor.UI.Chat;

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

    private SessionViewModel _vm = null!;
    private SessionStore _store = null!;
    private IModelProfileRegistry _modelProfiles = null!;
    private EditSession _session = null!;
    private ImagePreview? _imagePreview;
    private CancellationTokenSource? _cts;
    private bool _closing;
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

    public MainWindow(
        EditSession session,
        ICommandParser commandParser,
        IExecutor executor,
        SessionStore sessionStore,
        IModelProfileRegistry modelProfiles,
        LaunchOptions? launchOptions = null)
    {
        _store = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _modelProfiles = modelProfiles ?? throw new ArgumentNullException(nameof(modelProfiles));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        // Step 9C.5: the same EditSession instance is passed as both the read-only
        // session view and the writer (it implements IEditSession / IEditSessionWriter);
        // the UI view model never references the Agent implementation type.
        _vm = new SessionViewModel(session, session, commandParser, executor);

        InitializeComponent();
        if (this.FindControl<ChromeTitleBar>("PART_Chrome") is { } chrome)
        {
            ChromeBehavior.Init(this, chrome);
        }

        InitChat();
        InitImport();
        InitSend();

        _vm.Messages.CollectionChanged += (_, _) => RenderChat();
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
    /// Handles a request forwarded by a second instance: brings the window forward and
    /// loads the new request into the session.
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
        _vm.ApplyRequest(request);

        if (request.Prompt is { Length: > 0 } prompt && FindInput() is { } input)
        {
            input.Text = prompt;
        }

        SetStatus("已接收新的编辑请求");
    }

    private void InitChat()
    {
        if (this.FindControl<Button>("PART_BtnSend") is { } send)
        {
            send.Click += (_, _) => _ = SubmitAsync();
        }

        if (FindInput() is { } input)
        {
            // Multi-line input: Enter sends, Shift+Enter inserts a newline. Intercept on
            // the TUNNEL phase, because with AcceptsReturn the TextBox's own class handler
            // consumes Enter (inserting a newline) before the bubbling KeyDown reaches us.
            input.AddHandler(
                InputElement.KeyDownEvent,
                (_, e) =>
                {
                    if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        e.Handled = true;
                        _ = SubmitAsync();
                    }
                },
                RoutingStrategies.Tunnel);
        }

        if (this.FindControl<ListBox>("PART_HistoryList") is { } history)
        {
            history.SelectionChanged += OnHistorySelectionChanged;
        }

        // Title-bar sidebar button toggles the history pane (Step 9C.3 UI pass).
        // Marked "User" so the OS treats it as client content inside the caption
        // (otherwise the title-bar hit-test swallows the click).
        if (this.FindControl<Button>("PART_BtnToggleSidebar") is { } toggleSidebar
            && this.FindControl<Border>("PART_HistoryPane") is { } historyPane)
        {
            WindowDecorationProperties.SetElementRole(toggleSidebar, WindowDecorationsElementRole.User);
            toggleSidebar.Click += (_, _) => historyPane.IsVisible = !historyPane.IsVisible;
        }

        // Resolution tier picker (Step 6.5 logic, first UI): selection feeds the plan.
        if (this.FindControl<ResolutionPicker>("PART_ResolutionPicker") is { } picker)
        {
            picker.Attach(_modelProfiles.Default);
            picker.SelectionChanged += (_, _) => ApplyResolution(picker);
            ApplyResolution(picker);
        }
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

                _vm.NavigateTo(nodeId);
                list.SelectedItem = null;
                ScrollToEnd();
            },
            DispatcherPriority.Background);
    }

    private void RenderChat()
    {
        if (_vm is null || this.FindControl<StackPanel>("PART_ChatStream") is not { } stream)
        {
            return;
        }

        ReleaseBitmaps();
        stream.Children.Clear();
        foreach (var message in _vm.Messages)
        {
            stream.Children.Add(BuildMessage(message));
        }

        ScrollToEnd();
    }

    private Control BuildMessage(ChatMessage message)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = 420 };

        panel.Children.Add(new TextBlock
        {
            Text = message.Role switch
            {
                ChatRole.User => "你",
                ChatRole.Assistant => "AI",
                _ => "系统",
            },
            Foreground = SecondaryTextBrush,
            FontSize = 12,
        });

        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            var textBlock = new TextBlock
            {
                Text = message.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = message.IsError ? ErrorBrush : TextBrush,
            };

            // Progress / status is routed into the in-flight "生成中" bubble.
            if (message.IsPending)
            {
                _pendingTextLabel = textBlock;
            }

            panel.Children.Add(textBlock);
        }

        if (message.ImagePath is { Length: > 0 } path)
        {
            AddPreview(panel, path);
        }

        if (message.IsPending)
        {
            // The live preview target; ShowPreview fills it as 0x02 frames arrive.
            var preview = new Image
            {
                MaxWidth = 320,
                MaxHeight = 320,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsVisible = false,
            };
            _pendingPreviewImage = preview;
            panel.Children.Add(preview);
        }

        return new Border
        {
            Background = message.Role == ChatRole.User ? UserBubbleBrush : AssistantBubbleBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            HorizontalAlignment = message.Role == ChatRole.User
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            Child = panel,
        };
    }

    private void AddPreview(Panel panel, string path)
    {
        try
        {
            var bitmap = new Bitmap(path);
            _bitmaps.Add(bitmap);

            // Clicking a chat image opens the standalone large-image preview window.
            var image = new Image
            {
                Source = bitmap,
                MaxWidth = 320,
                MaxHeight = 320,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            image.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                OpenImagePreview(path);
            };
            ToolTip.SetTip(image, "点击查看大图");

            panel.Children.Add(image);
        }
        catch (Exception ex)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"[预览失败] {ex.Message}",
                Foreground = ErrorBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }
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

    private static ListBoxItem BuildHistoryItem(HistoryItem item)
    {
        var label = string.IsNullOrWhiteSpace(item.Node.Command)
            ? item.Node.NodeId[..Math.Min(8, item.Node.NodeId.Length)]
            : item.Node.Command;

        return new ListBoxItem
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

    /// <summary>
    /// Renders a live preview JPEG (a backend <c>0x02</c> frame) into the pending
    /// bubble. Called on the UI thread by the App wiring; ignored when no bubble is
    /// pending or the bytes cannot be decoded. Does not rebuild the chat stream, so
    /// frequent frames only update one Image (no flicker).
    /// </summary>
    public void ShowPreview(byte[] jpegBytes)
    {
        if (_pendingPreviewImage is null || jpegBytes is null || jpegBytes.Length == 0)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(jpegBytes);
            var bitmap = new Bitmap(stream);
            _pendingPreviewBitmap?.Dispose();
            _pendingPreviewBitmap = bitmap;
            _pendingPreviewImage.Source = bitmap;
            _pendingPreviewImage.IsVisible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[preview] {ex.Message}");
        }
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
            var save = await ConfirmDialog.ShowAsync(this, "保存本次项目？");
            if (save == true)
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
        _cts?.Dispose();
    }
}
