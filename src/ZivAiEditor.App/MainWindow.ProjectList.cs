using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.App.Controls;
using ZivAiEditor.UI;
using ZivAiEditor.UI.Projects;
using Path = Avalonia.Controls.Shapes.Path;

namespace ZivAiEditor.App;

/// <summary>
/// Project-list UI half of <see cref="MainWindow"/> (module-boundary migration step 3): the
/// left-pane list rendering, new / save buttons, Ctrl+S, the debounced open timer, and the
/// pending-mask flush. Split out of <c>MainWindow.Projects.cs</c> so the catalog-flow half stays
/// under the Z8 budget; the actual project catalog lives in the Agent's <c>ProjectService</c>.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush CurrentProjectBrush = new SolidColorBrush(Color.Parse("#3A3A3A"));

    private DispatcherTimer? _projectOpenTimer;
    private string? _pendingOpenId;

    private void InitProjects(LaunchOptions? options)
    {
        if (this.FindControl<Button>("PART_BtnNewProject") is { } newProject)
        {
            WindowDecorationProperties.SetElementRole(newProject, WindowDecorationsElementRole.User);
            newProject.Click += async (_, _) => await NewProjectAsync();
        }

        if (this.FindControl<Button>("PART_BtnSave") is { } save)
        {
            WindowDecorationProperties.SetElementRole(save, WindowDecorationsElementRole.User);
            save.Click += async (_, _) => await SaveCurrentAsync();
        }

        _projectOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _projectOpenTimer.Tick += OnProjectOpenTick;

        KeyDown += OnMainKeyDown;

        _savedSignature = SessionSignature.Compute(_session);
        _ = LoadStartupProjectAsync(options);
    }

    private async void OnMainKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            await SaveCurrentAsync();
        }
    }

    private async Task RefreshProjectListAsync()
    {
        if (this.FindControl<StackPanel>("PART_ProjectList") is not { } list)
        {
            return;
        }

        var projects = await _projects.ListAsync();
        list.Children.Clear();
        foreach (var project in projects)
        {
            list.Children.Add(BuildProjectRow(new ProjectListItem
            {
                SessionId = project.SessionId,
                Name = project.Name,
                IsCurrent = string.Equals(project.SessionId, _session.SessionId, StringComparison.Ordinal),
            }));
        }
    }

    private Control BuildProjectRow(ProjectListItem item)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Background = item.IsCurrent ? CurrentProjectBrush : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = item.Name,
            Foreground = item.IsCurrent ? Brushes.White : SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var remove = new Button
        {
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Path
            {
                Data = Geometry.Parse("M18 6l-12 12 M6 6l12 12"),
                Stroke = SecondaryTextBrush,
                StrokeThickness = 1.4,
                Width = 8,
                Height = 8,
                Stretch = Stretch.Uniform,
            },
        };
        ToolTip.SetTip(remove, "删除项目");
        remove.Click += async (_, e) =>
        {
            e.Handled = true;
            await DeleteProjectAsync(item.SessionId);
        };
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);

        row.Child = grid;
        row.Tapped += (_, e) =>
        {
            e.Handled = true;
            ScheduleOpen(item.SessionId);
        };
        row.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            CancelScheduledOpen();
            _ = RenameProjectAsync(item.SessionId, item.Name);
        };

        return row;
    }

    private void ScheduleOpen(string sessionId)
    {
        _pendingOpenId = sessionId;
        _projectOpenTimer?.Stop();
        _projectOpenTimer?.Start();
    }

    private void CancelScheduledOpen()
    {
        _projectOpenTimer?.Stop();
        _pendingOpenId = null;
    }

    private async void OnProjectOpenTick(object? sender, EventArgs e)
    {
        _projectOpenTimer?.Stop();
        if (_pendingOpenId is not { } sessionId)
        {
            return;
        }

        _pendingOpenId = null;
        try
        {
            await OpenProjectAsync(sessionId, askSave: true);
        }
        catch (Exception ex)
        {
            SetStatus($"打开项目失败：{ex.Message}");
        }
    }

    /// <summary>
    /// Awaits the preview's pending mask export, if any (Step 9C.7). Called before reading
    /// the dirty signature or saving, so a just-drawn mask is on disk. A no-op when no
    /// preview is open.
    /// </summary>
    private async Task FlushPendingMaskAsync()
    {
        if (_imagePreview is not null)
        {
            await _imagePreview.FlushMaskAsync();
        }
    }
}
