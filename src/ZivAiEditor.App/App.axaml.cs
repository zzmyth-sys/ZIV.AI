using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ZivAiEditor.Backend;
using ZivAiEditor.Diagnostics;
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

            // LoRA unified management: seed template-private weights (owner=model in loras.json)
            // into the unified directory, in the background / best-effort. Plugin-owned weights are
            // seeded by their plugins at first use; public (owner=none) weights by the manager plugin.
            Task.Run(() => SeedModelLoras(Shell!.LoadSettings(), _context.Loras));

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
                _context.PluginRegistry,
                _context.CommandTemplates,
                _context.Loras,
                _context);
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

    /// <summary>
    /// Copies every logged <c>owner=model</c> LoRA origin (from <c>Template/loras.json</c>) into
    /// the unified directory when the destination is missing. Never throws; a missing source /
    /// copy failure is logged to <see cref="DiagLog"/> only.
    /// </summary>
    private static void SeedModelLoras(BackendSettings settings, LoraRegistry registry)
    {
        // Target the SAME unified dir the backend loads from (settings.LoraRoot), not a
        // re-derivation from ComfyRoot — an explicit lora_root override must be honoured.
        var loraRoot = settings.LoraRoot;
        if (string.IsNullOrWhiteSpace(loraRoot))
        {
            return;
        }

        foreach (var entry in registry.All)
        {
            if (!string.Equals(entry.Owner, "model", StringComparison.Ordinal))
            {
                continue;
            }

            // source/path split (2026-09-30): copy `source` (origin) to `<loraRoot>/<basename(path)>`
            // so the unified-relative `path` (used to load) resolves.
            var source = entry.Source;
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(entry.Path)
                ? Path.GetFileName(entry.Path!)
                : Path.GetFileName(source);

            try
            {
                var dest = Path.Combine(loraRoot!, name);
                if (File.Exists(dest))
                {
                    continue;
                }

                if (!File.Exists(source))
                {
                    DiagLog.Log($"lora seed missing (model): {source}");
                    continue;
                }

                Directory.CreateDirectory(loraRoot!);
                File.Copy(source, dest, overwrite: false);
                DiagLog.Log($"lora seeded (model): {dest}");
            }
            catch (Exception ex)
            {
                DiagLog.Log($"lora seed failed (model): {source}: {ex.Message}");
            }
        }
    }
}
