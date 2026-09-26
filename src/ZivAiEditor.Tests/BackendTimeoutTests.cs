using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// CPU-only coverage for the timeout / watchdog / mechanism-failure policy over a
/// fake in-process backend (connected named-pipe pair, no GPU / no Python):
/// load timeout forwards a cancel, sampling is unbounded (L4), the L1 watchdog
/// recovers a stalled sampling task, loading is not watched (D2), and the L2
/// AcceleratorError path recovers + logs (L3).
/// </summary>
[SupportedOSPlatform("windows")]
public class BackendTimeoutTests
{
    [Fact]
    public async Task LoadTimeout_Forwards_Cancel_And_Drops_Late_Result()
    {
        var options = NewOptions(modelLoadTimeoutMs: 300);
        using var server = NewPipeServer(options.PipeName);
        using var peer = NewPipeClient(options.PipeName);
        await ConnectAsync(server, peer);

        await using var manager = new PythonProcessManager(options);
        manager.EnsureStartedOverride = _ => Task.FromResult<Stream>(server);
        using var client = new IpcInferenceClient(manager);

        var results = new List<InferenceResultDetail>();
        client.ResultReceived += value => { lock (results) { results.Add(value); } };

        var taskId = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelJson = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var peerTask = Task.Run(async () =>
        {
            var submit = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            var id = ReadTaskId(submit);
            taskId.TrySetResult(id);

            var cancel = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            cancelJson.TrySetResult(cancel is null ? null : Encoding.UTF8.GetString(cancel.Value.Payload));
            await IpcFraming.WriteJsonAsync(peer, Frame("canceled", id), CancellationToken.None);
        });

        var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.SubmitInpaintAsync(CreateRequest(), progress: null, CancellationToken.None));
        Assert.Contains("sampling", ex.Message, StringComparison.OrdinalIgnoreCase);

