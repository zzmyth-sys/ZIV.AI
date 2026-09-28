using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZivAiEditor.Backend;

namespace ZivAiEditor.App;

/// <summary>
/// Settings-window plugin tab (batch 1). Lists the plugins registered in
/// <c>Template/plugins.json</c>, shows install / enable state, toggles enable (writing the raw
/// <c>1</c> / <c>0</c> into <c>settings.ini [plugins]</c>) and opens the plugin directory. The
/// dependency check is deferred to a later batch (button disabled).
/// </summary>
public partial class SettingsWindow
{
    private static readonly IBrush PluginTextBrush = new SolidColorBrush(Color.Parse("#DDDDDD"));
    private static readonly IBrush PluginSecondaryBrush = new SolidColorBrush(Color.Parse("#AAAAAA"));
    private static readonly IBrush PluginEnabledBrush = new SolidColorBrush(Color.Parse("#7FC97F"));
    private static readonly IBrush PluginMissingBrush = new SolidColorBrush(Color.Parse("#E06C4A"));

    private void InitPlugins() => RefreshPlugins();

    private void RefreshPlugins()
    {
        if (this.FindControl<ItemsControl>("PART_PluginList") is not { } list)
        {
            return;
        }

        // Re-read the registry so 注销 enablement reflects the current user override file; the
        // raw user set is never cached across refreshes.
        _plugins = new PluginRegistry(_pluginsPath);

        list.Items.Clear();
        if (_plugins.All.Count == 0)
        {
            list.Items.Add(new TextBlock
            {
                Text = "未注册任何插件（Template/plugins.json 为空）。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = PluginSecondaryBrush,
            });
            return;
        }

        var states = ReadPluginStates();
        foreach (var plugin in _plugins.All)
        {
            list.Items.Add(BuildPluginRow(plugin, states));
        }
    }

    /// <summary>Enable state for display: an explicit <c>[plugins]</c> value wins, else the
    /// registry default. Pure so the plugin-table logic is unit-testable without a window.</summary>
    internal static bool IsPluginEnabled(PluginDescriptor plugin, IReadOnlyDictionary<string, string> states)
        => states.TryGetValue(plugin.Id, out var raw) ? raw == "1" : plugin.EnabledByDefault;

    /// <summary>Install / enable status text (batch 1). Pure and unit-testable.</summary>
    internal static string PluginStatusLabel(bool installed, bool enabled)
        => !installed ? "未安装" : enabled ? "已启用" : "已安装未启用";

