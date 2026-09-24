using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ZivAiEditor.Agent;
using ZivAiEditor.App.Controls;
using ZivAiEditor.UI;
using ZivAiEditor.UI.Imaging;
using ZivAiEditor.UI.Projects;
using Path = Avalonia.Controls.Shapes.Path;

namespace ZivAiEditor.App;

/// <summary>
/// Project-list half of <see cref="MainWindow"/> (Step 9C.6-E): the left-pane project list,
/// new / delete / rename / switch, save (button + Ctrl+S) and startup auto-open. Split out
/// of the main file to keep each file under the Z8 budget.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush CurrentProjectBrush = new SolidColorBrush(Color.Parse("#3A3A3A"));
    private const string UnnamedProject = "未命名";

    private string _projectName = UnnamedProject;
    private string? _savedSignature;
    private DispatcherTimer? _projectOpenTimer;
    private string? _pendingOpenId;
    private bool _opening;

    /// <summary>True when the session differs from the last saved / loaded snapshot.</summary>
    private bool IsDirty => !string.Equals(_savedSignature, ComputeSignature(), StringComparison.Ordinal);

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

        _savedSignature = ComputeSignature();
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

    private async Task LoadStartupProjectAsync(LaunchOptions? options)
    {
        try
        {
            await RefreshProjectListAsync();

            // A CLI image is an explicit request; it wins over the remembered project.
            if (options?.ImagePath is { Length: > 0 })
            {
                return;
            }

            var lastId = _store.GetLastProjectId();
            if (string.IsNullOrWhiteSpace(lastId))
            {
                return;
            }

            if (!Directory.Exists(_store.GetProjectDirectory(lastId)))
            {
                await _store.SetLastProjectIdAsync(null);
                return;
            }

            await OpenProjectAsync(lastId, askSave: false);
        }
        catch (Exception ex)
        {
            SetStatus($"加载项目失败：{ex.Message}");
        }
    }

    private async Task RefreshProjectListAsync()
    {
        if (this.FindControl<StackPanel>("PART_ProjectList") is not { } list)
        {
            return;
        }

        var projects = await _store.ListAsync();
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

    private async Task<bool> OpenProjectAsync(string sessionId, bool askSave)
    {
        if (_vm.IsBusy || _opening)
        {
            return false;
        }

        if (string.Equals(sessionId, _session.SessionId, StringComparison.Ordinal))
        {
            return true;
        }

        if (askSave && !await AskSaveIfDirtyAsync())
        {
            return false;
        }

        _opening = true;
        try
        {
            SessionLoadResult result;
            try
            {
                result = await _store.LoadAsync(sessionId);
            }
            catch (ProjectFormatException ex)
            {
                _vm.AddHint($"无法打开项目：{ex.Message}");
                ScrollToEnd();
                return false;
            }
            catch (ProjectCorruptException ex)
            {
                _vm.AddHint($"项目损坏：{ex.Message}");
                ScrollToEnd();
                return false;
            }
            catch (Exception ex)
            {
                SetStatus($"打开项目失败：{ex.Message}");
                return false;
            }

            // Step 9C.7: let any in-flight mask export land before its temp dir is cleaned.
            await FlushPendingMaskAsync();

            // Decision A: clean the OLD session's crop temp files BEFORE Restore rewrites SessionId.
            var oldId = _session.SessionId;
            if (!string.Equals(oldId, sessionId, StringComparison.Ordinal))
            {
                ImageCropper.CleanupSession(oldId);
                MaskExporter.CleanupSession(oldId); // Step 9C.7
            }

            _session.Restore(
                result.Session.GetHistory(),
                result.Session.CurrentNodeId,
                result.Session.SessionId,
                result.Session.CreatedAt);
            _projectName = result.Name;
            _vm.Reload();
            _savedSignature = ComputeSignature();
            await _store.SetLastProjectIdAsync(sessionId);
            await RefreshProjectListAsync();

            foreach (var warning in result.Warnings)
            {
                _vm.AddHint(warning);
            }

            SetStatus($"已打开项目：{_projectName}");
            ScrollToEnd();
            return true;
        }
        finally
        {
            _opening = false;
        }
    }

    private async Task<bool> AskSaveIfDirtyAsync()
    {
        // Step 9C.7: a mask export is written off-thread; flush it before reading the
        // dirty signature so a just-drawn mask is neither missed nor half-written.
        await FlushPendingMaskAsync();

        var hasContent = _vm.History.Count > 0 || !string.IsNullOrEmpty(_session.RootImagePath);
        if (!hasContent || !IsDirty)
        {
            return true;
        }

        var save = await ConfirmDialog.ShowAsync(this, "保存当前项目？");
        return save != true || await SaveCurrentAsync();
    }

    private async Task NewProjectAsync()
    {
        if (_vm.IsBusy)
        {
            return;
        }

        if (!await AskSaveIfDirtyAsync())
        {
            return;
        }

        await ResetToEmptyProjectAsync();
        SetStatus("已新建空项目");
    }

    private async Task ResetToEmptyProjectAsync()
    {
        // Step 9C.7: let any in-flight mask export land before its temp dir is cleaned.
        await FlushPendingMaskAsync();

        ImageCropper.CleanupSession(_session.SessionId);
        MaskExporter.CleanupSession(_session.SessionId); // Step 9C.7
        var fresh = new EditSession();
        _session.Restore(fresh.GetHistory(), fresh.CurrentNodeId, fresh.SessionId, fresh.CreatedAt);
        _projectName = UnnamedProject;
        _vm.Reload();
        _savedSignature = ComputeSignature();
        await _store.SetLastProjectIdAsync(null);
        await RefreshProjectListAsync();
    }

    private async Task<bool> SaveCurrentAsync()
    {
        if (_vm.IsBusy)
        {
            return false;
        }

        // Step 9C.7: the project save copies each node's mask temp PNG, so the pending
        // export must land on disk first (otherwise SessionStore.CopyIfNeeded silently
        // skips a not-yet-written file and the reload would drop the mask).
        await FlushPendingMaskAsync();

        try
        {
            var name = EffectiveProjectName();
            await _store.SaveAsync(_session, name);
            _projectName = name;
            _savedSignature = ComputeSignature();
            await _store.SetLastProjectIdAsync(_session.SessionId);
            await RefreshProjectListAsync();
            SetStatus("已保存项目");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败：{ex.Message}");
            return false;
        }
    }

    private async Task DeleteProjectAsync(string sessionId)
    {
        var confirm = await ConfirmDialog.ShowAsync(this, "删除该项目？该项目的所有文件将被删除。");
        if (confirm != true)
        {
            return;
        }

        var deletingCurrent = string.Equals(sessionId, _session.SessionId, StringComparison.Ordinal);
        await _store.DeleteAsync(sessionId);

        if (deletingCurrent)
        {
            var projects = await _store.ListAsync();
            if (projects.Count > 0)
            {
                await OpenProjectAsync(projects[0].SessionId, askSave: false);
            }
            else
            {
                await ResetToEmptyProjectAsync();
            }
        }

        await RefreshProjectListAsync();
        SetStatus("已删除项目");
    }

    private async Task RenameProjectAsync(string sessionId, string currentName)
    {
        var name = await TextPromptDialog.ShowAsync(this, "重命名项目", currentName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (string.Equals(sessionId, _session.SessionId, StringComparison.Ordinal))
        {
            _projectName = name;
        }

        await _store.RenameAsync(sessionId, name);
        await RefreshProjectListAsync();
    }

    /// <summary>
    /// The name to persist: an explicit / renamed name, else the root image's file name
    /// (the confirmed default), else "未命名" (Step 9C.6-E).
    /// </summary>
    private string EffectiveProjectName()
    {
        if (!string.IsNullOrWhiteSpace(_projectName)
            && !string.Equals(_projectName, UnnamedProject, StringComparison.Ordinal))
        {
            return _projectName;
        }

        var root = _session.RootImagePath;
        if (!string.IsNullOrWhiteSpace(root))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(root);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return UnnamedProject;
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

    private string ComputeSignature()
    {
        var builder = new StringBuilder();
        builder.Append(_session.SessionId).Append('|').Append(_session.CurrentNodeId).Append('|');
        foreach (var node in _session.GetHistory())
        {
            builder.Append(node.NodeId).Append(':').Append(node.ImagePath).Append(':');
            if (node.Crop is { } crop)
            {
                // The crop result path is geometry-independent (per-node file), so the
                // rectangle must be part of the signature or a re-crop would look clean.
                builder.Append(crop.X).Append(',').Append(crop.Y).Append(',')
                       .Append(crop.Width).Append(',').Append(crop.Height).Append(',')
                       .Append(crop.ResultImagePath);
            }

            if (node.Mask is { } mask)
            {
                // Step 9C.7: a mask adds no node, so without this a mask-only edit would
                // look clean and the close prompt would not fire. The per-node file is
                // overwritten in place, so its write stamp + length stand in for content.
                builder.Append(mask.MaskImagePath).Append(',').Append(mask.Width).Append(',')
                       .Append(mask.Height);
                AppendFileStamp(builder, mask.MaskImagePath);
            }

            builder.Append(';');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends a file's last-write ticks + length as a cheap content proxy (Step 9C.7).
    /// A missing / unreadable file contributes nothing (treated as absent).
    /// </summary>
    private static void AppendFileStamp(StringBuilder builder, string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var info = new FileInfo(path);
            builder.Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(':').Append(info.Length);
        }
        catch (Exception)
        {
            // Unreadable stamp is not fatal — the mask path itself is already in the signature.
        }
    }
}
