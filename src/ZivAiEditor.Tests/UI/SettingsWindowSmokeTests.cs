using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// Batch 1: proves the settings window builds under Avalonia.Headless with the new
/// <c>TabControl</c> (environment + plugin tabs), that the plugin tab renders a row per
/// registry entry, and that a toggle round-trips to <c>settings.ini [plugins]</c>. No Python /
/// GPU / real window. A fake <see cref="IShellContext"/> avoids the <c>settings.ini</c> seed
/// write and the single-instance pipe; the toggle test snapshots / restores the file it writes.
/// </summary>
public class SettingsWindowSmokeTests
{
    [Fact]
    public void SettingsWindow_Has_Environment_And_Plugin_Tabs()
    {
        var directory = NewDirectory();
        try
        {
            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory), new PluginRegistry(Path.Combine(directory, "plugins.json")));
                try
                {
                    var tabs = window.FindControl<TabControl>("PART_SettingsTabs");
                    Assert.NotNull(tabs);
                    Assert.Equal(2, tabs!.ItemCount);
                    Assert.NotNull(window.FindControl<TextBox>("PART_DitPath"));
                    Assert.NotNull(window.FindControl<ItemsControl>("PART_PluginList"));
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
    public void PluginTab_Renders_A_Row_Per_Registered_Plugin()
    {
        var directory = NewDirectory();
        try
        {
            var installed = Path.Combine(directory, "installed");
            Directory.CreateDirectory(installed);
            var registryPath = Path.Combine(directory, "plugins.json");
            File.WriteAllText(registryPath,
                "{ \"version\": \"1\", \"plugins\": ["
                + " { \"id\": \"pose-map\", \"display_name\": \"换姿势\", \"dir\": \"" + installed.Replace("\\", "\\\\") + "\" },"
                + " { \"id\": \"missing\", \"display_name\": \"缺插件\", \"dir\": \"" + Path.Combine(directory, "absent").Replace("\\", "\\\\") + "\" } ] }");
            var registry = new PluginRegistry(registryPath);

            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory), registry);
                try
                {
                    var list = window.FindControl<ItemsControl>("PART_PluginList");
                    Assert.NotNull(list);
                    Assert.Equal(2, list!.ItemCount);
                    // Both entries default to disabled -> the install/enable action is "启用".
                    Assert.NotNull(FindButtonByContent((Control)list.Items.Cast<object>().First(), "启用"));
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
    public void PluginToggle_Writes_Flag_To_Settings_File()
    {
        var directory = NewDirectory();
        var settingsPath = Path.Combine(System.AppContext.BaseDirectory, SettingsLoader.FileName);
        var existed = File.Exists(settingsPath);
        var original = existed ? File.ReadAllBytes(settingsPath) : null;
        try
        {
            var installed = Path.Combine(directory, "installed");
            Directory.CreateDirectory(installed);
            var registryPath = Path.Combine(directory, "plugins.json");
            File.WriteAllText(registryPath,
                "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"pose-map\", \"dir\": \"" + installed.Replace("\\", "\\\\") + "\", \"enabled_by_default\": true } ] }");
            var registry = new PluginRegistry(registryPath);

            HeadlessTest.Run(() =>
            {
                var window = new SettingsWindow(new FakeShell(directory), registry);
                try
                {
                    var list = window.FindControl<ItemsControl>("PART_PluginList");
                    Assert.NotNull(list);
                    var toggle = FindButtonByContent((Control)list!.Items.Cast<object>().First(), "禁用");
                    Assert.NotNull(toggle);

                    toggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                finally
                {
                    window.Close();
                }
            });

            var lines = File.ReadAllLines(settingsPath);
            Assert.Contains("[plugins]", lines);
            Assert.Contains("pose-map = 0", lines);
        }
        finally
        {
            if (existed)
            {
                File.WriteAllBytes(settingsPath, original!);
            }
            else if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }

            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void PluginStatusHelpers_Map_States()
    {
        var plugin = new PluginDescriptor { Id = "pose-map", EnabledByDefault = false };

        Assert.True(SettingsWindow.IsPluginEnabled(plugin, new Dictionary<string, string> { ["pose-map"] = "1" }));
        Assert.False(SettingsWindow.IsPluginEnabled(plugin, new Dictionary<string, string> { ["pose-map"] = "0" }));
        Assert.False(SettingsWindow.IsPluginEnabled(plugin, new Dictionary<string, string>()));
        Assert.True(SettingsWindow.IsPluginEnabled(
            new PluginDescriptor { Id = "on", EnabledByDefault = true },
            new Dictionary<string, string>()));

        Assert.Equal("未安装", SettingsWindow.PluginStatusLabel(installed: false, enabled: false));
        Assert.Equal("已安装未启用", SettingsWindow.PluginStatusLabel(installed: true, enabled: false));
        Assert.Equal("已启用", SettingsWindow.PluginStatusLabel(installed: true, enabled: true));
    }

    private static Button? FindButtonByContent(Control root, string content)
    {
        switch (root)
        {
            case Button button when Equals(button.Content, content):
                return button;
            case Border border when border.Child is { } child:
                return FindButtonByContent(child, content);
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    if (child is Control control && FindButtonByContent(control, content) is { } match)
                    {
                        return match;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zivai-sw-smoke-" + Guid.NewGuid().ToString("N"));
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
        private readonly BackendSettings _settings = new();

        public FakeShell(string templateDirectory)
        {
            TemplateDirectory = templateDirectory;
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
