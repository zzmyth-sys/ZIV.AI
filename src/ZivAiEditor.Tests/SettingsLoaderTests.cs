using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using ZivAiEditor.App;
using ZivAiEditor.Backend;

namespace ZivAiEditor.Tests;

/// <summary>
/// B7: <see cref="SettingsLoader"/> <c>[models]</c> parsing, the runtime backend defaults and the
/// <c>settings.ini.template</c> seed (no GPU / Python). Each test uses a temp program directory.
/// </summary>
public class SettingsLoaderTests : IDisposable
{
    private readonly string _directory;

    public SettingsLoaderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_load_" + Guid.NewGuid().ToString("N"));
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

    private string SettingsPath => Path.Combine(_directory, "settings.ini");

    [Fact]
    public void Load_Parses_Models_Section()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[models]",
            @"dit_path = D:\m\dit.safetensors",
            @"te_path = D:\m\te.safetensors",
            @"vae_path = D:\m\vae.safetensors",
        });

        var settings = SettingsLoader.Load(_directory);

        Assert.Equal(@"D:\m\dit.safetensors", settings.DitPath);
        Assert.Equal(@"D:\m\te.safetensors", settings.TePath);
        Assert.Equal(@"D:\m\vae.safetensors", settings.VaePath);
    }

    [Fact]
    public void Load_Returns_Null_For_Missing_Or_Empty_Model_Paths()
    {
        File.WriteAllLines(SettingsPath, new[] { "[models]", "dit_path =", "te_path =" });

        var settings = SettingsLoader.Load(_directory);

        Assert.Null(settings.DitPath);
        Assert.Null(settings.TePath);
        Assert.Null(settings.VaePath);
    }

    [Fact]
    public void Load_Resolves_Backend_Defaults_From_Program_Directory()
    {
        // A9: no dev-machine path is baked in; with an empty / missing backend path the
        // defaults are resolved relative to the program directory passed to Load.
        var settings = SettingsLoader.Load(_directory);

        Assert.Equal(
            Path.Combine(_directory, "Comfyui", "python_embeded", "python.exe"),
            settings.PythonExe);
        Assert.Equal(
            Path.Combine(_directory, "python", "server", "main.py"),
            settings.Script);
    }

    [Fact]
    public void Load_Seeds_From_Settings_Ini_Template()
    {
        // B2: the program-directory template is accepted as-is (no DOC/FROZEN.md needed), so a
        // published install with only settings.ini.template seeds a clean settings.ini on launch.
        var template = Path.Combine(_directory, "settings.ini.template");
        File.WriteAllLines(template, new[] { "[backend]", "pipe_name = seeded", "" });

        SettingsLoader.Load(_directory);

        Assert.True(File.Exists(SettingsPath));
        Assert.Equal(File.ReadAllText(template), File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Load_Does_Not_Overwrite_Existing_Settings()
    {
        File.WriteAllLines(SettingsPath, new[] { "[backend]", "pipe_name = keep", "" });
        File.WriteAllLines(
            Path.Combine(_directory, "settings.ini.template"),
            new[] { "[backend]", "pipe_name = seeded", "" });

        SettingsLoader.Load(_directory);

        Assert.Equal("keep", SettingsLoader.Load(_directory).PipeName);
    }

    [Fact]
    public void Load_Parses_Comfy_Root()
    {
        File.WriteAllLines(SettingsPath, new[] { "[backend]", @"comfy_root = D:\c\ComfyUI" });

        Assert.Equal(@"D:\c\ComfyUI", SettingsLoader.Load(_directory).ComfyRoot);
    }

    [Fact]
    public void Load_Returns_Null_For_Missing_Comfy_Root()
    {
        File.WriteAllLines(SettingsPath, new[] { "[backend]", "pipe_name = zivai.infer.v1" });

        Assert.Null(SettingsLoader.Load(_directory).ComfyRoot);
    }

    [Fact]
    public void Load_Parses_Lora_Root()
    {
        File.WriteAllLines(SettingsPath, new[] { "[models]", @"lora_root = D:\l\loras" });

        Assert.Equal(@"D:\l\loras", SettingsLoader.Load(_directory).LoraRoot);
    }

    [Fact]
    public void Load_Returns_Null_For_Missing_Or_Empty_Lora_Root()
    {
        File.WriteAllLines(SettingsPath, new[] { "[models]", "lora_root =" });

        Assert.Null(SettingsLoader.Load(_directory).LoraRoot);
    }

    [Fact]
    public void Load_Derives_Lora_Root_From_Discovered_Comfy_Root()
    {
        // No [models] lora_root -> default to the unified directory <comfy_root>/models/loras.
        Directory.CreateDirectory(Path.Combine(_directory, "Comfyui", "ComfyUI"));

        var settings = SettingsLoader.Load(_directory);

        Assert.Equal(
            Path.Combine(_directory, "Comfyui", "ComfyUI", "models", "loras"),
            settings.LoraRoot);
    }

    [Fact]
    public void Load_Prefers_Explicit_Lora_Root_Over_Derived()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "Comfyui", "ComfyUI"));
        File.WriteAllLines(SettingsPath, new[] { "[models]", @"lora_root = D:\l\loras" });

        Assert.Equal(@"D:\l\loras", SettingsLoader.Load(_directory).LoraRoot);
    }

    [Fact]
    public void Load_Prefers_Env_Settings_Path_Override()
    {
        // ZIV_AI_SETTINGS_PATH is honoured only on the default path (programDirectory == null).
        var overridePath = Path.Combine(_directory, "override.ini");
        File.WriteAllLines(overridePath, new[] { "[backend]", "pipe_name = from-env" });

        var saved = Environment.GetEnvironmentVariable("ZIV_AI_SETTINGS_PATH");
        try
        {
            Environment.SetEnvironmentVariable("ZIV_AI_SETTINGS_PATH", overridePath);
            var settings = SettingsLoader.Load();

            Assert.Equal("from-env", settings.PipeName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZIV_AI_SETTINGS_PATH", saved);
        }
    }

    [Fact]
    public void Load_Explicit_Directory_Ignores_Env_Override()
    {
        File.WriteAllLines(SettingsPath, new[] { "[backend]", "pipe_name = from-dir" });

        var saved = Environment.GetEnvironmentVariable("ZIV_AI_SETTINGS_PATH");
        try
        {
            Environment.SetEnvironmentVariable(
                "ZIV_AI_SETTINGS_PATH", Path.Combine(_directory, "nope.ini"));
            var settings = SettingsLoader.Load(_directory);

            Assert.Equal("from-dir", settings.PipeName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZIV_AI_SETTINGS_PATH", saved);
        }
    }

    [Fact]
    public void Load_Parses_Plugins_Section()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[plugins]",
            "pose-map = 1",
            "sdpose.ood = 0",
            "ignored = maybe",
        });

        var states = SettingsLoader.Load(_directory).PluginStates;

        Assert.True(states["pose-map"]);
        Assert.False(states["sdpose.ood"]);
        Assert.False(states.ContainsKey("ignored"));
    }

    [Fact]
    public void Load_Defaults_PluginStates_To_Empty()
    {
        File.WriteAllLines(SettingsPath, new[] { "[backend]", "pipe_name = x" });

        Assert.Empty(SettingsLoader.Load(_directory).PluginStates);
    }
}

