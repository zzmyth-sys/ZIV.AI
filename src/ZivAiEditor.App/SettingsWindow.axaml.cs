using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ZivAiEditor.Backend;

namespace ZivAiEditor.App;

/// <summary>
/// Modal settings window. The environment tab is split into: the three-piece model set (DiT /
/// TE / VAE, the only user-editable weights), read-only auto-discovery fields (ComfyUI root,
/// python.exe, the shipped main.py, output / input), and a collapsed "advanced" section whose
/// python.exe / comfy_root / lora_root overrides persist to <c>settings.ini</c> and win over
/// discovery. The plugin tab opens by default. main.py is never editable (it ships with the
/// version); a custom <c>[backend] script</c> is ignored and only warned about.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly IShellContext _shell = null!;
    private readonly string _settingsPath = null!;
    private readonly string _pluginsPath = null!;
    private PluginRegistry _plugins = null!;
    private bool _comfyRootEditable;
    private bool _wizardShown;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    internal SettingsWindow(IShellContext shell)
        : this(shell, plugins: null)
    {
    }

    internal SettingsWindow(IShellContext shell, PluginRegistry? plugins)
        : this()
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _settingsPath = Path.Combine(System.AppContext.BaseDirectory, SettingsLoader.FileName);
        _pluginsPath = Path.Combine(shell.TemplateDirectory, "plugins.json");
        _plugins = plugins ?? new PluginRegistry(_pluginsPath);

        if (this.FindControl<Border>("PART_Header") is { } header)
        {
            header.PointerPressed += (_, e) => BeginMoveDrag(e);
        }

        WireBrowse("PART_BrowseDit", "PART_DitPath", "选择模型权重");
        WireBrowse("PART_BrowseTe", "PART_TePath", "选择模型权重");
        WireBrowse("PART_BrowseVae", "PART_VaePath", "选择模型权重");
        WireOpen("PART_OpenDit", "PART_DitPath");
        WireOpen("PART_OpenTe", "PART_TePath");
        WireOpen("PART_OpenVae", "PART_VaePath");
        WirePython();
        WireComfy();
        WireLoraRoot();
        WireFooter();
        WirePublicLoras();

        var settings = _shell.LoadSettings();
        Prefill(settings);
        InitDiscovery(settings);
        UpdateOpenEnabled();
        InitPlugins();

        Opened += OnWindowOpened;
    }

    private void WireBrowse(string buttonName, string textName, string title)
    {
        if (this.FindControl<Button>(buttonName) is not { } button)
        {
            return;
        }

        button.Click += async (_, _) =>
        {
            if (this.FindControl<TextBox>(textName) is not { } target)
            {
                return;
            }

            var picked = await _shell.PickFileAsync(this, title, SuggestedDirectory(target.Text));
            if (!string.IsNullOrEmpty(picked))
            {
                target.Text = picked;
                UpdateOpenEnabled();
            }
        };
    }

    private void WireOpen(string buttonName, string textName)
    {
        if (this.FindControl<Button>(buttonName) is not { } button)
        {
            return;
        }

        button.Click += (_, _) =>
        {
            var directory = SuggestedDirectory(Text(textName));
            if (!string.IsNullOrEmpty(directory))
            {
                _shell.OpenFolder(directory);
            }
        };
    }

    private void UpdateOpenEnabled()
    {
        SetOpenEnabled("PART_OpenDit", "PART_DitPath");
        SetOpenEnabled("PART_OpenTe", "PART_TePath");
        SetOpenEnabled("PART_OpenVae", "PART_VaePath");
    }

    private void SetOpenEnabled(string buttonName, string textName)
    {
        if (this.FindControl<Button>(buttonName) is { } button)
        {
            button.IsEnabled = !string.IsNullOrEmpty(SuggestedDirectory(Text(textName)));
        }
    }

    /// <summary>Advanced override: python.exe (auto-derived in the main area; overriding is opt-in).</summary>
    private void WirePython()
    {
        if (this.FindControl<Button>("PART_BrowsePython") is { } browse)
        {
            browse.Click += async (_, _) =>
            {
                if (this.FindControl<TextBox>("PART_PythonExeOverride") is not { } target)
                {
                    return;
                }

                var directory = await _shell.PickFolderAsync(this, "选择 Python 目录", SuggestedDirectory(target.Text));
                if (!string.IsNullOrEmpty(directory))
                {
                    target.Text = Path.Combine(directory, "python.exe");
                }
            };
        }

        if (this.FindControl<Button>("PART_ClearPython") is { } clear)
        {
            clear.Click += (_, _) =>
            {
                if (this.FindControl<TextBox>("PART_PythonExeOverride") is { } target)
                {
                    target.Text = string.Empty;
                }
            };
        }
    }

    /// <summary>Advanced override: comfy_root. Empty falls back to auto-discovery.</summary>
    private void WireComfy()
    {
        if (this.FindControl<Button>("PART_BrowseComfy") is { } browse)
        {
            browse.Click += async (_, _) =>
            {
                if (this.FindControl<TextBox>("PART_ComfyRootOverride") is not { } target)
                {
                    return;
                }

                var directory = await _shell.PickFolderAsync(this, "选择 ComfyUI 目录", SuggestedDirectory(target.Text));
                if (!string.IsNullOrEmpty(directory))
                {
                    target.Text = directory;
                }
            };
        }

        if (this.FindControl<Button>("PART_ClearComfy") is { } clear)
        {
            clear.Click += (_, _) =>
            {
                if (this.FindControl<TextBox>("PART_ComfyRootOverride") is { } target)
                {
                    target.Text = string.Empty;
                }
            };
        }
    }

    private void WireLoraRoot()
    {
        if (this.FindControl<Button>("PART_BrowseLoraRoot") is { } browse)
        {
            browse.Click += async (_, _) =>
            {
                if (this.FindControl<TextBox>("PART_LoraRoot") is not { } target)
                {
                    return;
                }

                var directory = await _shell.PickFolderAsync(this, "选择 LoRA 根目录", SuggestedDirectory(target.Text));
                if (!string.IsNullOrEmpty(directory))
                {
                    target.Text = directory;
                }
            };
        }

        if (this.FindControl<Button>("PART_ClearLoraRoot") is { } clear)
        {
            clear.Click += (_, _) =>
            {
                if (this.FindControl<TextBox>("PART_LoraRoot") is { } target)
                {
                    target.Text = string.Empty;
                }
            };
        }
    }

    private void WireFooter()
    {
        if (this.FindControl<Button>("PART_OpenTemplate") is { } open)
        {
            open.Click += (_, _) => _shell.OpenFolder(_shell.TemplateDirectory);
        }

        if (this.FindControl<Button>("PART_Cancel") is { } cancel)
        {
            cancel.Click += (_, _) => Close();
        }

        if (this.FindControl<Button>("PART_Save") is { } save)
        {
            save.Click += async (_, _) => await SaveAsync();
        }
    }

    /// <summary>The only editable weights: the three-piece set.</summary>
    private void Prefill(BackendSettings settings)
    {
        SetText("PART_DitPath", settings.DitPath);
        SetText("PART_TePath", settings.TePath);
        SetText("PART_VaePath", settings.VaePath);
    }

    /// <summary>
    /// Fills the read-only auto-discovery fields, the advanced overrides (persisted values only)
    /// and the derived labels. comfy_root becomes editable only when discovery failed.
    /// </summary>
    private void InitDiscovery(BackendSettings settings)
    {
        _comfyRootEditable = ComfyDiscovery.ShouldPromptForComfyRoot(settings.ComfyRoot);

        SetText("PART_ComfyRoot", settings.ComfyRoot);
        SetText("PART_PythonExe", settings.PythonExe);
        SetText("PART_Script", settings.Script);

        if (this.FindControl<TextBox>("PART_ComfyRoot") is { } comfyBox)
        {
            comfyBox.IsReadOnly = !_comfyRootEditable;
        }

        SetText("PART_PythonExeOverride", settings.ConfiguredPythonExe);
        SetText("PART_ComfyRootOverride", settings.ConfiguredComfyRoot);
        SetText("PART_LoraRoot", settings.LoraRoot);

        SetLabel("PART_ModelRootLabel",
            "models: " + (ComfyDiscovery.DeriveModelRoot(settings.ComfyRoot) ?? "(未发现 ComfyUI，无法推导)"));
        SetLabel("PART_LoraRootLabel",
            "loras: " + (ComfyDiscovery.DeriveLoraRoot(settings.ComfyRoot) ?? "(未发现 ComfyUI，无法推导)"));

        RefreshPublicLorasDisplay(settings);
        RefreshOwnedLorasDisplay(settings);

        if (!string.IsNullOrWhiteSpace(settings.IgnoredScript)
            && this.FindControl<TextBlock>("PART_ScriptWarning") is { } warning)
        {
            warning.Text = "已忽略自定义 main.py（随版本发布，不可修改）：" + settings.IgnoredScript;
            warning.IsVisible = true;
        }
    }

    /// <summary>The weight extensions the public scan accepts; identical to Python <c>_PUBLIC_EXTS</c>.</summary>
    internal static readonly string[] PublicLoraExtensions = { ".safetensors", ".ckpt", ".pt", ".sft" };

    /// <summary>
    /// Read-only scan of the unified LoRA directory for **public** weights: files not owned by a
    /// logged <c>owner=model</c> / <c>owner=Plugin</c> entry. Mirrors the Python manager plugin's
    /// diff (same extension set as <c>lora-manager._PUBLIC_EXTS</c>). The sort is UI-friendly
    /// (<see cref="StringComparer.OrdinalIgnoreCase"/>) and is intentionally not asserted by the
    /// cross-language alignment tests (they compare sets, not order).
    /// </summary>
    internal static IReadOnlyList<string> ScanPublicLoras(string? loraRoot, string? lorasJsonPath)
    {
        if (string.IsNullOrWhiteSpace(loraRoot) || !Directory.Exists(loraRoot))
        {
            return Array.Empty<string>();
        }

        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var registry = new LoraRegistry(lorasJsonPath);
            foreach (var entry in registry.All)
            {
                var owner = entry.Owner ?? "";
                if (!owner.Equals("model", StringComparison.OrdinalIgnoreCase)
                    && !owner.Equals("Plugin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.Path))
                {
                    owned.Add(Path.GetFileName(entry.Path!));
                }
            }
        }
        catch (Exception)
        {
            // A missing / unreadable log leaves everything public (never fatal).
        }

        try
        {
            return Directory.EnumerateFiles(loraRoot!)
                .Where(IsPublicLoraExtension)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) && !owned.Contains(name!))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsPublicLoraExtension(string path)
    {
        var extension = Path.GetExtension(path);
        foreach (var candidate in PublicLoraExtensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<string> ScanPublicLoras(string? loraRoot)
        => ScanPublicLoras(loraRoot, Path.Combine(_shell.TemplateDirectory, "loras.json"));

    /// <summary>Re-scans the unified directory and refreshes the read-only public-LoRA label.</summary>
    private void RefreshPublicLorasDisplay(BackendSettings settings)
    {
        var publicLoras = ScanPublicLoras(ComfyDiscovery.DeriveLoraRoot(settings.ComfyRoot));
        SetLabel("PART_PublicLoras", publicLoras.Count == 0
            ? "（暂无；放入统一目录后点刷新）"
            : string.Join("、", publicLoras));
    }

    /// <summary>
    /// Renders the read-only "owned LoRA" reference: <c>loras.json</c> entries whose
    /// <c>owner</c> is <c>model</c> or <c>Plugin</c>, as <c>id（owner）：basename</c>.
    /// </summary>
    private void RefreshOwnedLorasDisplay(BackendSettings settings)
    {
        var entries = new List<string>();
        try
        {
            var registry = new LoraRegistry(Path.Combine(_shell.TemplateDirectory, "loras.json"));
            foreach (var entry in registry.All)
            {
                var owner = entry.Owner ?? "";
                if (!owner.Equals("model", StringComparison.OrdinalIgnoreCase)
                    && !owner.Equals("Plugin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(entry.Path) ? "" : Path.GetFileName(entry.Path!);
                entries.Add($"{entry.Id ?? "?"}（{owner}）：{name}");
            }
        }
        catch (Exception)
        {
            // A missing / unreadable log leaves the list empty (never fatal).
        }

        entries.Sort(StringComparer.Ordinal);
        SetLabel("PART_OwnedLoras", entries.Count == 0 ? "（无）" : string.Join("\n", entries));
    }

    /// <summary>Wires the public-LoRA "refresh" button (UI-side scan; no IPC / no seam trigger).</summary>
    private void WirePublicLoras()
    {
        if (this.FindControl<Button>("PART_RefreshPublicLoras") is { } button)
        {
            button.Click += (_, _) => RefreshPublicLorasDisplay(_shell.LoadSettings());
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e) => _ = RunFirstRunWizardAsync();

    /// <summary>
    /// First-run wizard (adjudication 1): only when the whole discovery chain failed. A cancelled
    /// picker keeps the existing failure behavior (a readable backend error later, never a crash).
    /// </summary>
    private async Task RunFirstRunWizardAsync()
    {
        if (_wizardShown || !_comfyRootEditable)
        {
            return;
        }

        _wizardShown = true;
        var directory = await _shell.PickFolderAsync(this, "未找到 ComfyUI 目录，请选择", null);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        SetText("PART_ComfyRoot", directory);
        SetText("PART_ComfyRootOverride", directory);
        if (this.FindControl<TextBox>("PART_ComfyRoot") is { } comfyBox)
        {
            comfyBox.IsReadOnly = true;
        }

        _comfyRootEditable = false;
        PersistComfyRoot(directory);
    }

    private void PersistComfyRoot(string directory)
    {
        try
        {
            SettingsWriter.WriteComfyRoot(_settingsPath, Normalize(directory));
        }
        catch (Exception)
        {
            // A failed wizard write must not break the window; the override still applies on Save.
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var dit = Normalize(Text("PART_DitPath"));
            var te = Normalize(Text("PART_TePath"));
            var vae = Normalize(Text("PART_VaePath"));
            var python = Normalize(Text("PART_PythonExeOverride"));
            var loraRoot = Normalize(Text("PART_LoraRoot"));

            // Advanced override wins; otherwise the main-area value is persisted only when it was
            // editable (discovery failed). An auto-discovered root is never written back.
            var comfyOverride = Normalize(Text("PART_ComfyRootOverride"));
            var comfy = comfyOverride.Length > 0
                ? comfyOverride
                : _comfyRootEditable ? Normalize(Text("PART_ComfyRoot")) : string.Empty;

            SettingsWriter.WriteModelPaths(_settingsPath, dit, te, vae);
            SettingsWriter.WriteLoraRoot(_settingsPath, loraRoot);
            SettingsWriter.WritePythonExe(_settingsPath, python);
            SettingsWriter.WriteComfyRoot(_settingsPath, comfy);

            await MessageDialog.ShowAsync(this, "保存成功，需重启 App 生效");
            Close();
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, ex.Message);
        }
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFullPath(value).Trim();

    private string? Text(string name) => this.FindControl<TextBox>(name)?.Text;

    private void SetText(string name, string? value)
    {
        if (this.FindControl<TextBox>(name) is { } target)
        {
            target.Text = value ?? string.Empty;
        }
    }

    private void SetLabel(string name, string value)
    {
        if (this.FindControl<TextBlock>(name) is { } target)
        {
            target.Text = value;
        }
    }

    private static string? SuggestedDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(value));
            return string.IsNullOrEmpty(directory) ? null : directory;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
