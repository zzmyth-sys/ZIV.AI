using System;
using System.IO;
using Xunit;
using ZivAiEditor.Backend;

namespace ZivAiEditor.Tests;

/// <summary>
/// Batch 1: <see cref="PluginRegistry"/> data loading (no GPU / Python). Each test uses its own
/// temp directory so no shipped <c>plugins.json</c> is read.
/// </summary>
public class PluginRegistryTests : IDisposable
{
    private readonly string _directory;

    public PluginRegistryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_plugins_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string RegistryPath => Path.Combine(_directory, "plugins.json");

    [Fact]
    public void Loads_Valid_Registry_And_Maps_Fields()
    {
        File.WriteAllText(RegistryPath,
            "{ \"version\": \"1\", \"plugins\": ["
            + " { \"id\": \"pose-map\", \"display_name\": \"换姿势\", \"version\": \"1.2.3\","
            + "   \"dir\": \"Comfyui/plugins/pose-map\", \"entry\": \"plugin.py\","
            + "   \"deps\": [\"numpy\", \"\"], \"enabled_by_default\": true,"
            + "   \"description\": \"pose demo\", \"kind\": \"pose\" } ] }");

        var registry = new PluginRegistry(RegistryPath);

        var plugin = Assert.Single(registry.All);
        Assert.Equal("pose-map", plugin.Id);
        Assert.Equal("换姿势", plugin.DisplayName);
        Assert.Equal("1.2.3", plugin.Version);
        Assert.Equal("Comfyui/plugins/pose-map", plugin.Dir);
        Assert.Equal("plugin.py", plugin.Entry);
        Assert.Equal(new[] { "numpy" }, plugin.Deps);
        Assert.True(plugin.EnabledByDefault);
        Assert.Equal("pose demo", plugin.Description);
        Assert.Equal("pose", plugin.Kind);
        Assert.NotNull(registry.Get("pose-map"));
        Assert.Null(registry.Get("missing"));
    }

    [Fact]
    public void Defaults_Entry_When_Omitted()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");

        Assert.Equal("__init__.py", Assert.Single(new PluginRegistry(RegistryPath).All).Entry);
    }

    [Fact]
    public void Missing_File_Yields_Empty_Registry()
    {
        Assert.Empty(new PluginRegistry(Path.Combine(_directory, "absent.json")).All);
    }

    [Fact]
    public void Corrupt_Json_Yields_Empty_Registry()
    {
        File.WriteAllText(RegistryPath, "{ not json");

        Assert.Empty(new PluginRegistry(RegistryPath).All);
    }

    [Fact]
    public void Empty_Plugins_Array_Yields_Empty_Registry()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [] }");

        Assert.Empty(new PluginRegistry(RegistryPath).All);
    }

    [Fact]
    public void EnvName_Matches_Python_Rule()
    {
        Assert.Equal("ZIV_AI_PLUGIN_POSE_MAP", PluginRegistry.EnvName("pose-map"));
        Assert.Equal("ZIV_AI_PLUGIN_SDPOSE_OOD", PluginRegistry.EnvName("sdpose.ood"));
        Assert.Equal("ZIV_AI_PLUGIN_A_B9", PluginRegistry.EnvName("A b9"));
        Assert.Equal("ZIV_AI_PLUGIN_X_1", PluginRegistry.EnvName("x_1"));
    }

    [Fact]
    public void ResolveDirectory_Relative_Is_Under_Program_Directory()
    {
        var plugin = new PluginDescriptor { Dir = Path.Combine("plugins", "pose-map") };

        Assert.Equal(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "plugins", "pose-map")),
            PluginRegistry.ResolveDirectory(plugin));
    }

    [Fact]
    public void ResolveDirectory_Absolute_Is_Preserved()
    {
        var plugin = new PluginDescriptor { Dir = _directory };

        Assert.Equal(Path.GetFullPath(_directory), PluginRegistry.ResolveDirectory(plugin));
    }
}