/// <summary>B3 / T1: the AppContext backend environment assembly is pure and unit-testable.</summary>
public class BackendEnvironmentTests : IDisposable
{
    private readonly string _directory;

    public BackendEnvironmentTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_env_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void BuildBackendEnvironment_Includes_Only_NonEmpty_Paths()
    {
        var settings = new BackendSettings
        {
            DitPath = @"D:\m\dit.safetensors",
            TePath = null,
            VaePath = "   ",
        };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.Equal(new[] { "ZIV_AI_DIT_PATH" }, environment.Keys.ToArray());
        Assert.Equal(@"D:\m\dit.safetensors", environment["ZIV_AI_DIT_PATH"]);
    }

    [Fact]
    public void BuildBackendEnvironment_Includes_All_Keys_When_Set()
    {
        var settings = new BackendSettings
        {
            DitPath = @"D:\m\dit.safetensors",
            TePath = @"D:\m\te.safetensors",
            VaePath = @"D:\m\vae.safetensors",
        };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.Equal(3, environment.Count);
        Assert.True(environment.ContainsKey("ZIV_AI_DIT_PATH"));
        Assert.True(environment.ContainsKey("ZIV_AI_TE_PATH"));
        Assert.True(environment.ContainsKey("ZIV_AI_VAE_PATH"));
    }

    [Fact]
    public void BuildBackendEnvironment_Injects_Registry_Paths_When_Files_Exist()
    {
        var settings = new BackendSettings();
        File.WriteAllText(Path.Combine(_directory, "models.json"), "{}");
        File.WriteAllText(Path.Combine(_directory, "loras.json"), "{}");

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.Equal(Path.Combine(_directory, "models.json"), environment["ZIV_AI_MODELS_REGISTRY"]);
        Assert.Equal(Path.Combine(_directory, "loras.json"), environment["ZIV_AI_LORA_REGISTRY"]);
    }

    [Fact]
    public void BuildBackendEnvironment_Skips_Missing_Registry()
    {
        var settings = new BackendSettings();
        File.WriteAllText(Path.Combine(_directory, "models.json"), "{}");

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.True(environment.ContainsKey("ZIV_AI_MODELS_REGISTRY"));
        Assert.False(environment.ContainsKey("ZIV_AI_LORA_REGISTRY"));
    }

    [Fact]
    public void BuildBackendEnvironment_Injects_ComfyRoot_When_Directory_Exists()
    {
        var settings = new BackendSettings { ComfyRoot = _directory };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.Equal(_directory, environment["ZIV_AI_COMFY_ROOT"]);
    }

