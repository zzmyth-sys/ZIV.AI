using System;
using System.Diagnostics;
using System.IO;
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
        IModelProfileRegistry modelProfiles,
        ICommandParser commandParser,
        EditSession session,
        SessionStore sessionStore)
    {
        Backend = backend;
        Client = client;

        // App-layer wiring (ARCHITECTURE §6): surface backend preview frames
        // (0x02, JPEG) as plain bytes so the UI never references Backend types (§4).
        client.PreviewReceived += frame => PreviewReceived?.Invoke(frame.JpegBytes);
        _llmHttp = llmHttp;
        LlmClient = llmClient;
        Planner = planner;
        Tools = tools;
        Executor = executor;
        _executionQueue = executionQueue;
        ModelProfiles = modelProfiles;
        CommandParser = commandParser;
        Session = session;
        SessionStore = sessionStore;
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

    /// <summary>Deterministic slash-command / prompt parser (Step 8).</summary>
    public ICommandParser CommandParser { get; }

    /// <summary>The single in-memory edit session (Step 8); persisted as a project on save.</summary>
    public EditSession Session { get; }

    /// <summary>Persists sessions as projects (Step 9C.6-E); the UI drives save / open.</summary>
    public SessionStore SessionStore { get; }

    /// <summary>
    /// Raised for every backend preview frame (<c>0x02</c>, JPEG bytes) so the App
    /// can push it into the UI's pending bubble. The UI receives only
    /// <see cref="byte"/>[] — no Backend type crosses the boundary (§4).
    /// </summary>
    public event Action<byte[]>? PreviewReceived;

    public static AppContext Create()
    {
        var settings = SettingsLoader.Load();
        var options = new PythonBackendOptions
        {
            PipeName = settings.PipeName,
            PythonExe = settings.PythonExe,
            Script = settings.Script,
            AutoRestartEnabled = true,
            // Step 9C.6-D diagnostic: persist the backend log under the program directory
            // (Z14) so the submit resolution / errors survive the in-memory capture.
            LogFilePath = Path.Combine(System.AppContext.BaseDirectory, "_cache", "backend.log"),
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

        // Step 9C.8-A: the executor rebuilds a re-run plan from the DAG, so it needs the
        // session (read + navigate) and the deterministic command parser.
        var commandParser = new CommandParser(ResolveCommandsPath());
        var session = new EditSession();
        var executor = new Executor(tools, executionQueue, session, session, commandParser);

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

        // Step 8: one in-memory session; Step 9C.6-E: a project store. The UI drives the
        // session (already built above, before the executor).
        var sessionStore = new SessionStore();

        return new AppContext(
            backend, client, llmHttp, plannerLlm, planner, tools, executor, executionQueue,
            modelProfiles, commandParser, session, sessionStore);
    }

    /// <summary>
    /// Resolves <c>Template/commands.json</c> from the program directory, falling
    /// back to walking up to the repository root (mirrors <see cref="SettingsLoader"/>).
    /// If nothing is found the <see cref="CommandParser"/> uses its built-in set (Z28).
    /// </summary>
    private static string ResolveCommandsPath()
    {
        var programDirectory = System.AppContext.BaseDirectory;
        var candidate = Path.Combine(programDirectory, "Template", "commands.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var directory = new DirectoryInfo(programDirectory);
        while (directory is not null)
        {
            var repository = Path.Combine(directory.FullName, "Template", "commands.json");
            if (File.Exists(repository) && File.Exists(Path.Combine(directory.FullName, "DOC", "FROZEN.md")))
            {
                return repository;
            }

            directory = directory.Parent;
        }

        return candidate;
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