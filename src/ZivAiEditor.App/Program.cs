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

        using var single = new SingleInstance();

        // A later process hands its request to the running one, then exits without a window.
        if (!single.IsFirstInstance)
        {
            single.SendToExistingInstance(options);
            return;
        }

        var builder = BuildAvaloniaApp();
        builder.AfterSetup(b =>
        {
            if (b.Instance is App app)
            {
                app.SingleInstance = single;
            }
        });

        builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