    [Fact]
    public void BuildBackendEnvironment_Skips_Missing_ComfyRoot_Directory()
    {
        var settings = new BackendSettings { ComfyRoot = Path.Combine(_directory, "nope") };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.False(environment.ContainsKey("ZIV_AI_COMFY_ROOT"));
    }

    [Fact]
    public void BuildBackendEnvironment_Injects_LoraRoot_Even_When_Directory_Absent()
    {
        // AddIfSet (NOT AddDirectoryIfPresent): a configured-but-missing root must reach Python.
        var absent = Path.Combine(_directory, "no-such-loras");
        var settings = new BackendSettings { LoraRoot = absent };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.Equal(absent, environment["ZIV_AI_LORA_ROOT"]);
    }

    [Fact]
    public void BuildBackendEnvironment_Omits_LoraRoot_When_Empty()
    {
        var settings = new BackendSettings { LoraRoot = "   " };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory);

        Assert.DoesNotContain("ZIV_AI_LORA_ROOT", environment.Keys);
    }

    [Fact]
    public void BuildBackendEnvironment_Skips_Plugin_Keys_When_Registry_Null()
    {
        // Batch 1: the 2-arg overload injects no per-plugin key, even if plugins.json exists.
        File.WriteAllText(
            Path.Combine(_directory, "plugins.json"),
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"pose-map\", \"enabled_by_default\": true } ] }");

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(new BackendSettings(), _directory);

        Assert.Equal(Path.Combine(_directory, "plugins.json"), environment["ZIV_AI_PLUGINS_REGISTRY"]);
        Assert.DoesNotContain("ZIV_AI_PLUGIN_POSE_MAP", environment.Keys);
    }

    [Fact]
    public void BuildBackendEnvironment_Injects_Plugin_States_From_Registry_And_Settings()
    {
        File.WriteAllText(
            Path.Combine(_directory, "plugins.json"),
            "{ \"version\": \"1\", \"plugins\": ["
            + " { \"id\": \"pose-map\", \"dir\": \"plugins/pose-map\", \"enabled_by_default\": false },"
            + " { \"id\": \"tagger\", \"dir\": \"plugins/tagger\", \"enabled_by_default\": true } ] }");
        var registry = new PluginRegistry(Path.Combine(_directory, "plugins.json"));
        var settings = new BackendSettings
        {
            PluginStates = new Dictionary<string, bool> { ["pose-map"] = true },
        };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory, registry);

        Assert.Equal("1", environment["ZIV_AI_PLUGIN_POSE_MAP"]);
        Assert.Equal("1", environment["ZIV_AI_PLUGIN_TAGGER"]);
    }

    [Fact]
    public void BuildBackendEnvironment_Disables_Plugin_When_Settings_Says_So()
    {
        File.WriteAllText(
            Path.Combine(_directory, "plugins.json"),
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"pose-map\", \"enabled_by_default\": true } ] }");
        var registry = new PluginRegistry(Path.Combine(_directory, "plugins.json"));
        var settings = new BackendSettings
        {
            PluginStates = new Dictionary<string, bool> { ["pose-map"] = false },
        };

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(settings, _directory, registry);

        Assert.Equal("0", environment["ZIV_AI_PLUGIN_POSE_MAP"]);
    }

    [Fact]
    public void PluginEnvName_Normalizes_Ids()
    {
        Assert.Equal("ZIV_AI_PLUGIN_POSE_MAP", ZivAiEditor.App.AppContext.PluginEnvName("pose-map"));
        Assert.Equal("ZIV_AI_PLUGIN_SDPOSE_OOD", ZivAiEditor.App.AppContext.PluginEnvName("sdpose.ood"));
        Assert.Equal("ZIV_AI_PLUGIN_A_B9", ZivAiEditor.App.AppContext.PluginEnvName("A b9"));
        Assert.Equal("ZIV_AI_PLUGIN_X_1", ZivAiEditor.App.AppContext.PluginEnvName("x_1"));
    }

    [Fact]
    public void BuildBackendEnvironment_Plugins_Registry_Is_The_File_PluginRegistry_Reads()
    {
        // The injected path (consumed by Python's config.PLUGINS_REGISTRY_PATH) must be exactly
        // the file the C# registry object reads, so both sides share one registry.
        File.WriteAllText(
            Path.Combine(_directory, "plugins.json"),
            "{ \"version\": \"1\", \"plugins\": [ { \"id\": \"pose-map\", \"dir\": \"plugins/pose-map\" } ] }");
        var registry = new PluginRegistry(Path.Combine(_directory, "plugins.json"));

        var environment = ZivAiEditor.App.AppContext.BuildBackendEnvironment(
            new BackendSettings(), _directory, registry);

        var injected = environment["ZIV_AI_PLUGINS_REGISTRY"];
        Assert.Equal(Path.Combine(_directory, "plugins.json"), injected);
        Assert.NotNull(new PluginRegistry(injected).Get("pose-map"));
        // Relative plugin dirs use the program directory (same base as C# ResolveDirectory).
        Assert.Equal(System.AppContext.BaseDirectory, environment["ZIV_AI_PLUGINS_BASE_DIR"]);
    }
}
