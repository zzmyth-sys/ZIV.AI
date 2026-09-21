using System.Diagnostics;
using System.Runtime.Versioning;
using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

[SupportedOSPlatform("windows")]
public class IpcSmokeTests
{
    [Fact]
    public async Task PingPong_Returns_Health_With_ModelStatus()
    {
        var options = CreateOptions();
        Assert.True(File.Exists(options.PythonExe), $"Python executable not found: {options.PythonExe}");
        Assert.True(File.Exists(options.Script), $"Python script not found: {options.Script}");

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var health = await client.CheckHealthAsync(timeout.Token);

        Assert.Equal("ok", health.Status);
        Assert.Equal("not_loaded", health.ModelStatus);
        Assert.True(health.VramUsedMb >= 0, "vram_used_mb should be a non-negative number.");
        Assert.NotEmpty(health.Models);
        Assert.True(manager.IsPipeConnected, "C# server pipe should report a live connection.");
        Assert.True(manager.IsProcessRunning, "Python process should be running after a successful round trip.");
    }

    [Fact]
    public async Task Python_Exits_And_Clears_When_Pipe_Closed()
    {
        var options = CreateOptions();
        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await client.CheckHealthAsync(timeout.Token);
        }

        var pid = manager.ProcessId;
        Assert.NotNull(pid);
        Assert.True(IsAlive(pid!.Value));

        var stopwatch = Stopwatch.StartNew();
        await manager.StopAsync();
        stopwatch.Stop();

        Assert.False(manager.IsProcessRunning, "Python process should have exited after the pipe closed.");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(7),
            $"Python exit took {stopwatch.Elapsed.TotalSeconds:F2}s, expected under ShutdownTimeoutMs (5s).");
        Assert.False(IsAlive(pid.Value), $"Orphan Python process {pid.Value} is still alive.");
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static PythonBackendOptions CreateOptions()
    {
        var root = FindRepositoryRoot();
        return new PythonBackendOptions
        {
            PipeName = "zivai.infer.test." + Guid.NewGuid().ToString("N"),
            PythonExe = Path.Combine(root, "Comfyui", "python_embeded", "python.exe"),
            Script = Path.Combine(root, "python", "server", "main.py"),
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, "DOC", "FROZEN.md");
            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the ZIV.AI repository root from " + AppContext.BaseDirectory);
    }
}
