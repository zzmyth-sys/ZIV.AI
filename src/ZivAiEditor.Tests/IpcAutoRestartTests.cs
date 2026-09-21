using System.Diagnostics;
using System.Runtime.Versioning;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 4 coverage: backend self-healing. Heartbeats stop (the process is
/// killed) -> <see cref="IpcInferenceClient.HeartbeatLost"/> -> the manager
/// restarts the backend with a fresh pipe and a new task succeeds; repeated
/// losses exhaust the restart budget and mark the backend failed. Drives the
/// real GPU backend, so budgets are generous and the class shares the serial
/// GPU collection (Z18).
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(GpuSerialCollection.Name)]
public class IpcAutoRestartTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(300);

    [Fact]
    public async Task Restart_After_HeartbeatLost_Recovers_And_New_Task_Succeeds()
    {
        var input = Path.Combine(FindRepositoryRoot(), "_test_step2", "input_test_512.png");
        Assert.True(File.Exists(input), $"Input image not found: {input}");

        var firstOutput = NewTempPath("zivai_step4_before");
        var secondOutput = NewTempPath("zivai_step4_after");

        var options = CreateOptions(new Dictionary<string, string>
        {
            ["ZIV_AI_HEARTBEAT_INTERVAL_S"] = "1",
        }, heartbeatLostAfterMs: 3_000, restartBackoffMs: 200);

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);
        using var timeout = new CancellationTokenSource(Budget);

        var handle = await client.SubmitInpaintAsync(CreateRequest(input, firstOutput), null, timeout.Token);
        Assert.Equal(TaskStatus.Succeeded, handle.Status);
        Assert.True(File.Exists(firstOutput), "the pre-restart submit should produce output");

        var restarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Restarted += () => restarted.TrySetResult(true);

        var oldPid = manager.ProcessId;
        Assert.NotNull(oldPid);
        var stopwatch = Stopwatch.StartNew();
        KillProcessTree(oldPid!.Value);

        var recovered = await restarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        stopwatch.Stop();
        Assert.True(recovered, "the manager should report a successful restart");
        Console.WriteLine($"[step4] heartbeat-loss detection + restart = {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"detection + restart took {stopwatch.Elapsed.TotalSeconds:F1}s, expected under 30s");
        Assert.NotEqual(oldPid, manager.ProcessId);
        Assert.True(manager.IsProcessRunning, "a fresh Python process should be running");

        var handle2 = await client.SubmitInpaintAsync(CreateRequest(input, secondOutput), null, timeout.Token);
        Assert.Equal(TaskStatus.Succeeded, handle2.Status);
        Assert.True(File.Exists(secondOutput), "the post-restart submit should produce output");

        var (width, height) = ReadPngSize(File.ReadAllBytes(secondOutput));
        Assert.Equal(512, width);
        Assert.Equal(512, height);
    }

    [Fact]
    public async Task Repeated_HeartbeatLoss_Exceeds_Budget_Then_Fails()
    {
        var options = CreateOptions(new Dictionary<string, string>
        {
            ["ZIV_AI_HEARTBEAT_INTERVAL_S"] = "1",
        }, heartbeatLostAfterMs: 2_000, restartBackoffMs: 100, maxRestartAttempts: 3);

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.RestartFailed += _ => failed.TrySetResult(true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await client.CheckHealthAsync(timeout.Token);

        // Each kill consumes one restart attempt; the counter only resets on a
        // successful submit, so four consecutive losses exceed the budget.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var pid = manager.ProcessId;
            Assert.NotNull(pid);
            KillProcessTree(pid!.Value);

            await WaitUntilAsync(
                () => manager.State == PythonBackendState.Running
                      && manager.IsProcessRunning
                      && manager.ProcessId != pid,
                TimeSpan.FromSeconds(30),
                $"restart {attempt} should come back up");
        }

        var lastPid = manager.ProcessId;
        Assert.NotNull(lastPid);
        KillProcessTree(lastPid!.Value);

        var fired = await failed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(fired, "RestartFailed should fire once the budget is exhausted");
        Assert.Equal(PythonBackendState.Failed, manager.State);
        Assert.False(manager.IsProcessRunning, "the backend must not be restarted after failure");

        await Task.Delay(3_000);
        Assert.Equal(PythonBackendState.Failed, manager.State);

        // A failed backend must not keep spawning processes.
        Assert.False(manager.IsProcessRunning);
    }

    private static void KillProcessTree(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(10_000);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail(message);
    }

    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (width, height);
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
        IReadOnlyDictionary<string, string> environment,
        int heartbeatLostAfterMs,
        int restartBackoffMs,
        int maxRestartAttempts = 3)
    {
        var root = FindRepositoryRoot();
        var env = new Dictionary<string, string>(environment)
        {
            ["ZIV_AI_MAX_RESOLUTION"] = "512",
        };
        return new PythonBackendOptions
        {
            PipeName = "zivai.infer.test." + Guid.NewGuid().ToString("N"),
            PythonExe = Path.Combine(root, "Comfyui", "python_embeded", "python.exe"),
            Script = Path.Combine(root, "python", "server", "main.py"),
            Environment = env,
            HeartbeatLostAfterMs = heartbeatLostAfterMs,
            RestartBackoffMs = restartBackoffMs,
            MaxRestartAttempts = maxRestartAttempts,
            AutoRestartEnabled = true,
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
