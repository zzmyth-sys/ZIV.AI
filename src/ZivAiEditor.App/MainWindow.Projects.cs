using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Session;
using ZivAiEditor.UI;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App;

/// <summary>
/// Project-flow half of <see cref="MainWindow"/> (Step 9C.6-E; module-boundary migration
/// step 3): startup auto-open, new / save / delete / rename / switch, and the dirty check. The
/// project <b>catalog</b> lives in the Agent's <c>ProjectService</c> (<c>_projects</c>); the
/// session <b>content</b> save / load stays on <c>SessionStore</c> (<c>_store</c>). The
/// left-pane list UI is in <c>MainWindow.ProjectList.cs</c>.
/// </summary>
public partial class MainWindow
{
    private string _projectName = ProjectNaming.Unnamed;
    private string? _savedSignature;
    private bool _opening;

    /// <summary>True when the session differs from the last saved / loaded snapshot.</summary>
    private bool IsDirty => !string.Equals(_savedSignature, SessionSignature.Compute(_session), StringComparison.Ordinal);

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

            var lastId = _projects.GetLastProjectId();
            if (string.IsNullOrWhiteSpace(lastId))
            {
                return;
            }

            if (!Directory.Exists(_projects.GetDirectory(lastId)))
            {
                await _projects.SetLastProjectIdAsync(null);
                return;
            }

            await OpenProjectAsync(lastId, askSave: false);
        }
        catch (Exception ex)
        {
            SetStatus($"加载项目失败：{ex.Message}");
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

            // Z-018: flush the export, exit the mask tool and close the preview window before
            // the session is swapped; the main window is brought forward.
            await ResetTransientUiAsync();

            // Decision A: clean the OLD session's crop temp files BEFORE Restore rewrites SessionId.
            var oldId = _session.SessionId;
            if (!string.Equals(oldId, sessionId, StringComparison.Ordinal))
            {
                _imaging.CleanupSession(oldId); // Step 9C.7
            }

            _writer.Restore(
                result.Session.GetHistory(),
                result.Session.CurrentNodeId,
                result.Session.SessionId,
                result.Session.CreatedAt);
            _projectName = result.Name;
            _vm.Reload();
            _savedSignature = SessionSignature.Compute(_session);
            await _projects.SetLastProjectIdAsync(sessionId);
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

        // Z-018: the confirm dialog is modal to the main window only; disable the preview
        // window while the prompt is up so it cannot be edited behind the dialog.
        if (_imagePreview is { } preview)
        {
            preview.IsEnabled = false;
        }

        bool save;
        try
        {
            save = await _shell.ConfirmAsync(this, "保存当前项目？");
        }
        finally
        {
            if (_imagePreview is { } restored)
            {
                restored.IsEnabled = true;
            }
        }

        if (!save)
        {
            return true;
        }

        return await SaveCurrentAsync();
    }

    /// <summary>
    /// Z-018: drops the transient per-project UI before the session is swapped — flushes the
    /// pending mask export, exits the mask tool, closes the standalone preview window and brings
    /// the main window forward. Aborting paths (busy / save failed / load failed) must not call
    /// this.
    /// </summary>
    private async Task ResetTransientUiAsync()
    {
        await FlushPendingMaskAsync();

        if (_imagePreview is { } preview)
        {
            // Null the field first so a re-entrant switch skips a second close.
            _imagePreview = null;
            _switchingProject = true;
            try
            {
                preview.ToolState.SetTool(ToolMode.None);
                preview.IsEnabled = false; // 防模态期间误操作
                preview.Close();
            }
            finally
            {
                _switchingProject = false;
            }
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
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
        // Z-018: flush / exit the mask tool / close the preview before clearing the project.
        await ResetTransientUiAsync();

        _imaging.CleanupSession(_session.SessionId); // Step 9C.7
        _writer.NewSession();
        _projectName = ProjectNaming.Unnamed;
        _vm.Reload();
        _savedSignature = SessionSignature.Compute(_session);
        await _projects.SetLastProjectIdAsync(null);
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
            var name = ProjectNaming.EffectiveName(_projectName, _session.RootImagePath);
            await _store.SaveAsync(_session, name);
            _projectName = name;
            _savedSignature = SessionSignature.Compute(_session);
            await _projects.SetLastProjectIdAsync(_session.SessionId);
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
        if (!await _shell.ConfirmAsync(this, "删除该项目？该项目的所有文件将被删除。"))
        {
            return;
        }

        var deletingCurrent = string.Equals(sessionId, _session.SessionId, StringComparison.Ordinal);
        await _projects.DeleteAsync(sessionId);

        if (deletingCurrent)
        {
            // Z-018: deleting the current project swaps the session, so drop the transient UI.
            await ResetTransientUiAsync();

            var projects = await _projects.ListAsync();
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
        var name = await _shell.PromptAsync(this, "重命名项目", currentName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (string.Equals(sessionId, _session.SessionId, StringComparison.Ordinal))
        {
            _projectName = name;
        }

        await _projects.RenameAsync(sessionId, name);
        await RefreshProjectListAsync();
    }
}