        var id2 = await taskId.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelPayload = await cancelJson.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(cancelPayload);
        Assert.Contains("\"cancel\"", cancelPayload);

        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));

        await IpcFraming.WriteJsonAsync(peer, Frame("result", id2), CancellationToken.None);
        await Task.Delay(200);
        lock (results)
        {
            Assert.Empty(results);
        }
    }

    [Fact]
    public async Task Sampling_Phase_Is_Not_Bounded_By_The_Load_Timeout()
    {
        var options = NewOptions(modelLoadTimeoutMs: 3_000);
        using var server = NewPipeServer(options.PipeName);
        using var peer = NewPipeClient(options.PipeName);
        await ConnectAsync(server, peer);

        await using var manager = new PythonProcessManager(options);
        manager.EnsureStartedOverride = _ => Task.FromResult<Stream>(server);
        using var client = new IpcInferenceClient(manager);

        var samplingSent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var peerTask = Task.Run(async () =>
        {
            var submit = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            var id = ReadTaskId(submit);

            await IpcFraming.WriteJsonAsync(peer, Frame("accepted", id), CancellationToken.None);
            await IpcFraming.WriteJsonAsync(peer, Progress(id, "loading_model"), CancellationToken.None);
            await IpcFraming.WriteJsonAsync(peer, Progress(id, "sampling"), CancellationToken.None);
            samplingSent.TrySetResult(id);

            await writeResult.Task;
            await IpcFraming.WriteJsonAsync(peer, Frame("result", id), CancellationToken.None);
        });

        var submitTask = client.SubmitInpaintAsync(CreateRequest(), progress: null, CancellationToken.None);
        await samplingSent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // L4: the load timer is disabled on the first sampling frame, so waiting past
        // the load budget must NOT fault the submit.
        await Task.Delay(3_500);
        Assert.False(submitTask.IsCompleted, "the sampling phase must be unbounded (L4)");

        writeResult.TrySetResult();
        var handle = await submitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.Succeeded, handle.Status);
        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Watchdog_Recovers_A_Stalled_Sampling_Task()
    {
        var options = NewOptions(modelLoadTimeoutMs: 60_000, stuckTimeoutMs: 600, cancelConfirmTimeoutMs: 600);
        using var server = NewPipeServer(options.PipeName);
        using var peer = NewPipeClient(options.PipeName);
        await ConnectAsync(server, peer);

        await using var manager = new PythonProcessManager(options);
        manager.EnsureStartedOverride = _ => Task.FromResult<Stream>(server);
        using var client = new IpcInferenceClient(manager);
        var logPath = NewLogPath();
        client.FailureLogPath = logPath;

        var recovery = new TaskCompletionSource<IpcInferenceClient.StuckRecoveryInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StuckRecoveryTriggered += info => recovery.TrySetResult(info);

        var cancelSeen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerTask = Task.Run(async () =>
        {
            var submit = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            var id = ReadTaskId(submit);
            await IpcFraming.WriteJsonAsync(peer, Frame("accepted", id), CancellationToken.None);
            await IpcFraming.WriteJsonAsync(peer, Progress(id, "sampling"), CancellationToken.None);

            // Stall: send nothing else. The watchdog must forward a cancel.
            var cancel = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            cancelSeen.TrySetResult(cancel is null ? null : Encoding.UTF8.GetString(cancel.Value.Payload));
            await IpcFraming.WriteJsonAsync(peer, Frame("canceled", id), CancellationToken.None);
        });

        var submitTask = client.SubmitInpaintAsync(CreateRequest(), progress: null, CancellationToken.None);

        var info = await recovery.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("stuck", info.Reason);

        var cancelPayload = await cancelSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(cancelPayload);
        Assert.Contains("\"cancel\"", cancelPayload);

        var handle = await submitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.Canceled, handle.Status);
        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(File.Exists(logPath));
        var line = File.ReadAllLines(logPath).Single();
        Assert.Contains("\"reason\":\"stuck\"", line);
        Assert.Contains("\"action\":\"cancel_ok\"", line);
    }

    [Fact]
    public async Task Watchdog_Does_Not_Fire_During_The_Load_Phase()
    {
        var options = NewOptions(modelLoadTimeoutMs: 60_000, stuckTimeoutMs: 500);
        using var server = NewPipeServer(options.PipeName);
        using var peer = NewPipeClient(options.PipeName);
        await ConnectAsync(server, peer);

        await using var manager = new PythonProcessManager(options);
        manager.EnsureStartedOverride = _ => Task.FromResult<Stream>(server);
        using var client = new IpcInferenceClient(manager);
        client.FailureLogPath = NewLogPath();

        var recovered = false;
        client.StuckRecoveryTriggered += _ => recovered = true;

        var peerTask = Task.Run(async () =>
        {
            var submit = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            var id = ReadTaskId(submit);
            await IpcFraming.WriteJsonAsync(peer, Frame("accepted", id), CancellationToken.None);
            await IpcFraming.WriteJsonAsync(peer, Progress(id, "loading_model"), CancellationToken.None);
            await Task.Delay(1_800);
            await IpcFraming.WriteJsonAsync(peer, Frame("result", id), CancellationToken.None);
        });

        var submitTask = client.SubmitInpaintAsync(CreateRequest(), progress: null, CancellationToken.None);

        await Task.Delay(1_400);
        Assert.False(recovered, "the load phase must not be watched (D2)");

        var handle = await submitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TaskStatus.Succeeded, handle.Status);
        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Mechanism_Error_Recovers_And_Logs()
    {
        var options = NewOptions(modelLoadTimeoutMs: 60_000, stuckTimeoutMs: 60_000);
        using var server = NewPipeServer(options.PipeName);
        using var peer = NewPipeClient(options.PipeName);
        await ConnectAsync(server, peer);

        await using var manager = new PythonProcessManager(options);
        manager.EnsureStartedOverride = _ => Task.FromResult<Stream>(server);
        using var client = new IpcInferenceClient(manager);
        var logPath = NewLogPath();
        client.FailureLogPath = logPath;

        var recovery = new TaskCompletionSource<IpcInferenceClient.StuckRecoveryInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StuckRecoveryTriggered += info => recovery.TrySetResult(info);

        var peerTask = Task.Run(async () =>
        {
            var submit = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            var id = ReadTaskId(submit);
            await IpcFraming.WriteJsonAsync(peer, Frame("accepted", id), CancellationToken.None);
            await IpcFraming.WriteJsonAsync(
                peer,
                Error(id, "inference_failed", "AcceleratorError: CUDA error: out of memory"),
                CancellationToken.None);
        });

        var thrown = await Assert.ThrowsAsync<InferenceBackendException>(async () =>
            await client.SubmitInpaintAsync(CreateRequest(), progress: null, CancellationToken.None));
        Assert.Contains("AcceleratorError", thrown.Message);

        var info = await recovery.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("accelerator_error", info.Reason);
        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(File.Exists(logPath));
        var line = File.ReadAllLines(logPath).Single();
        Assert.Contains("\"reason\":\"accelerator_error\"", line);
        Assert.Contains("\"action\":\"restart\"", line);
    }

    [Theory]
    [InlineData("AcceleratorError: CUDA error", true)]
    [InlineData("RuntimeError: CUDA out of memory. Tried to allocate", true)]
    [InlineData("model_load_failed: FileNotFoundError", false)]
    [InlineData(null, false)]
    public void IsMechanismFailure_Matches_Expected(string? message, bool expected)
        => Assert.Equal(expected, IpcInferenceClient.IsMechanismFailure(message));

    private static string NewLogPath()
        => Path.Combine(Path.GetTempPath(), "zivai_faillog_" + Guid.NewGuid().ToString("N") + ".jsonl");

    private static string ReadTaskId((byte FrameType, byte[] Payload)? frame)
    {
        Assert.NotNull(frame);
        using var document = JsonDocument.Parse(frame!.Value.Payload);
        return document.RootElement.GetProperty("task_id").GetString()!;
    }

    private static PythonBackendOptions NewOptions(
        int modelLoadTimeoutMs,
        int stuckTimeoutMs = 60_000,
        int cancelConfirmTimeoutMs = 5_000)
        => new()
        {
            PipeName = "zivai.test." + Guid.NewGuid().ToString("N"),
            PythonExe = "python.exe",
            Script = "main.py",
            AutoRestartEnabled = false,
            ModelLoadTimeoutMs = modelLoadTimeoutMs,
            StuckTimeoutMs = stuckTimeoutMs,
            CancelConfirmTimeoutMs = cancelConfirmTimeoutMs,
            RequestTimeoutMs = 1_000,
        };

    private static InpaintRequest CreateRequest()
        => new()
        {
            ImagePath = "in.png",
            Prompt = "test",
            Steps = 1,
            Seed = 1,
            Denoise = 1.0,
            OutputPath = "out.png",
        };

    private static NamedPipeServerStream NewPipeServer(string name)
        => new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static NamedPipeClientStream NewPipeClient(string name)
        => new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);

    private static async Task ConnectAsync(NamedPipeServerStream server, NamedPipeClientStream peer)
    {
        var connecting = server.WaitForConnectionAsync();
        await peer.ConnectAsync(5000);
        await connecting;
    }

    private static string Frame(string type, string taskId)
        => $"{{\"type\":\"{type}\",\"task_id\":\"{taskId}\",\"output_path\":\"out.png\",\"duration_ms\":1,\"width\":8,\"height\":8,\"seed\":1}}";

    private static string Progress(string taskId, string stage)
        => $"{{\"type\":\"progress\",\"task_id\":\"{taskId}\",\"stage\":\"{stage}\",\"sub_stage\":\"ready\",\"step\":0,\"total\":0,\"fraction\":1.0,\"message\":\"{stage}\"}}";

    private static string Error(string taskId, string code, string message)
        => $"{{\"type\":\"error\",\"task_id\":\"{taskId}\",\"code\":\"{code}\",\"message\":\"{message}\"}}";
}
