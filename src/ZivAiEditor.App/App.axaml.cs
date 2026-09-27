using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ZivAiEditor.UI;

namespace ZivAiEditor.App;

public partial class App : Application
{
    private AppContext? _context;

    /// <summary>Set by <see cref="Program"/> after setup; <c>null</c> in the designer.</summary>
    internal ShellService? Shell { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Idempotent; Program.Main already installs it on the normal path.
        CrashLog.Install();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = LaunchOptions.Parse(desktop.Args);

            _context = AppContext.Create(Shell!);
            var window = new MainWindow(
                _context.Session,
                _context.SessionWriter,
                _context.CommandParser,
                _context.Executor,
                _context.SessionStore,
                _context.Projects,
                _context.Imaging,
                Shell!,
                _context.ModelProfiles,
                options,
                _context.Commands,
                _context.PromptExpander,
                _context.LlmPreflight,
                _context.CreateExecutor,
                _context.PluginRegistry);
            desktop.MainWindow = window;

            // Backend preview frames (0x02) → pending bubble, marshalled to the UI thread.
            _context.PreviewReceived += bytes =>
                Dispatcher.UIThread.Post(() => window.ShowPreview(bytes));

            // L1/L2 recovery (Step 9C.20) → mark the in-flight bubble as restarted.
            _context.StuckRecovery += () =>
                Dispatcher.UIThread.Post(() => window.NotifyStuckRecovery());

            // A second instance forwards its request through the pipe; marshal to the UI thread.
            if (Shell is { } shell)
            {
                shell.LaunchRequested += request =>
                    Dispatcher.UIThread.Post(() => window.ApplyLaunchRequest(request));
            }

            desktop.Exit += (_, _) =>
            {
                // Step 9C.6-B2 / 9C.7 + 8K proxy cache: on exit every temp artifact under
                // _cache is orphaned, so clear them all — crop / mask per-session dirs and the
                // runtime display proxies. Saved projects under sessions/ are untouched. Never throws.
                if (_context is { } ctx)
                {
                    ctx.Imaging.CleanupAll();
                }

                _context?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
