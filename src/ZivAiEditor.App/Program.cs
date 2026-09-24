using System;
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
        if (!shell.IsFirstInstance)
        {
            shell.SendToExistingInstance(options);
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