    /// <summary>
    /// Metadata line for the plugin row (batch 3 catalog): version + capability names, read
    /// data-driven from <c>plugins.json</c>. Empty parts are omitted. Pure and unit-testable.
    /// </summary>
    internal static string PluginMetaLabel(PluginDescriptor plugin)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(plugin.Version))
        {
            parts.Add("v" + plugin.Version);
        }

        if (plugin.Capabilities.Count > 0)
        {
            parts.Add("capabilities: " + string.Join(", ", plugin.Capabilities));
        }

        return string.Join(" · ", parts);
    }

    private Control BuildPluginRow(PluginDescriptor plugin, IReadOnlyDictionary<string, string> states)
    {
        var directory = PluginRegistry.ResolveDirectory(plugin);
        var installed = directory.Length > 0 && Directory.Exists(directory);
        var enabled = IsPluginEnabled(plugin, states);

        var status = PluginStatusLabel(installed, enabled);
        var statusBrush = !installed ? PluginMissingBrush : enabled ? PluginEnabledBrush : PluginSecondaryBrush;

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = plugin.DisplayName,
                    Foreground = PluginTextBrush,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = status,
                    Foreground = statusBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        var description = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(plugin.Description) ? plugin.Id : plugin.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = PluginSecondaryBrush,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var body = new StackPanel { Children = { header } };

        var meta = PluginMetaLabel(plugin);
        if (meta.Length > 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = meta,
                TextWrapping = TextWrapping.Wrap,
                Foreground = PluginSecondaryBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        body.Children.Add(description);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                BuildPluginToggle(plugin.Id, enabled, installed),
                BuildOpenDirButton(directory),
                BuildInstallCheckButton(plugin, directory),
                BuildUnregisterButton(plugin),
            },
        };

        body.Children.Add(actions);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#3A3A3A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = body,
        };
    }

    private Button BuildPluginToggle(string pluginId, bool enabled, bool installed)
    {
        var button = new Button
        {
            Content = enabled ? "禁用" : "启用",
            MinWidth = 64,
            Height = 28,
            IsEnabled = installed,
        };

        if (!installed)
        {
            ToolTip.SetTip(button, "插件目录不存在，先放入插件再启用");
        }

        button.Click += (_, _) => SetPluginState(pluginId, !enabled);
        return button;
    }

    private Button BuildOpenDirButton(string directory)
    {
        var button = new Button { Content = "打开目录", MinWidth = 80, Height = 28 };
        button.Click += (_, _) => _shell.OpenFolder(directory);
        return button;
    }

    /// <summary>
    /// "检查安装" (batch 3): verifies the plugin directory and entry file exist, using **only
    /// the C# side** (no IPC). The Python dependency probe (<c>find_spec</c>) stays deferred —
    /// it needs the backend — so it is not part of this button.
    /// </summary>
    private Button BuildInstallCheckButton(PluginDescriptor plugin, string directory)
    {
        var button = new Button { Content = "检查安装", MinWidth = 80, Height = 28 };
        ToolTip.SetTip(button, "检查插件目录与入口文件是否存在");
        button.Click += (_, _) =>
        {
            var dirPresent = directory.Length > 0 && Directory.Exists(directory);
            var entryPath = dirPresent ? Path.Combine(directory, plugin.Entry) : string.Empty;
            var entryPresent = entryPath.Length > 0 && File.Exists(entryPath);
            _ = MessageDialog.ShowAsync(
                this,
                $"插件：{plugin.Id}\n"
                + $"目录：{(directory.Length > 0 ? directory : "(未配置)")}\n"
                + $"目录存在：{(dirPresent ? "是" : "否")}\n"
                + $"入口（{plugin.Entry}）：{(entryPresent ? "存在" : "缺失")}");
        };
        return button;
    }

    /// <summary>
    /// "注销" (batch 3): removes the user override entry so the plugin reverts to its built-in
    /// entry (or disappears when it had no built-in). Enabled only for a user entry; the built-in
    /// list is never touched and no plugin directory file is deleted.
    /// </summary>
    private Button BuildUnregisterButton(PluginDescriptor plugin)
    {
        var isUserEntry = _plugins.IsUserEntry(plugin.Id);
        var button = new Button { Content = "注销", MinWidth = 64, Height = 28, IsEnabled = isUserEntry };
        if (!isUserEntry)
        {
            ToolTip.SetTip(button, "内置插件不可注销");
        }

        button.Click += async (_, _) =>
        {
            if (await ConfirmDialog.ShowAsync(this, "确认注销该插件？目录文件不会删除。") != true)
            {
                return;
            }

            PluginOverrideStore.RemoveEntry(_pluginsPath, plugin.Id);
            RefreshPlugins();
        };
        return button;
    }

    private void SetPluginState(string pluginId, bool enabled)
    {
        var states = ReadPluginStates();
        states[pluginId] = enabled ? "1" : "0";
        try
        {
            SettingsWriter.WritePluginStates(_settingsPath, states);
            RefreshPlugins();
        }
        catch (Exception ex)
        {
            _ = MessageDialog.ShowAsync(this, ex.Message);
        }
    }

    private Dictionary<string, string> ReadPluginStates()
    {
        var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _shell.LoadSettings().PluginStates)
        {
            states[pair.Key] = pair.Value ? "1" : "0";
        }

        return states;
    }
}
