using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.Contracts.Session;
using ZivAiEditor.UI;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.App;

/// <summary>
/// Bridge half of <see cref="MainWindow"/> (bridge §7.3): the in-editor quick path and the
/// engine-lock helpers. All ZIV ↔ ZIV.AI bridge logic lives under <c>MainWindow.*</c> so the rest
/// of the editor stays bridge-agnostic.
///
/// <para>R1: an in-editor quick task runs on a <b>temporary</b> session through an executor that
/// shares the process tools / queue / parser — it never calls <c>_vm.ApplyRequest</c> and never
/// touches the editor's own DAG.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>The engine lock path (bridge §5): <c>&lt;program dir&gt;/engine.lock</c>.</summary>
    private string EngineLockPath => Path.Combine(_shell.ProgramDirectory, "engine.lock");

    /// <summary>
    /// Runs a quick request forwarded to the running editor (bridge §2.1 / R1). Writes an
    /// <c>exited:false</c> notify for every outcome and keeps the window disabled while running.
    /// </summary>
    private async Task RunQuickInEditorAsync(LaunchOptions request)
    {
        var notifyPath = request.NotifyPath;

        void Notify(string status, string? outputPath, string? error)
            => NotifyWriter.TryWrite(notifyPath, new NotifyMessage
            {
                Status = status,
                SourceImage = request.ImagePath,
                Template = request.QuickTemplateId,
                OutputPath = outputPath,
                Error = error,
                Exited = false,
            });

        // Busy when an editor task is in flight or another process holds the engine lock.
        if (_vm.IsBusy || EngineLock.IsBusy(EngineLockPath))
        {
            Notify(NotifyStatus.Busy, request.OutputPath, "engine busy");
            SetStatus("AI 引擎忙，快捷任务未执行");
            return;
        }

        var engineLock = new EngineLock(EngineLockPath);
        if (!engineLock.TryAcquire())
        {
            Notify(NotifyStatus.Busy, request.OutputPath, "engine busy");
            SetStatus("AI 引擎忙，快捷任务未执行");
            return;
        }

        var previousStatus = this.FindControl<TextBlock>("PART_Status")?.Text;
        try
        {
            if (_createExecutor is null || _parser is null)
            {
                Notify(NotifyStatus.Error, request.OutputPath, "executor factory unavailable");
                return;
            }

            if (request.ImagePath is not { Length: > 0 } image
                || request.QuickTemplateId is not { Length: > 0 } template)
            {
                Notify(NotifyStatus.Error, request.OutputPath, "missing --image or --quick");
                return;
            }

            IsEnabled = false;
            SetStatus("快捷编辑进行中…");

            // R1: temp session + temp writer (same instance) + shared parser / tools / queue.
            var temp = new EditSession();
            temp.SetRoot(image);
            var executor = _createExecutor(temp, temp);

            var resolution = ResolveTier(request.Resolution);
            var parsed = await _parser
                .ParseAsync(template, temp, imageCount: 1, resolution, request.OutputPath, CancellationToken.None)
                .ConfigureAwait(true);
            if (!parsed.Success || parsed.Plan is null)
            {
                Notify(NotifyStatus.Error, request.OutputPath, parsed.ErrorMessage ?? "parse failed");
                return;
            }

            var state = await executor
                .ExecuteAsync(parsed.Plan, progress: null, CancellationToken.None)
                .ConfigureAwait(true);

            var resultPath = string.IsNullOrWhiteSpace(state.OutputImagePath)
                ? request.OutputPath
                : state.OutputImagePath;
            if (state.Status == TaskStatus.Succeeded)
            {
                Notify(NotifyStatus.Success, resultPath, null);
            }
            else if (state.Status == TaskStatus.Canceled)
            {
                Notify(NotifyStatus.Cancelled, resultPath, state.ErrorMessage);
            }
            else
            {
                Notify(NotifyStatus.Error, resultPath, state.ErrorMessage ?? "task failed");
            }
        }
        catch (Exception ex)
        {
            Notify(NotifyStatus.Error, request.OutputPath, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Release order (bridge §5): notify already written → temp executor/session dropped →
            // close the lock handle. IsEnabled is restored so the editor is usable again.
            engineLock.Dispose();
            IsEnabled = true;
            SetStatus(previousStatus is { Length: > 0 } ? previousStatus : "就绪");
        }
    }

    /// <summary>
    /// Resolves the CLI resolution tier text through the shared model profiles (bridge §7.2).
    /// Returns <c>null</c> for a missing / invalid / custom tier (backend default).
    /// </summary>
    private ResolutionPolicy? ResolveTier(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)
            || !Enum.TryParse<ResolutionTier>(tier, ignoreCase: true, out var parsed)
            || parsed == ResolutionTier.Custom)
        {
            return null;
        }

        return ResolutionResolver.FromTier(parsed, _modelProfiles.Default);
    }

    /// <summary>
    /// Takes the engine lock for an editor manual task (bridge §7.3): while held, the viewer's
    /// lock probe reports "busy". Returns <c>false</c> when another process holds it.
    /// </summary>
    private bool TryAcquireEngineLock()
    {
        _engineLock ??= new EngineLock(EngineLockPath);
        return _engineLock.TryAcquire();
    }

    /// <summary>Closes the manual-task lock handle (the file is left; existence is not the signal).</summary>
    private void ReleaseEngineLock() => _engineLock?.Release();
}
