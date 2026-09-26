using System;
using System.IO;
using System.Linq;
using Xunit;
using ZivAiEditor.App;

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
}
