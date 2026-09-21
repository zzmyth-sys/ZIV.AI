using System.Runtime.Versioning;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 3 coverage: idle unload (Z21) and heartbeats. The idle test drives the
/// real GPU backend; the heartbeat tests only ping. Serialized with the other
/// GPU classes (Z18).
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(GpuSerialCollection.Name)]
public class IpcIdleUnloadTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(300);

    [Fact]
    public async Task Idle_Timeout_Unloads_Then_Reloads_On_Next_Submit()
    {
        var input = Path.Combine(FindRepositoryRoot(), "_test_step2", "input_test_512.png");
        Assert.True(File.Exists(input), $"Input image not found: {input}");

        var output = NewTempPath("zivai_step3_idle");
        var options = CreateOptions(new Dictionary<string, string>
        {
            ["ZIV_AI_IDLE_UNLOAD_S"] = "3",
            ["ZIV_AI_IDLE_CHECK_S"] = "1",
        });

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);
        using var timeout = new CancellationTokenSource(Budget);

        var handle = await client.SubmitInpaintAsync(CreateRequest(input, output), null, timeout.Token);
        Assert.Equal(TaskStatus.Succeeded, handle.Status);
        Assert.True(File.Exists(output), "first submit should produce output");

        // The idle watcher should unload the model without killing the process.
        await WaitUntilAsync(
            async () => (await client.CheckHealthAsync(timeout.Token)).ModelStatus == "not_loaded",
            TimeSpan.FromSeconds(60));
        Assert.True(manager.IsProcessRunning, "process must survive an idle unload");

        // VRAM falls back after the unload (NVML; WDDM lags briefly).
        await WaitUntilAsync(
            async () => (await client.CheckHealthAsync(timeout.Token)).VramUsedMb < 3000,
            TimeSpan.FromSeconds(15),
            "VRAM should fall back after idle unload");

        // The next submit reloads lazily and succeeds.
        var output2 = NewTempPath("zivai_step3_reload");
        var handle2 = await client.SubmitInpaintAsync(CreateRequest(input, output2), null, timeout.Token);
        Assert.Equal(TaskStatus.Succeeded, handle2.Status);
        Assert.True(File.Exists(output2), "reload submit should produce output");
    }

    [Fact]
    public async Task Heartbeat_Is_Received_Periodically()
    {
        var options = CreateOptions(
            new Dictionary<string, string> { ["ZIV_AI_HEARTBEAT_INTERVAL_S"] = "1" },
            heartbeatLostAfterMs: 10_000);

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var heartbeats = 0;
        client.HeartbeatReceived += () => Interlocked.Increment(ref heartbeats);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.CheckHealthAsync(timeout.Token);

        await WaitUntilAsync(
            () => Task.FromResult(Volatile.Read(ref heartbeats) >= 2),
            TimeSpan.FromSeconds(10),
            "expected at least two heartbeats");
        Assert.NotNull(client.LastHeartbeatAt);
    }

    [Fact]
    public async Task HeartbeatLost_Fires_When_Heartbeats_Stop()
    {
        var options = CreateOptions(
            new Dictionary<string, string> { ["ZIV_AI_HEARTBEAT_DISABLED"] = "1" },
            heartbeatLostAfterMs: 2_000);

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var lost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.HeartbeatLost += () => lost.TrySetResult(true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.CheckHealthAsync(timeout.Token);

        var fired = await lost.Task.WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(fired, "HeartbeatLost should fire when heartbeats stop");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string? message = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail(message ?? $"Condition not met within {timeout.TotalSeconds:F0}s.");
    }

    private static InpaintRequest CreateRequest(string input, string output)
        => new()
        {
            ImagePath = input,
            Prompt = "make the background a snowy mountain landscape, keep the subject unchanged",
            Steps = 8,
            Seed = 42,
            Denoise = 1.0,
            OutputPath = output,
        };

    private static string NewTempPath(string prefix)
        => Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N") + ".png");

    private static PythonBackendOptions CreateOptions(
        IReadOnlyDictionary<string, string>? environment = null,
        int heartbeatLostAfterMs = 30_000)
    {
        var root = FindRepositoryRoot();
        var env = new Dictionary<string, string>(environment ?? new Dictionary<string, string>())
        {
            // Keep these Step 3 tests at 512 (fast, low VRAM); the production
            // default is 1024 (see IpcInferenceTests.Submit_1024_*).
            ["ZIV_AI_MAX_RESOLUTION"] = "512",
        };
        return new PythonBackendOptions
        {
            PipeName = "zivai.infer.test." + Guid.NewGuid().ToString("N"),
            PythonExe = Path.Combine(root, "Comfyui", "python_embeded", "python.exe"),
            Script = Path.Combine(root, "python", "server", "main.py"),
            Environment = env,
            HeartbeatLostAfterMs = heartbeatLostAfterMs,
            // Step 3 tests observe HeartbeatLost directly; Step 4 auto-restart
            // would otherwise respawn a heartbeat-disabled backend.
            AutoRestartEnabled = false,
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DOC", "FROZEN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the ZIV.AI repository root from " + AppContext.BaseDirectory);
    }
}
