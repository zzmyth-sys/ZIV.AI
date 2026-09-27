using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ZivAiEditor.App;

namespace ZivAiEditor.Tests;

/// <summary>
/// B7: <see cref="SettingsWriter"/> key-value replacement (no GPU / Python). Each test runs in
/// its own temp directory so the repository settings file is never touched.
/// </summary>
public class SettingsWriterTests : IDisposable
{
    private readonly string _directory;

    public SettingsWriterTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_cfg_" + Guid.NewGuid().ToString("N"));
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

    private string[] Read() => File.ReadAllLines(SettingsPath);

    private static string Full(string relative) => Path.GetFullPath(relative);

    [Fact]
    public void WriteModelPaths_Replaces_Existing_Keys()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[models]",
            "dit_path = old_dit.safetensors",
            "te_path = old_te.safetensors",
            "vae_path = old_vae.safetensors",
        });

        SettingsWriter.WriteModelPaths(
            SettingsPath, "new_dit.safetensors", "new_te.safetensors", "new_vae.safetensors");

        var lines = Read();
        Assert.Equal("dit_path = " + Full("new_dit.safetensors"), lines[1]);
        Assert.Equal("te_path = " + Full("new_te.safetensors"), lines[2]);
        Assert.Equal("vae_path = " + Full("new_vae.safetensors"), lines[3]);
    }

    [Fact]
    public void WriteModelPaths_Appends_Missing_Keys_To_Existing_Models_Section()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[models]",
            "dit_path = d.safetensors",
            "",
            "[backend]",
            "pipe_name = zivai.infer.v1",
        });

        SettingsWriter.WriteModelPaths(
            SettingsPath, "d.safetensors", "t.safetensors", "v.safetensors");

        var lines = Read();
        Assert.Equal("[models]", lines[0]);
        Assert.Equal("dit_path = " + Full("d.safetensors"), lines[1]);
        Assert.Contains("te_path = " + Full("t.safetensors"), lines);
        Assert.Contains("vae_path = " + Full("v.safetensors"), lines);
        Assert.Contains("[backend]", lines);
        Assert.Contains("pipe_name = zivai.infer.v1", lines);
    }

    [Fact]
    public void WriteModelPaths_Creates_Models_Section_When_Absent()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[backend]",
            "pipe_name = zivai.infer.v1",
        });

        SettingsWriter.WriteModelPaths(
            SettingsPath, "d.safetensors", "t.safetensors", "v.safetensors");

        var lines = Read();
        Assert.Equal("[backend]", lines[0]);
        Assert.Equal("pipe_name = zivai.infer.v1", lines[1]);
        Assert.Equal(string.Empty, lines[2]);
        Assert.Equal("[models]", lines[3]);
        Assert.Equal("dit_path = " + Full("d.safetensors"), lines[4]);
        Assert.Equal("te_path = " + Full("t.safetensors"), lines[5]);
        Assert.Equal("vae_path = " + Full("v.safetensors"), lines[6]);
    }

    [Fact]
    public void WritePythonExe_Replaces_Key_And_Preserves_Comments_And_Order()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "; top comment",
            "[models]",
            "; inside comment",
            "dit_path = d.safetensors",
            "[backend]",
            "python_exe = C:\\old\\python.exe",
        });

        SettingsWriter.WritePythonExe(SettingsPath, "C:\\new\\python.exe");

        var lines = Read();
        Assert.Equal("; top comment", lines[0]);
        Assert.Equal("[models]", lines[1]);
        Assert.Equal("; inside comment", lines[2]);
        Assert.Equal("dit_path = d.safetensors", lines[3]);
        Assert.Equal("[backend]", lines[4]);
        Assert.Equal("python_exe = C:\\new\\python.exe", lines[5]);
    }

    [Fact]
    public void WritePythonExe_Creates_File_And_Backend_Section_When_Missing()
    {
        SettingsWriter.WritePythonExe(SettingsPath, "C:\\py\\python.exe");

        Assert.True(File.Exists(SettingsPath));
        var lines = Read();
        Assert.Equal("[backend]", lines[0]);
        Assert.Equal("python_exe = C:\\py\\python.exe", lines[1]);
    }

    [Fact]
    public void WriteScript_Replaces_Backend_Script_Key()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[backend]",
            "pipe_name = zivai.infer.v1",
            "script = old.py",
        });

        SettingsWriter.WriteScript(SettingsPath, "new.py");

        var lines = Read();
        Assert.Equal("[backend]", lines[0]);
        Assert.Equal("pipe_name = zivai.infer.v1", lines[1]);
        Assert.Equal("script = " + Full("new.py"), lines[2]);
    }

    [Fact]
    public void WriteComfyRoot_Replaces_Backend_Key_And_Normalizes_Relative()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[backend]",
            @"comfy_root = C:\old\ComfyUI",
        });

        SettingsWriter.WriteComfyRoot(SettingsPath, @"rel\ComfyUI");

        var lines = Read();
        Assert.Equal("[backend]", lines[0]);
        Assert.Equal("comfy_root = " + Full(@"rel\ComfyUI"), lines[1]);
    }

    [Fact]
    public void WriteModelPaths_Normalizes_Relative_Values_To_Absolute()
    {
        File.WriteAllLines(SettingsPath, new[] { "[models]", "dit_path =", "te_path =", "vae_path =" });

        SettingsWriter.WriteModelPaths(
            SettingsPath, "rel\\dit.safetensors", "rel\\te.safetensors", "rel\\vae.safetensors");

        var lines = Read();
        Assert.Equal("dit_path = " + Full("rel\\dit.safetensors"), lines[1]);
        Assert.True(Path.IsPathRooted(lines[1]["dit_path = ".Length..]));
        Assert.Equal("te_path = " + Full("rel\\te.safetensors"), lines[2]);
        Assert.Equal("vae_path = " + Full("rel\\vae.safetensors"), lines[3]);
    }

    [Fact]
    public void WriteModelPaths_Writes_Empty_Value_When_Cleared()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[models]",
            "dit_path = d.safetensors",
            "te_path = t.safetensors",
            "vae_path = v.safetensors",
        });

        SettingsWriter.WriteModelPaths(SettingsPath, "", "", "");

        var lines = Read();
        Assert.Equal("dit_path =", lines[1]);
        Assert.Equal("te_path =", lines[2]);
        Assert.Equal("vae_path =", lines[3]);
    }

    [Fact]
    public void WritePluginStates_Writes_Raw_Flag_Not_A_Path()
    {
        SettingsWriter.WritePluginStates(SettingsPath, new Dictionary<string, string>
        {
            ["pose-map"] = "1",
            ["sdpose.ood"] = "0",
        });

        var lines = Read();
        Assert.Equal("[plugins]", lines[0]);
        Assert.Contains("pose-map = 1", lines);
        Assert.Contains("sdpose.ood = 0", lines);
        // The value must never be path-normalized (it is a flag, not a file path).
        Assert.DoesNotContain(lines, line => line.Contains(Full("1"), StringComparison.Ordinal));
    }

    [Fact]
    public void WritePluginStates_Preserves_Comments_And_Other_Sections()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "; top",
            "[backend]",
            "pipe_name = zivai.infer.v1",
            "",
            "[plugins]",
            "; plugin flags",
            "pose-map = 1",
        });

        SettingsWriter.WritePluginStates(SettingsPath, new Dictionary<string, string> { ["pose-map"] = "0" });

        var lines = Read();
        Assert.Equal("; top", lines[0]);
        Assert.Contains("; plugin flags", lines);
        Assert.Contains("pipe_name = zivai.infer.v1", lines);
        Assert.Contains("pose-map = 0", lines);
        Assert.DoesNotContain("pose-map = 1", lines);
    }

    [Fact]
    public void WriteModelPaths_Is_Atomic_And_Overwrites_Without_Temp_Artifacts()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "[models]",
            "dit_path = old.safetensors",
            "te_path = old.safetensors",
            "vae_path = old.safetensors",
        });

        SettingsWriter.WriteModelPaths(SettingsPath, "a.safetensors", "b.safetensors", "c.safetensors");
        SettingsWriter.WriteModelPaths(SettingsPath, "d.safetensors", "e.safetensors", "f.safetensors");

        Assert.DoesNotContain(
            Directory.GetFiles(_directory),
            file => file.Contains(".tmp-", StringComparison.Ordinal));
        var lines = Read();
        Assert.Equal("dit_path = " + Full("d.safetensors"), lines[1]);
        Assert.Equal("te_path = " + Full("e.safetensors"), lines[2]);
        Assert.Equal("vae_path = " + Full("f.safetensors"), lines[3]);
    }
}
