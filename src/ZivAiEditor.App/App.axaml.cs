using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ZivAiEditor.Diagnostics;
using ZivAiEditor.UI;

namespace ZivAiEditor.App;

public partial class App : Application
{
    private AppContext? _context;

    /// <summary>
    /// Bootstrap seeds for **template-private** LoRAs: copies each known weight into the unified
    /// directory (<c>&lt;comfy_root&gt;/models/loras</c>) on launch so the relative filename
    /// declared in <c>commands.json</c> resolves. Best-effort: a missing source is a diagnostic
    /// only (never blocks). Public LoRAs are seeded by their plugins at first use.
    /// </summary>
    private static readonly (string Source, string Name)[] PrivateLoraSeeds =
    {
        (@"C:\AI\ComfyUI_PIC\ComfyUI\models\loras\qwen_image2\bfs_head_v1.1_qwen_2.1.safetensors",
         "bfs_head_v1.1_qwen_2.1.safetensors"),
    };

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

            // LoRA unified management: seed template-private weights into the unified directory
            // (background, best-effort). Public weights are seeded by their plugins at first use.
            Task.Run(() => SeedPrivateLoras(Shell!.LoadSettings()));

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
    /// Copies each <see cref="PrivateLoraSeeds"/> weight into the unified LoRA directory when the
    /// destination is missing. Never throws; a missing source / copy failure is logged to
    /// <see cref="DiagLog"/> only.
    /// </summary>
    private static void SeedPrivateLoras(BackendSettings settings)
    {
        var loraRoot = ComfyDiscovery.DeriveLoraRoot(settings.ComfyRoot);
        if (string.IsNullOrWhiteSpace(loraRoot))
        {
            return;
        }

        foreach (var (source, name) in PrivateLoraSeeds)
        {
            try
            {
                var dest = Path.Combine(loraRoot!, name);
                if (File.Exists(dest))
                {
                    continue;
                }

                if (!File.Exists(source))
                {
                    DiagLog.Log($"lora seed missing (private): {source}");
                    continue;
                }

                Directory.CreateDirectory(loraRoot!);
                File.Copy(source, dest, overwrite: false);
                DiagLog.Log($"lora seeded (private): {dest}");
            }
            catch (Exception ex)
            {
                DiagLog.Log($"lora seed failed (private): {source}: {ex.Message}");
            }
        }
    }
}
