using System;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.UI;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.App;

/// <summary>
/// The seam between <see cref="HeadlessQuickRunner"/> and the editor composition (bridge §7.2,
/// P1-8): the runner orchestrates lock / notify / error handling while the host performs the
/// session, parser and executor work. Production wraps <see cref="AppContext"/>; tests inject a
/// fake so the control flow is verifiable without starting Python or touching the GPU (Z30).
/// </summary>
internal interface IQuickRunHost : IDisposable
{
    /// <summary>Resets the (temporary) session root to <paramref name="imagePath"/>.</summary>
    void SetRoot(string imagePath);

    /// <summary>Resolves the CLI resolution tier text (<c>fast</c> / <c>balanced</c> / <c>high_quality</c>).</summary>
    ResolutionPolicy? ResolveResolution(string? tier);

    /// <summary>Parses the quick template with the pinned absolute output path.</summary>
    Task<ParseResult> ParseAsync(
        string template,
        string? outputPath,
        ResolutionPolicy? resolution,
        CancellationToken ct);

    /// <summary>Executes the parsed plan.</summary>
    Task<TaskState> ExecuteAsync(EditPlan plan, CancellationToken ct);
}

/// <summary>
/// Production <see cref="IQuickRunHost"/> over a freshly-built <see cref="AppContext"/> (bridge
/// §7.2): shares the process-wide inference client and owns the context's lifetime via
/// <see cref="Dispose"/>. The runner creates one per quick task.
/// </summary>
internal sealed class QuickRunHost : IQuickRunHost
{
    private readonly AppContext _context;

    public QuickRunHost(AppContext context) => _context = context;

    public void SetRoot(string imagePath) => _context.SessionWriter.SetRoot(imagePath);

    public ResolutionPolicy? ResolveResolution(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)
            || !Enum.TryParse<ResolutionTier>(tier, ignoreCase: true, out var parsed)
            || parsed == ResolutionTier.Custom)
        {
            return null;
        }

        return ResolutionResolver.FromTier(parsed, _context.ModelProfiles.Default);
    }

    public Task<ParseResult> ParseAsync(
        string template,
        string? outputPath,
        ResolutionPolicy? resolution,
        CancellationToken ct)
        => _context.CommandParser.ParseAsync(
            template,
            _context.Session,
            imageCount: 1,
            resolution,
            outputPath,
            ct);

    public Task<TaskState> ExecuteAsync(EditPlan plan, CancellationToken ct)
        => _context.Executor.ExecuteAsync(plan, progress: null, ct);

    public void Dispose() => _context.Dispose();
}

/// <summary>
/// Headless quick-edit orchestration (bridge §2.1 / §7.2). It owns the engine lock, writes the
/// notify document for every outcome, and subscribes to <c>LaunchRequested</c> while running so a
/// second ZIV that forwards a quick request gets an immediate <c>busy</c> instead of waiting.
/// </summary>
internal static class HeadlessQuickRunner
{
    public static async Task<int> RunAsync(
        LaunchOptions options,
        IShellContext shell,
        EngineLock engineLock,
        Func<IQuickRunHost> hostFactory)
    {
        var startedAt = Environment.TickCount64;
        var source = options.ImagePath;
        var template = options.QuickTemplateId ?? "";
        var output = options.OutputPath;
        var notifyPath = options.NotifyPath;

        void Notify(string status, string? outputPath, string? error)
            => NotifyWriter.TryWrite(notifyPath, new NotifyMessage
            {
                Status = status,
                SourceImage = source,
                Template = template,
                OutputPath = outputPath,
                Error = error,
                ElapsedMs = Environment.TickCount64 - startedAt,
                Exited = true,
            });

        // P1-4: while this headless process owns the engine, a forwarded quick request (a later
        // ZIV instance) must get an immediate busy notify rather than an empty wait.
        void OnForwarded(LaunchOptions request)
        {
            if (string.IsNullOrWhiteSpace(request.NotifyPath))
            {
                return;
            }

            NotifyWriter.TryWrite(request.NotifyPath, new NotifyMessage
            {
                Status = NotifyStatus.Busy,
                SourceImage = request.ImagePath,
                Template = request.QuickTemplateId,
                OutputPath = request.OutputPath,
                Error = "engine busy",
                Exited = true,
            });
        }

        var subscribed = false;
        try
        {
            if (!engineLock.TryAcquire())
            {
                Notify(NotifyStatus.Busy, output, "engine busy");
                return 1;
            }

            shell.LaunchRequested += OnForwarded;
            subscribed = true;

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(template))
            {
                Notify(NotifyStatus.Error, output, "missing --image or --quick");
                return 1;
            }

            // The host (and its AppContext → Python backend) is built only after the lock is held,
            // so a losing process never starts a second backend (bridge §2.1 / P2).
            using var host = hostFactory();
            host.SetRoot(source);

            var resolution = host.ResolveResolution(options.Resolution);
            var parsed = await host.ParseAsync(template, output, resolution, CancellationToken.None)
                .ConfigureAwait(false);
            if (!parsed.Success || parsed.Plan is null)
            {
                Notify(NotifyStatus.Error, output, parsed.ErrorMessage ?? "parse failed");
                return 1;
            }

            var state = await host.ExecuteAsync(parsed.Plan, CancellationToken.None).ConfigureAwait(false);
            var resultPath = string.IsNullOrWhiteSpace(state.OutputImagePath) ? output : state.OutputImagePath;
            if (state.Status == TaskStatus.Succeeded)
            {
                Notify(NotifyStatus.Success, resultPath, null);
                return 0;
            }

            if (state.Status == TaskStatus.Canceled)
            {
                Notify(NotifyStatus.Cancelled, resultPath, state.ErrorMessage);
                return 1;
            }

            Notify(NotifyStatus.Error, resultPath, state.ErrorMessage ?? "task failed");
            return 1;
        }
        catch (Exception ex)
        {
            Notify(NotifyStatus.Error, output, $"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            if (subscribed)
            {
                shell.LaunchRequested -= OnForwarded;
            }

            // Release order (bridge §5): notify already written, host (AppContext) disposed above,
            // now close the handle and try to delete the file.
            engineLock.Dispose();
        }
    }
}
