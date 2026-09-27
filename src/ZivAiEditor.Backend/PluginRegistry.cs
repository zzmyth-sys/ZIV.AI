using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ZivAiEditor.Backend;

/// <summary>
/// One plugin entry from <c>Template/plugins.json</c> (batch 1). A plain value object; the
/// enable state is not stored here (it lives in <c>settings.ini [plugins]</c> keyed by
/// <see cref="Id"/>).
/// </summary>
public sealed class PluginDescriptor
{
    public string Id { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string Version { get; init; } = "";

    /// <summary>Plugin directory; relative to the program directory (absolute allowed).</summary>
    public string Dir { get; init; } = "";

    /// <summary>Entry file inside <see cref="Dir"/>; defaults to <c>__init__.py</c>.</summary>
    public string Entry { get; init; } = "__init__.py";

    /// <summary>Declared Python import names; checked (not installed) by the backend loader.</summary>
    public IReadOnlyList<string> Deps { get; init; } = Array.Empty<string>();

    public bool EnabledByDefault { get; init; }

    public string Description { get; init; } = "";

    public string Kind { get; init; } = "";
}

/// <summary>
/// In-process plugin registry (batch 1). Loads <c>Template/plugins.json</c> (data); adding a
/// plugin is a data change. A missing / unreadable file yields an empty registry (Z28: never
/// fails). Enable state is owned by <c>settings.ini [plugins]</c>, not this type.
/// </summary>
public sealed class PluginRegistry
{
    private const string FileName = "plugins.json";

    private readonly List<PluginDescriptor> _plugins;

    /// <param name="pluginsFilePath">
    /// Overrides the registry path (tests / App assembly use this). Defaults to
    /// <c>{AppContext.BaseDirectory}/Template/plugins.json</c>.
    /// </param>
    public PluginRegistry(string? pluginsFilePath = null)
    {
        _plugins = Load(pluginsFilePath);
    }

    public IReadOnlyList<PluginDescriptor> All => _plugins;

    public PluginDescriptor? Get(string pluginId)
        => _plugins.FirstOrDefault(p => string.Equals(p.Id, pluginId, StringComparison.Ordinal));

    /// <summary>
    /// Env var name for a plugin id; must match Python <c>config.plugin_env_name</c>. Every char
    /// outside <c>[A-Za-z0-9]</c> becomes <c>_</c>, then the whole id is upper-cased
    /// (<c>pose-map</c> -&gt; <c>ZIV_AI_PLUGIN_POSE_MAP</c>).
    /// </summary>
    public static string EnvName(string pluginId)
    {
        var builder = new StringBuilder("ZIV_AI_PLUGIN_");
        foreach (var ch in pluginId ?? string.Empty)
        {
            builder.Append(IsAsciiAlphanumeric(ch) ? ch : '_');
        }

        return builder.ToString().ToUpperInvariant();
    }

    /// <summary>Absolute plugin directory; a relative <see cref="PluginDescriptor.Dir"/> is
    /// resolved against the program directory (<see cref="AppContext.BaseDirectory"/>).</summary>
    public static string ResolveDirectory(PluginDescriptor plugin)
    {
        var dir = plugin.Dir ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dir))
        {
            return string.Empty;
        }

        return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(AppContext.BaseDirectory, dir));
    }

    private static bool IsAsciiAlphanumeric(char ch)
        => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');

    private static List<PluginDescriptor> Load(string? pluginsFilePath)
    {
        var path = pluginsFilePath ?? Path.Combine(AppContext.BaseDirectory, "Template", FileName);
        if (!File.Exists(path))
        {
            return new List<PluginDescriptor>();
        }

        try
        {
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize(stream, PluginJsonContext.Default.PluginsFileDto);
            return (dto?.Plugins ?? new List<PluginDto>())
                .Where(plugin => !string.IsNullOrWhiteSpace(plugin.Id))
                .Select(ToDescriptor)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[plugin] plugins.json load failed: {ex.Message}");
            return new List<PluginDescriptor>();
        }
    }

    private static PluginDescriptor ToDescriptor(PluginDto dto)
    {
        var id = dto.Id!.Trim();
        return new PluginDescriptor
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? id : dto.DisplayName!,
            Version = dto.Version ?? string.Empty,
            Dir = dto.Dir ?? string.Empty,
            Entry = string.IsNullOrWhiteSpace(dto.Entry) ? "__init__.py" : dto.Entry!,
            Deps = (dto.Deps ?? new List<string>())
                .Where(dep => !string.IsNullOrWhiteSpace(dep))
                .ToArray(),
            EnabledByDefault = dto.EnabledByDefault,
            Description = dto.Description ?? string.Empty,
            Kind = dto.Kind ?? string.Empty,
        };
    }
}
