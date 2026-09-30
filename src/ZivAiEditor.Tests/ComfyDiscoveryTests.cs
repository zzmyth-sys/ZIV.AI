using System;
using System.IO;
using System.Linq;
using Xunit;
using ZivAiEditor.App;

namespace ZivAiEditor.Tests;

/// <summary>
/// Plan B: the pure ComfyUI-root discovery chain and path derivations. Every filesystem probe is
/// injected, so the tests are headless with no real directories.
/// </summary>
public class ComfyDiscoveryTests
{
    private static Func<string, bool> Dirs(params string[] existing)
        => path => existing.Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));

    private static Func<string, bool> Files(params string[] existing)
        => path => existing.Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Configured_Value_Wins_When_It_Exists()
    {
        const string configured = @"C:\configured\ComfyUI";
        var root = ComfyDiscovery.DiscoverComfyRoot(
            configured, @"D:\app", null,
            Dirs(configured, @"D:\app\Comfyui\ComfyUI"), Files());

        Assert.Equal(configured, root);
    }

    [Fact]
    public void Program_Directory_Portable_Layout_Used_When_Unconfigured()
    {
        const string appDir = @"D:\app";
        var expected = Path.Combine(appDir, "Comfyui", "ComfyUI");

        var root = ComfyDiscovery.DiscoverComfyRoot(null, appDir, null, Dirs(expected), Files());

        Assert.Equal(expected, root);
    }

    [Fact]
    public void Python_Exe_Sibling_Used_When_Program_Layout_Missing()
    {
        const string pythonExe = @"D:\portable\python_embeded\python.exe";
        var expected = Path.GetFullPath(Path.Combine(@"D:\portable\python_embeded", "..", "ComfyUI"));

        var root = ComfyDiscovery.DiscoverComfyRoot(null, @"D:\app", pythonExe, Dirs(expected), Files());

        Assert.Equal(expected, root);
    }

    [Fact]
    public void Repository_Relative_Ancestor_Used_When_Guarded_By_Frozen()
    {
        const string repo = @"D:\repo";
        var appDir = Path.Combine(repo, "src", "ZivAiEditor.App", "bin", "Debug");
        var comfy = Path.Combine(repo, "Comfyui", "ComfyUI");
        var frozen = Path.Combine(repo, "DOC", "FROZEN.md");

        var root = ComfyDiscovery.DiscoverComfyRoot(null, appDir, null, Dirs(comfy), Files(frozen));

        Assert.Equal(comfy, root);
    }

    [Fact]
    public void Repository_Relative_Ancestor_Ignored_Without_Frozen_Guard()
    {
        const string repo = @"D:\repo";
        var appDir = Path.Combine(repo, "src", "ZivAiEditor.App", "bin", "Debug");
        var comfy = Path.Combine(repo, "Comfyui", "ComfyUI");

        var root = ComfyDiscovery.DiscoverComfyRoot(null, appDir, null, Dirs(comfy), Files());

        Assert.Null(root);
    }

    [Fact]
    public void All_Layers_Fail_Returns_Null()
    {
        var root = ComfyDiscovery.DiscoverComfyRoot(null, @"D:\app", @"D:\p\python.exe", Dirs(), Files());

        Assert.Null(root);
    }

    [Fact]
    public void Precedence_Configured_Then_Program_Then_Python_Then_Repo()
    {
        const string repo = @"D:\repo";
        var appDir = Path.Combine(repo, "src", "app");
        const string configured = @"C:\configured\ComfyUI";
        var portable = Path.Combine(appDir, "Comfyui", "ComfyUI");
        const string pythonExe = @"D:\portable\python_embeded\python.exe";
        var pythonSibling = Path.GetFullPath(Path.Combine(@"D:\portable\python_embeded", "..", "ComfyUI"));
        var repoComfy = Path.Combine(repo, "Comfyui", "ComfyUI");
        var frozen = Path.Combine(repo, "DOC", "FROZEN.md");

        Assert.Equal(configured, ComfyDiscovery.DiscoverComfyRoot(
            configured, appDir, pythonExe, Dirs(configured, portable, pythonSibling, repoComfy), Files(frozen)));
        Assert.Equal(portable, ComfyDiscovery.DiscoverComfyRoot(
            null, appDir, pythonExe, Dirs(portable, pythonSibling, repoComfy), Files(frozen)));
        Assert.Equal(pythonSibling, ComfyDiscovery.DiscoverComfyRoot(
            null, appDir, pythonExe, Dirs(pythonSibling, repoComfy), Files(frozen)));
        Assert.Equal(repoComfy, ComfyDiscovery.DiscoverComfyRoot(
            null, appDir, null, Dirs(repoComfy), Files(frozen)));
    }

    [Fact]
    public void Derivations_Map_Under_Comfy_Root()
    {
        const string root = @"D:\c\ComfyUI";

        Assert.Equal(Path.Combine(root, "models"), ComfyDiscovery.DeriveModelRoot(root));
        Assert.Equal(Path.Combine(root, "models", "loras"), ComfyDiscovery.DeriveLoraRoot(root));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "..", "python_embeded", "python.exe")),
            ComfyDiscovery.DerivePythonExe(root));
    }

    [Fact]
    public void Derivations_Return_Null_Without_Root()
    {
        Assert.Null(ComfyDiscovery.DeriveModelRoot(null));
        Assert.Null(ComfyDiscovery.DeriveLoraRoot(null));
        Assert.Null(ComfyDiscovery.DerivePythonExe(null));
    }

    [Fact]
    public void ShouldPrompt_True_Only_When_No_Root()
    {
        Assert.True(ComfyDiscovery.ShouldPromptForComfyRoot(null));
        Assert.True(ComfyDiscovery.ShouldPromptForComfyRoot("   "));
        Assert.False(ComfyDiscovery.ShouldPromptForComfyRoot(@"D:\c\ComfyUI"));
    }
}
