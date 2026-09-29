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

    private string UserPath => Path.Combine(_directory, PluginRegistry.UserFileName);

    [Fact]
    public void Loads_Valid_Registry_And_Maps_Fields()
    {
        File.WriteAllText(RegistryPath,
            "{ \"version\": \"1\", \"plugins\": ["
            + " { \"id\": \"pose-map\", \"display_name\": \"换姿势\", \"version\": \"1.2.3\","
            + "   \"capabilities\": [\"sampling_plan\", \"\"],"
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
        Assert.Equal(new[] { "sampling_plan" }, plugin.Capabilities);
        Assert.Equal(new[] { "numpy" }, plugin.Deps);
        Assert.True(plugin.EnabledByDefault);
        Assert.Equal("pose demo", plugin.Description);
        Assert.Equal("pose", plugin.Kind);
        Assert.NotNull(registry.Get("pose-map"));
        Assert.Null(registry.Get("missing"));
    }

        [Fact]
    public void Maps_Seams_And_Filters_Blank()
    {
        File.WriteAllText(RegistryPath,
            "{ \"version\": \"1\", \"plugins\": ["
            + " { \"id\": \"before-sampler\", \"seams\": [\"before_sample\", \"\"] } ] }");

        var plugin = Assert.Single(new PluginRegistry(RegistryPath).All);

        Assert.Equal(new[] { "before_sample" }, plugin.Seams);
    }

    [Fact]
    public void Defaults_Entry_When_Omitted()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");

        Assert.Equal("__init__.py", Assert.Single(new PluginRegistry(RegistryPath).All).Entry);
        Assert.Empty(Assert.Single(new PluginRegistry(RegistryPath).All).Seams);
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

    [Fact]
    public void User_File_Whole_Entry_Override_Replaces_Builtin()
    {
        File.WriteAllText(RegistryPath,
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\", \"dir\": \"builtin-a\", \"enabled_by_default\": true } ] }");
        File.WriteAllText(UserPath,
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\", \"dir\": \"user-a\" } ] }");

        var plugin = Assert.Single(new PluginRegistry(RegistryPath).All);

        Assert.Equal("a", plugin.Id);
        Assert.Equal("user-a", plugin.Dir);
        // Whole-entry replace: enabled_by_default is NOT inherited from the built-in entry.
        Assert.False(plugin.EnabledByDefault);
    }

    [Fact]
    public void User_File_New_Id_Is_Appended_After_Builtins()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");
        File.WriteAllText(UserPath,
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"b\" }, { \"id\": \"c\" } ] }");

        var registry = new PluginRegistry(RegistryPath);

        Assert.Equal(3, registry.All.Count);
        Assert.Equal("a", registry.All[0].Id);
        Assert.Equal("b", registry.All[1].Id);
        Assert.Equal("c", registry.All[2].Id);
    }

    [Fact]
    public void Missing_User_File_Leaves_Builtin_And_No_User_Entries()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");

        var registry = new PluginRegistry(RegistryPath);

        Assert.Single(registry.All);
        Assert.False(registry.IsUserEntry("a"));
    }

    [Fact]
    public void Unreadable_User_File_Leaves_Builtin_Intact()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");
        File.WriteAllText(UserPath, "{ not json");

        var registry = new PluginRegistry(RegistryPath);

        Assert.Single(registry.All);
        Assert.False(registry.IsUserEntry("a"));
    }

    [Fact]
    public void IsUserEntry_Tracks_Raw_User_Set_And_UserFilePath()
    {
        File.WriteAllText(RegistryPath,
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"builtin\" }, { \"id\": \"overridden\" } ] }");
        File.WriteAllText(UserPath,
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"overridden\" }, { \"id\": \"extra\" } ] }");

        var registry = new PluginRegistry(RegistryPath);

        Assert.True(registry.IsUserEntry("overridden"));
        Assert.True(registry.IsUserEntry("extra"));
        Assert.False(registry.IsUserEntry("builtin"));
        Assert.Equal(UserPath, registry.UserFilePath);
    }

    [Fact]
    public void Missing_Builtin_Ignores_User_File()
    {
        File.WriteAllText(UserPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"extra\" } ] }");

        var registry = new PluginRegistry(RegistryPath);

        Assert.Empty(registry.All);
        Assert.False(registry.IsUserEntry("extra"));
    }

    [Fact]
    public void RemoveEntry_Drops_User_Entry_And_Reverts_To_Builtin()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");
        File.WriteAllText(UserPath,
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\", \"dir\": \"user-a\" }, { \"id\": \"b\" } ] }");

        Assert.True(PluginOverrideStore.RemoveEntry(RegistryPath, "a"));

        var registry = new PluginRegistry(RegistryPath);
        Assert.Equal(2, registry.All.Count);
        Assert.False(registry.IsUserEntry("a"));
        Assert.True(registry.IsUserEntry("b"));
    }

    [Fact]
    public void RemoveEntry_Deletes_User_File_When_List_Becomes_Empty()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");
        File.WriteAllText(UserPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");

        Assert.True(PluginOverrideStore.RemoveEntry(RegistryPath, "a"));

        Assert.False(File.Exists(UserPath));
        // No override -> the built-in entry is used again.
        Assert.Equal("a", Assert.Single(new PluginRegistry(RegistryPath).All).Id);
        Assert.False(new PluginRegistry(RegistryPath).IsUserEntry("a"));
    }

    [Fact]
    public void RemoveEntry_Missing_User_File_Is_NoOp()
    {
        File.WriteAllText(RegistryPath, "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"a\" } ] }");

        Assert.False(PluginOverrideStore.RemoveEntry(RegistryPath, "a"));
    }
}
