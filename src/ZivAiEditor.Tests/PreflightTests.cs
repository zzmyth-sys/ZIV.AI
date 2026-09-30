using System.Net;
using ZivAiEditor.App;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>LLM/VRAM preflight tests (fake HTTP + fake inference client, no GPU).</summary>
public class PreflightTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpResponseMessage> responder) => _responder = responder;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_responder());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }

    private sealed class FakeInferenceClient : IInferenceClient
    {
        private readonly double _vramUsed;

        public FakeInferenceClient(double vramUsed) => _vramUsed = vramUsed;

        public int HealthCalls { get; private set; }

        public Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
        {
            HealthCalls++;
            return Task.FromResult(new HealthStatus { VramUsedMb = _vramUsed });
        }

        public Task<InferenceTaskHandle> SubmitInpaintAsync(
            InpaintRequest request,
            IProgress<InferenceProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InferenceTaskHandle> SubmitEditAsync(
            EditRequest request,
            IProgress<InferenceProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingInferenceClient : IInferenceClient
    {
        public Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("backend down");

        public Task<InferenceTaskHandle> SubmitInpaintAsync(
            InpaintRequest request,
            IProgress<InferenceProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InferenceTaskHandle> SubmitEditAsync(
            EditRequest request,
            IProgress<InferenceProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    private static LlmPreflight Create(
        IInferenceClient client,
        HttpMessageHandler handler,
        double total = 16376,
        double need = 9800)
        => new(client, "http://127.0.0.1:8080/v1/chat/completions", total, need,
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });

    [Fact]
    public async Task Unreachable_When_Probe_Fails()
    {
        var preflight = Create(new FakeInferenceClient(0), new ThrowingHandler());

        var result = await preflight.CheckAsync();

        Assert.Equal(LlmPreflightStatus.LlmUnreachable, result.Status);
        Assert.Contains("启动 LLM", result.Message);
    }

    [Fact]
    public async Task Unreachable_When_Probe_Returns_Error_Status()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var preflight = Create(new FakeInferenceClient(0), handler);

        var result = await preflight.CheckAsync();

        Assert.Equal(LlmPreflightStatus.LlmUnreachable, result.Status);
    }

    [Fact]
    public async Task LowVram_When_Free_Below_Need()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK));
        var preflight = Create(new FakeInferenceClient(14000), handler, total: 16376, need: 9800);

        var result = await preflight.CheckAsync();

        Assert.Equal(LlmPreflightStatus.LowVram, result.Status);
        Assert.Equal(2376, result.VramFreeMb, precision: 0);
    }

    [Fact]
    public async Task Ready_When_Enough_Free_Vram()
    {
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK));
        var preflight = Create(new FakeInferenceClient(4000), handler, total: 16376, need: 9800);

        var result = await preflight.CheckAsync();

        Assert.Equal(LlmPreflightStatus.Ready, result.Status);
        Assert.Equal(12376, result.VramFreeMb, precision: 0);
    }

    [Fact]
    public async Task Ready_When_Llm_Reachable_But_Vram_Read_Fails()
    {
        // The LLM probe succeeds, so the rewriter is reachable; a ComfyUI backend VRAM
        // read failure must not hard-block (D7).
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK));
        var preflight = Create(new ThrowingInferenceClient(), handler);

        var result = await preflight.CheckAsync();

        Assert.Equal(LlmPreflightStatus.Ready, result.Status);
    }

    [Fact]
    public async Task Backend_Not_Running_Skips_Vram_Probe_And_Stays_Ready()
    {
        // B17: the LLM is reachable but the ComfyUI backend is not running. The preflight must NOT
        // call CheckHealthAsync (which would start Python); VRAM degrades to "unknown" -> Ready.
        var fake = new FakeInferenceClient(0);
        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK));
        var preflight = new LlmPreflight(
            fake,
            "http://127.0.0.1:8080/v1/chat/completions",
            16376,
            9800,
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            isBackendRunning: () => false);

        var result = await preflight.CheckAsync();

        Assert.Equal(LlmPreflightStatus.Ready, result.Status);
        Assert.Equal(0, fake.HealthCalls);
    }

    [Fact]
    public async Task Real_Backend_Not_Started_By_Preflight()
    {
        // B17: end-to-end proof with a real IpcInferenceClient — a stopped backend is never started
        // by the preflight (no Python / GPU is launched).
        var manager = new PythonProcessManager(new PythonBackendOptions
        {
            PythonExe = "python.exe",
            Script = "main.py",
        });
        using var client = new IpcInferenceClient(manager, ownsProcess: true);

        var handler = new FakeHandler(() => new HttpResponseMessage(HttpStatusCode.OK));
        var preflight = new LlmPreflight(
            client,
            "http://127.0.0.1:8080/v1/chat/completions",
            16376,
            9800,
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });

        var result = await preflight.CheckAsync();

        Assert.Equal(LlmPreflightStatus.Ready, result.Status);
        Assert.Equal(PythonBackendState.Stopped, manager.State);
        Assert.False(manager.IsProcessRunning);
    }
}