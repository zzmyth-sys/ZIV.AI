using System;
using System.Diagnostics;
using ZivAiEditor.Agent;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.App;

/// <summary>
/// Composition root for the App layer (Z1: no static hub). Wires the Python
/// process manager and IPC client from <c>settings.ini</c>, and builds the Z22
/// planner chain (<c>LlmPlanner</c> over <c>LocalLlmClient</c>, degrading to
/// <c>FallbackPlanner</c>) exposed as the frozen <see cref="IPlanner"/> contract.
/// </summary>
internal sealed class AppContext : IDisposable
{
    private bool _disposed;

    private AppContext(
        PythonProcessManager backend,
        IpcInferenceClient client,
        HttpClient llmHttp,
        LocalLlmClient llmClient,
        IPlanner planner)
    {
        Backend = backend;
        Client = client;
        _llmHttp = llmHttp;
        LlmClient = llmClient;
        Planner = planner;
    }

    private readonly HttpClient _llmHttp;

    public PythonProcessManager Backend { get; }

    public IInferenceClient Client { get; }

    /// <summary>The planner-scoped LLM client (exposed for later named registration).</summary>
    public ILlmClient LlmClient { get; }

    public IPlanner Planner { get; }

    public static AppContext Create()
    {
        var settings = SettingsLoader.Load();
        var options = new PythonBackendOptions
        {
            PipeName = settings.PipeName,
            PythonExe = settings.PythonExe,
            Script = settings.Script,
            AutoRestartEnabled = true,
        };

        var backend = new PythonProcessManager(options);
        var client = new IpcInferenceClient(backend, ownsProcess: true);

        // LocalLlmClient applies its own per-call timeout, so the shared
        // HttpClient stays timeout-free (single source of truth).
        var llmHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // Later extension point: prompt-rewriting / multi-image scenarios can
        // build additional LocalLlmClient instances here with their own
        // LlmClientOptions (e.g. higher temperature). Do not implement those
        // scenarios in Step 5 (ARCHITECTURE.md §11).
        var plannerOptions = new LlmClientOptions
        {
            Endpoint = settings.LlmPlanner.Endpoint,
            Model = string.IsNullOrWhiteSpace(settings.LlmPlanner.Model) ? null : settings.LlmPlanner.Model,
            Timeout = TimeSpan.FromSeconds(settings.LlmPlanner.TimeoutSeconds),
            Temperature = settings.LlmPlanner.Temperature,
            MaxTokens = settings.LlmPlanner.MaxTokens,
            EnableThinking = settings.LlmPlanner.EnableThinking,
        };

        var plannerLlm = new LocalLlmClient(llmHttp, plannerOptions, ownsHttpClient: false);
        var planner = new ResilientPlanner(
            new LlmPlanner(plannerLlm, new EmptyToolRegistry()),
            new FallbackPlanner(),
            ex => Debug.WriteLine($"[planner] degraded to fallback: {ex.Message}"));

        return new AppContext(backend, client, llmHttp, plannerLlm, planner);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LlmClient.Dispose();
        _llmHttp.Dispose();
        Client.Dispose();
        Backend.Dispose();
    }
}