using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
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
    private ISessionExporter _exporter = null!;
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
        ISessionExporter sessionExporter,
        LaunchOptions? launchOptions = null)
    {
        _exporter = sessionExporter ?? throw new ArgumentNullException(nameof(sessionExporter));
        _vm = new SessionViewModel(session, commandParser, executor);

        InitializeComponent();
        InitChrome();
        InitChat();

        _vm.Messages.CollectionChanged += (_, _) => RenderChat();
        _vm.History.CollectionChanged += (_, _) => RenderHistory();
        _vm.Start(launchOptions);

        if (launchOptions?.Prompt is { Length: > 0 } prompt && FindInput() is { } input)
        {
            input.Text = prompt;
        }

        Closing += OnClosing;
        Closed += (_, _) => DisposeBitmaps();
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

    private void InitChrome()
    {
        var titleBar = this.FindControl<Border>("PART_TitleBar");
        var btnMinimize = this.FindControl<Button>("PART_BtnMinimize");
        var btnMaximize = this.FindControl<Button>("PART_BtnMaximize");
        var btnClose = this.FindControl<Button>("PART_BtnClose");

        // Avalonia 12 chrome roles: treat the self-drawn elements as the real caption.
        if (titleBar is not null)
        {
            WindowDecorationProperties.SetElementRole(titleBar, WindowDecorationsElementRole.TitleBar);
        }

        SetRole(btnMinimize, WindowDecorationsElementRole.MinimizeButton);
        SetRole(btnMaximize, WindowDecorationsElementRole.MaximizeButton);
        SetRole(btnClose, WindowDecorationsElementRole.CloseButton);

        if (btnMinimize is not null)
        {
            btnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        if (btnMaximize is not null)
        {
            btnMaximize.Click += (_, _) => WindowState =
                WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        if (btnClose is not null)
        {
            btnClose.Click += (_, _) => Close();
        }

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMaximizeIcon();
            }
        };

        UpdateMaximizeIcon();
    }

    private static void SetRole(Button? button, WindowDecorationsElementRole role)
    {
        if (button is not null)
        {
            WindowDecorationProperties.SetElementRole(button, role);
        }
    }

    private void UpdateMaximizeIcon()
    {
        var icon = this.FindControl<Avalonia.Controls.Shapes.Path>("PART_IconMaximize");
        if (icon is null)
        {
            return;
        }

        icon.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M0 3H7V10H0Z M3 0H10V7H3Z"
            : "M0 0H10V10H0Z");
    }

    private void InitChat()
    {
        if (this.FindControl<Button>("PART_BtnSend") is { } send)
        {
            send.Click += (_, _) => _ = SubmitAsync();
        }

        if (FindInput() is { } input)
        {
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    _ = SubmitAsync();
                }
            };
        }

        if (this.FindControl<ListBox>("PART_HistoryList") is { } history)
        {
            history.SelectionChanged += OnHistorySelectionChanged;
        }
    }

    private TextBox? FindInput() => this.FindControl<TextBox>("PART_Input");

    private async Task SubmitAsync()
    {
        if (_vm is null || FindInput() is not { } input)
        {
            return;
        }

        var text = input.Text ?? "";
        if (string.IsNullOrWhiteSpace(text) || _vm.IsBusy)
        {
            return;
        }

        input.Text = "";
        SetBusy(true);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progress = new Progress<TaskProgress>(OnProgress);
        try
        {
            await _vm.SubmitAsync(text, progress, _cts.Token);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
            ScrollToEnd();
        }
    }

    private void OnProgress(TaskProgress progress)
    {
        var fraction = progress.Fraction > 0 ? $" {progress.Fraction:P0}" : "";
        SetStatus($"{progress.Message}{fraction}");
    }

    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressHistorySelection || _vm is null)
        {
            return;
        }

        if (sender is ListBox list && list.SelectedItem is ListBoxItem { Tag: string nodeId })
        {
            _vm.NavigateTo(nodeId);
            list.SelectedItem = null;
            ScrollToEnd();
        }
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
            panel.Children.Add(new TextBlock
            {
                Text = message.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = message.IsError ? ErrorBrush : TextBrush,
            });
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
            panel.Children.Add(new Image
            {
                Source = bitmap,
                MaxWidth = 320,
                MaxHeight = 320,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
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
        if (FindInput() is { } input)
        {
            input.IsEnabled = !busy;
        }

        if (this.FindControl<Button>("PART_BtnSend") is { } send)
        {
            send.IsEnabled = !busy;
        }

        SetStatus(busy ? "处理中…" : "就绪");
    }

    private void SetStatus(string text)
    {
        if (this.FindControl<TextBlock>("PART_Status") is { } status)
        {
            status.Text = text;
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
        if (_closing || _vm is null || _exporter is null)
        {
            return;
        }

        // An empty session has nothing to save; close without prompting.
        var hasContent = _vm.History.Count > 0 || !string.IsNullOrEmpty(_vm.Session.RootImagePath);
        if (!hasContent)
        {
            return;
        }

        e.Cancel = true;
        _closing = true;

        try
        {
            var save = await ConfirmDialog.ShowAsync(this, "保存本次会话？");
            if (save == true && await PickFolderAsync() is { Length: > 0 } directory)
            {
                // A failed export returns null and must not block the close (INTERACTION.md §4).
                await _exporter.ExportAsync(_vm.Session, directory);
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

    private async Task<string?> PickFolderAsync()
    {
        var storage = StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择会话保存目录",
            AllowMultiple = false,
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private void DisposeBitmaps()
    {
        ReleaseBitmaps();
        _cts?.Dispose();
    }
}
