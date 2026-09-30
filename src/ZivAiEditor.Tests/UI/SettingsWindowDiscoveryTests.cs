using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Xunit;
using ZivAiEditor.App;
using ZivAiEditor.Backend;
using ZivAiEditor.UI;

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// Plan C: the settings window defaults to the plugin tab; the environment tab shows
/// auto-discovery results read-only and keeps main.py non-editable. Headless only.
/// </summary>
public class SettingsWindowDiscoveryTests
{
    [Fact]
    public void SettingsWindow_Opens_Plugin_Tab_By_Default()
    {
        var directory = NewDirectory();
        try
        {
            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    window.Show();
                    var tabs = window.FindControl<TabControl>("PART_SettingsTabs");
                    Assert.NotNull(tabs);
                    Assert.Equal("插件", ((TabItem)tabs!.SelectedItem!).Header);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SettingsWindow_ComfyRoot_Is_ReadOnly_When_Resolved()
    {
        var directory = NewDirectory();
        try
        {
            var settings = new BackendSettings
            {
                ComfyRoot = @"D:\c\ComfyUI",
                PythonExe = @"D:\c\python_embeded\python.exe",
                Script = @"D:\app\python\server\main.py",
            };

            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory, settings), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    var comfy = window.FindControl<TextBox>("PART_ComfyRoot");
                    Assert.NotNull(comfy);
                    Assert.True(comfy!.IsReadOnly);
                    Assert.Equal(@"D:\c\ComfyUI", comfy.Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SettingsWindow_ComfyRoot_Is_Editable_When_Discovery_Fails()
    {
        var directory = NewDirectory();
        try
        {
            var settings = new BackendSettings(); // no ComfyUI root resolved

            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory, settings), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    var comfy = window.FindControl<TextBox>("PART_ComfyRoot");
                    Assert.NotNull(comfy);
                    Assert.False(comfy!.IsReadOnly);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SettingsWindow_Script_Is_ReadOnly_And_Has_No_Browse()
    {
        var directory = NewDirectory();
        try
        {
            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    var script = window.FindControl<TextBox>("PART_Script");
                    Assert.NotNull(script);
                    Assert.True(script!.IsReadOnly);
                    // main.py ships with the version: no browse button anywhere.
                    Assert.Null(window.FindControl<Button>("PART_BrowseScript"));
                    Assert.Null(window.FindControl<TextBox>("PART_ScriptOverride"));
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SettingsWindow_Lists_Public_Loras()
    {
        var directory = NewDirectory();
        try
        {
            var comfy = Path.Combine(directory, "ComfyUI");
            var loraRoot = Path.Combine(comfy, "models", "loras");
            Directory.CreateDirectory(loraRoot);
            File.WriteAllText(Path.Combine(loraRoot, "owned.safetensors"), "");
            File.WriteAllText(Path.Combine(loraRoot, "public.safetensors"), "");
            File.WriteAllText(Path.Combine(directory, "loras.json"),
                "{ \"loras\": [ { \"id\": \"face\", \"owner\": \"model\", \"path\": \"C:/x/owned.safetensors\" } ] }");
            var settings = new BackendSettings { ComfyRoot = comfy };

            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory, settings), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    var label = window.FindControl<TextBlock>("PART_PublicLoras");
                    Assert.NotNull(label);
                    Assert.Contains("public.safetensors", label!.Text);
                    Assert.DoesNotContain("owned.safetensors", label.Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SettingsWindow_Advanced_Exposes_Python_Comfy_Lora_Overrides()
    {
        var directory = NewDirectory();
        try
        {
            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    Assert.NotNull(window.FindControl<Expander>("PART_Advanced"));
                    Assert.NotNull(window.FindControl<TextBox>("PART_PythonExeOverride"));
                    Assert.NotNull(window.FindControl<TextBox>("PART_ComfyRootOverride"));
                    Assert.NotNull(window.FindControl<TextBox>("PART_LoraRoot"));
                    Assert.NotNull(window.FindControl<Button>("PART_BrowseLoraRoot"));
                    Assert.NotNull(window.FindControl<Button>("PART_ClearLoraRoot"));
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SettingsWindow_Refresh_Public_Loras_Button_Rescans()
    {
        var directory = NewDirectory();
        try
        {
            var comfy = Path.Combine(directory, "ComfyUI");
            var loraRoot = Path.Combine(comfy, "models", "loras");
            Directory.CreateDirectory(loraRoot);
            File.WriteAllText(Path.Combine(directory, "loras.json"), "{ \"loras\": [] }");
            var settings = new BackendSettings { ComfyRoot = comfy };

            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory, settings), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    var label = window.FindControl<TextBlock>("PART_PublicLoras");
                    Assert.NotNull(label);
                    Assert.Equal("(无)", label!.Text);

                    // A new public weight appears on disk; the refresh button must re-scan (UI-side).
                    File.WriteAllText(Path.Combine(loraRoot, "new.safetensors"), "");
                    var button = window.FindControl<Button>("PART_RefreshPublicLoras");
                    Assert.NotNull(button);
                    button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    Assert.Contains("new.safetensors", label.Text);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zivai-disc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeShell : IShellContext
    {
        private readonly BackendSettings _settings;

        public FakeShell(string templateDirectory, BackendSettings? settings = null)
        {
            TemplateDirectory = templateDirectory;
            _settings = settings ?? new BackendSettings();
        }

        public BackendSettings LoadSettings() => _settings;

        public string TemplateDirectory { get; }

        public event Action<LaunchOptions>? LaunchRequested
        {
            add { }
            remove { }
        }

        public void OpenFolder(string path)
        {
        }

        public Task<string?> PickFolderAsync(
            Window owner,
            string title,
            string? suggestedDirectory = null,
            CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(
            Window owner,
            string title,
            string? suggestedDirectory = null,
            CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
