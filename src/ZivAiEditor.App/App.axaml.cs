using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ZivAiEditor.App;

public partial class App : Application
{
    private AppContext? _context;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _context = AppContext.Create();
            desktop.MainWindow = new MainWindow(_context.Client);
            desktop.Exit += (_, _) => _context?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
