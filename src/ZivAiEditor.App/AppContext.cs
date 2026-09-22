using System;
using System.Diagnostics;
using ZivAiEditor.Agent;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Tools;

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
        IPlanner planner,
        IToolRegistry tools,
        IExecutor executor,
        ExecutionQueue executionQueue,
        IModelProfileRegistry modelProfiles)
    {
        Backend = backend;
        Client = client;
        _llmHttp = llmHttp;
        LlmClient = llmClient;
        Planner = planner;
        Tools = tools;
        Executor = executor;
        _executionQueue = executionQueue;
        ModelProfiles = modelProfiles;
    }

    private readonly HttpClient _llmHttp;
    private readonly ExecutionQueue _executionQueue;

    public PythonProcessManager Backend { get; }

    public IInferenceClient Client { get; }

    /// <summary>The planner-scoped LLM client (exposed for later named registration).</summary>
    public ILlmClient LlmClient { get; }

    public IPlanner Planner { get; }

    /// <summary>Registered edit tools (Step 6: the real <c>ToolRegistry</c>).</summary>
    public IToolRegistry Tools { get; }

    /// <summary>Multi-step executor over the shared serial queue (Z18).</summary>
    public IExecutor Executor { get; }

    /// <summary>Model resolution profiles (Step 6.5); the UI maps a tier via <see cref="ResolutionTier"/>.</summary>
    public IModelProfileRegistry ModelProfiles { get; }

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

        // Step 6: real tool registry + single serial execution queue (Z18).
        // Step 7: add the outpainting tool; Segment / Upscale stay unregistered
        // (they need independent models — Step 7.5 candidates).
        var tools = new ToolRegistry();
        tools.Register(new QwenImage21EditTool(client));
        tools.Register(new QwenImage21OutpaintTool(client));

        var executionQueue = new ExecutionQueue();
        var executor = new Executor(tools, executionQueue);
        var modelProfiles = new ModelProfileRegistry();

        // LocalLlmClient applies its own per-call timeout, so the shared
        // HttpClient stays timeout-free (single source of truth).
        var llmHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // Later extension point: prompt-rewriting / multi-image scenarios can
        // build additional LocalLlmClient instances here with their own
        // LlmClientOptions (e.g. higher temperature). Do not implement those
        // scenarios in Step 6 (ARCHITECTURE.md §11).
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
            new LlmPlanner(plannerLlm, tools),
            new FallbackPlanner(),
            ex => Debug.WriteLine($"[planner] degraded to fallback: {ex.Message}"));

        return new AppContext(backend, client, llmHttp, plannerLlm, planner, tools, executor, executionQueue, modelProfiles);
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
        _executionQueue.Dispose();
    }
}