using System;
using System.IO;
using Avalonia;
using ZivAiEditor.UI;

namespace ZivAiEditor.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // CLI entry point (SPEC §3.6). Parsing is lenient: bad / missing flags never
        // abort the launch — the app starts with a default session.
        var options = LaunchOptions.Parse(args);

        // Module-boundary migration step 5: the shell facade owns the single-instance guard
        // (created here, before Avalonia; disposed when the app exits).
        using var shell = new ShellService();

        // A later process hands its request to the running one, then exits without a window.
        // Bridge §2.1: a failed forward of a quick request reports error instead of leaving ZIV
        // waiting; the headless path never starts a second Python backend.
        if (!shell.IsFirstInstance)
        {
            if (!shell.SendToExistingInstance(options))
            {
                NotifyWriter.TryWrite(options.NotifyPath, new NotifyMessage
                {
                    Status = NotifyStatus.Error,
                    SourceImage = options.ImagePath,
                    Template = options.QuickTemplateId,
                    OutputPath = options.OutputPath,
                    Error = "forward failed",
                    Exited = true,
                });
            }

            return;
        }

        // First instance + quick: run headless and exit (bridge §2.1). Detection happens AFTER the
        // ShellService (SingleInstance) is created, never before.
        if (options.IsQuick)
        {
            var lockPath = Path.Combine(shell.ProgramDirectory, "engine.lock");
            using var engineLock = new EngineLock(lockPath);
            Environment.ExitCode = HeadlessQuickRunner
                .RunAsync(options, shell, engineLock, () => new QuickRunHost(AppContext.Create(shell)))
                .GetAwaiter()
                .GetResult();
            return;
        }

        var builder = BuildAvaloniaApp();
        builder.AfterSetup(b =>
        {
            if (b.Instance is App app)
            {
                app.Shell = shell;
            }
        });

        builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
