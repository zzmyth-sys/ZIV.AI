using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace ZivAiEditor.App;

/// <summary>
/// Modal settings window: model weights, the Python executable and the template folder.
/// Fields are pre-filled from the program-directory <c>settings.ini</c>; saving writes only
/// the affected keys (comments / other lines preserved) and asks for an app restart.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly IShellContext _shell = null!;
    private readonly string _settingsPath = null!;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    internal SettingsWindow(IShellContext shell)
        : this()
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _settingsPath = Path.Combine(System.AppContext.BaseDirectory, SettingsLoader.FileName);

        if (this.FindControl<Border>("PART_Header") is { } header)
        {
            header.PointerPressed += (_, e) => BeginMoveDrag(e);
        }

        WireBrowse("PART_BrowseDit", "PART_DitPath", "选择模型权重");
        WireBrowse("PART_BrowseTe", "PART_TePath", "选择模型权重");
        WireBrowse("PART_BrowseVae", "PART_VaePath", "选择模型权重");
        WireBrowse("PART_BrowseScript", "PART_Script", "选择 main.py");
        WireOpen("PART_OpenDit", "PART_DitPath");
        WireOpen("PART_OpenTe", "PART_TePath");
        WireOpen("PART_OpenVae", "PART_VaePath");
        WirePython();
        WireComfy();
        WireFooter();
        Prefill();
        UpdateOpenEnabled();
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

    private void WirePython()
    {
        if (this.FindControl<Button>("PART_BrowsePython") is not { } button)
        {
            return;
        }

        button.Click += async (_, _) =>
        {
            if (this.FindControl<TextBox>("PART_PythonExe") is not { } target)
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

    private void WireComfy()
    {
        if (this.FindControl<Button>("PART_BrowseComfy") is not { } button)
        {
            return;
        }

        button.Click += async (_, _) =>
        {
            if (this.FindControl<TextBox>("PART_ComfyRoot") is not { } target)
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

    private void Prefill()
    {
        var settings = _shell.LoadSettings();
        SetText("PART_DitPath", settings.DitPath);
        SetText("PART_TePath", settings.TePath);
        SetText("PART_VaePath", settings.VaePath);
        SetText("PART_PythonExe", settings.PythonExe);
        SetText("PART_Script", settings.Script);
        SetText("PART_ComfyRoot", settings.ComfyRoot);
    }

    private async Task SaveAsync()
    {
        try
        {
            var dit = Normalize(Text("PART_DitPath"));
            var te = Normalize(Text("PART_TePath"));
            var vae = Normalize(Text("PART_VaePath"));
            var python = Normalize(Text("PART_PythonExe"));
            var script = Normalize(Text("PART_Script"));
            var comfy = Normalize(Text("PART_ComfyRoot"));

            SettingsWriter.WriteModelPaths(_settingsPath, dit, te, vae);
            SettingsWriter.WritePythonExe(_settingsPath, python);
            SettingsWriter.WriteScript(_settingsPath, script);
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
