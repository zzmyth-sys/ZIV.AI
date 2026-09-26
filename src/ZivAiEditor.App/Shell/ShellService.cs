using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ZivAiEditor.App.Controls;
using ZivAiEditor.UI;

namespace ZivAiEditor.App;

/// <summary>
/// Shell-domain facade (module-boundary migration step 5): the App-layer platform services —
/// single instance, file pickers, dialogs (confirm / prompt), window chrome, program / template
/// paths and <c>settings.ini</c> — behind one entry point. The controls, dialogs,
/// <see cref="SingleInstance"/> and <see cref="SettingsLoader"/> stay as the domain's internals
/// (foldered under <c>Shell/</c>); App code goes through this facade.
///
/// <para>Lives in the App layer (not the UI domain) because it depends on Avalonia
/// <see cref="Window"/>. Owns a <see cref="SingleInstance"/> whose lifetime is created in
/// <c>Program.Main</c> (before Avalonia) and disposed when the app exits.</para>
///
/// <para>Deliberately <b>not</b> included yet: URL-protocol registration and a folder picker
/// (no implementation / caller exists — Q3). The specialized choice dialogs
/// (<c>MultiImagePromptDialog</c> / <c>PromptConfirmDialog</c>) remain direct (Q4).</para>
/// </summary>
internal sealed class ShellService : IShellContext, IDisposable
{
    private readonly SingleInstance _single;
    private bool _disposed;

    public ShellService()
    {
        _single = new SingleInstance();
        _single.PathReceived += request => LaunchRequested?.Invoke(request);
    }

    /// <summary>True when this process owns the single-instance mutex (the first instance).</summary>
    public bool IsFirstInstance => _single.IsFirstInstance;

    /// <summary>Raised (on the listener thread) for each request handed over by a later process.</summary>
    public event Action<LaunchOptions>? LaunchRequested;

    /// <summary>The program directory (Z14: data ships with the program).</summary>
    public string ProgramDirectory => System.AppContext.BaseDirectory;

    /// <summary>
    /// The <c>Template/</c> directory (commands.json), resolved from the program directory and
    /// falling back to walking up to the repository root (mirrors the old AppContext resolver).
    /// </summary>
    public string TemplateDirectory
    {
        get
        {
            var program = Path.Combine(ProgramDirectory, "Template");
            if (File.Exists(Path.Combine(program, "commands.json")))
            {
                return program;
            }

            var directory = new DirectoryInfo(ProgramDirectory);
            while (directory is not null)
            {
                var repository = Path.Combine(directory.FullName, "Template");
                if (File.Exists(Path.Combine(repository, "commands.json"))
                    && File.Exists(Path.Combine(directory.FullName, "DOC", "FROZEN.md")))
                {
                    return repository;
                }

                directory = directory.Parent;
            }

            return program;
        }
    }

    /// <summary>Reads the program-directory <c>settings.ini</c> (Z14).</summary>
    public BackendSettings LoadSettings() => SettingsLoader.Load();

    /// <summary>Hands a launch request to the running first instance (the second-instance path).</summary>
    public bool SendToExistingInstance(LaunchOptions options) => _single.SendToExistingInstance(options);

    /// <summary>Picks one or more image files. Returns an empty array when cancelled / unavailable.</summary>
    public async Task<string[]> PickImagesAsync(Window owner, CancellationToken ct = default)
    {
        var storage = owner.StorageProvider;
        if (storage is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择图片",
                AllowMultiple = true,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            });

            return files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Cast<string>()
                .ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[shell] pick images failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Shows a "save as" picker (PNG) starting in <paramref name="suggestedDirectory"/> when it
    /// exists. Returns the chosen local path, or <c>null</c> when cancelled / unavailable.
    /// </summary>
    public async Task<string?> PickSaveFileAsync(
        Window owner,
        string suggestedName,
        string? suggestedDirectory = null,
        CancellationToken ct = default)
    {
        var storage = owner.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(suggestedDirectory) && Directory.Exists(suggestedDirectory))
        {
            start = await storage.TryGetFolderFromPathAsync(new Uri(suggestedDirectory));
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为",
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = start,
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType("PNG 图像") { Patterns = new[] { "*.png" } } },
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>Shows a yes / no confirmation. Only an explicit "yes" returns <c>true</c>.</summary>
    public async Task<bool> ConfirmAsync(Window owner, string message)
        => await ConfirmDialog.ShowAsync(owner, message) == true;

    /// <summary>Shows a single-line text prompt. Returns the trimmed input, or <c>null</c> on cancel.</summary>
    public Task<string?> PromptAsync(Window owner, string title, string initial)
        => TextPromptDialog.ShowAsync(owner, title, initial);

    /// <summary>Applies the custom window chrome behaviour to a window that has a PART_Chrome.</summary>
    public void ApplyChrome(Window window)
    {
        if (window.FindControl<ChromeTitleBar>("PART_Chrome") is { } chrome)
        {
            ChromeBehavior.Init(window, chrome);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _single.Dispose();
    }
}
