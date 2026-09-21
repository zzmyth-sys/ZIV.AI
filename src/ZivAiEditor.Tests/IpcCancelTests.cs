using System.Runtime.Versioning;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 2.4 cancellation coverage over the real IPC pipe: submit -> sampling
/// -> cancel -> canceled, plus the no-active-task case. Drives the real GPU
/// backend, so budgets are generous and the class shares the serial GPU
/// collection (Z18).
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(GpuSerialCollection.Name)]
public class IpcCancelTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(300);

    [Fact]
    public async Task Cancel_During_Sampling_Returns_Canceled_And_Keeps_Model_Loaded()
    {
        var input = Path.Combine(FindRepositoryRoot(), "_test_step2", "input_test_512.png");
        Assert.True(File.Exists(input), $"Input image not found: {input}");

        var canceledOutput = NewTempPath("zivai_step24_cancel");
        var followUpOutput = NewTempPath("zivai_step24_after");

        var options = CreateOptions();
        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var samplingFrames = 0;
        client.ProgressReceived += detail =>
        {
            if (detail.Stage == "sampling" && detail.Message == "sampling")
            {
                Interlocked.Increment(ref samplingFrames);
            }
        };

        using var timeout = new CancellationTokenSource(Budget);
        var submitTask = client.SubmitInpaintAsync(
            CreateRequest(input, canceledOutput),
            progress: null,
            timeout.Token);

        await WaitUntilAsync(
            () => Volatile.Read(ref samplingFrames) >= 2,
            TimeSpan.FromSeconds(120));

        var taskId = client.ActiveTaskId;
        Assert.False(string.IsNullOrEmpty(taskId), "the client should expose the in-flight task id");

        var canceled = await client.CancelTaskAsync(taskId!);
        Assert.True(canceled, "the backend should acknowledge the cancellation");

        var handle = await submitTask;
        Assert.Equal(TaskStatus.Canceled, handle.Status);
        Assert.Null(client.ActiveTaskId);
        Assert.True(manager.IsProcessRunning, "the backend process must survive a cancel");
        Assert.False(File.Exists(canceledOutput), "a canceled task must not write an output file");

        // The model stays loaded (Z21 is a later step), so a second submit works.
        var handle2 = await client.SubmitInpaintAsync(
            CreateRequest(input, followUpOutput),
            progress: null,
            timeout.Token);
        Assert.Equal(TaskStatus.Succeeded, handle2.Status);
        Assert.True(File.Exists(followUpOutput), "the follow-up submit should produce output");
    }

    [Fact]
    public async Task Cancel_Without_Active_Task_Returns_False()
    {
        var options = CreateOptions();
        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var canceled = await client.CancelTaskAsync("no-such-task");
        Assert.False(canceled);
        Assert.Null(client.ActiveTaskId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Condition not met within {timeout.TotalSeconds:F0}s.");
    }

    private static InpaintRequest CreateRequest(string input, string output)
        => new()
        {
            ImagePath = input,
            Prompt = "make the background a snowy mountain landscape, keep the subject unchanged",
            Steps = 20,
            Seed = 42,
            Denoise = 1.0,
            OutputPath = output,
        };

    private static string NewTempPath(string prefix)
        => Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N") + ".png");

    private static PythonBackendOptions CreateOptions()
    {
        var root = FindRepositoryRoot();
        return new PythonBackendOptions
        {
            PipeName = "zivai.infer.test." + Guid.NewGuid().ToString("N"),
            PythonExe = Path.Combine(root, "Comfyui", "python_embeded", "python.exe"),
            Script = Path.Combine(root, "python", "server", "main.py"),
            Environment = new Dictionary<string, string>
            {
                ["ZIV_AI_MAX_RESOLUTION"] = "512",
            },
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
