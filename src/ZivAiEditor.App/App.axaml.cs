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
                _context.LlmPreflight);
            desktop.MainWindow = window;

            // Backend preview frames (0x02) → pending bubble, marshalled to the UI thread.
            _context.PreviewReceived += bytes =>
                Dispatcher.UIThread.Post(() => window.ShowPreview(bytes));

            // A second instance forwards its request through the pipe; marshal to the UI thread.
            if (Shell is { } shell)
            {
                shell.LaunchRequested += request =>
                    Dispatcher.UIThread.Post(() => window.ApplyLaunchRequest(request));
            }

            desktop.Exit += (_, _) =>
            {
                // Step 9C.6-B2: drop this session's crop temp files (the export, if any,
                // already copied them). Never throws.
                if (_context is { } ctx)
                {
                    // Step 9C.6-B2 / 9C.7: drop the session's crop + mask temp files
                    // (the export, if any, already copied them). Never throws.
                    ctx.Imaging.CleanupSession(ctx.Session.SessionId);
                }

                _context?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
