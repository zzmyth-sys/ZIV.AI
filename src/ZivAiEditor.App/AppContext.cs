using System;
using System.Diagnostics;
using System.IO;
using ZivAiEditor.Agent;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Imaging;
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
        CommandParser commandParser,
        IEditSession session,
        IEditSessionWriter sessionWriter,
        SessionStore sessionStore,
        ProjectService projects,
        IImagingService imaging,
        LocalLlmClient rewriterLlm,
        IPromptExpander promptExpander,
        ILlmPreflight llmPreflight)
    {
        _backend = backend;
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
        _commandParser = commandParser;
        Session = session;
        SessionWriter = sessionWriter;
        SessionStore = sessionStore;
        Projects = projects;
        Imaging = imaging;
        _rewriterLlm = rewriterLlm;
        PromptExpander = promptExpander;
        LlmPreflight = llmPreflight;
    }

    private readonly HttpClient _llmHttp;
    private readonly ExecutionQueue _executionQueue;
    private readonly CommandParser _commandParser;
    private readonly LocalLlmClient _rewriterLlm;
    private readonly PythonProcessManager _backend;

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
    public ICommandParser CommandParser => _commandParser;

    /// <summary>The loaded command set (single / multi template variants), for the /生成 flow.</summary>
    public IReadOnlyList<CommandDefinition> Commands => _commandParser.Commands;

    /// <summary>Prompt rewriter for <c>/生成</c> (prompt-rewriter flow).</summary>
    public IPromptExpander PromptExpander { get; }

    /// <summary>LLM reachability / VRAM preflight for <c>/生成</c> (prompt-rewriter flow).</summary>
    public ILlmPreflight LlmPreflight { get; }

    /// <summary>The single in-memory edit session (Step 8); persisted as a project on save.</summary>
    public IEditSession Session { get; }

    /// <summary>
    /// The write surface of <see cref="Session"/> (module-boundary migration step 2): the same
    /// instance, consumed by the App for project load / new-session resets. Kept separate from
    /// the read-only <see cref="Session"/> so read consumers cannot mutate the session.
    /// </summary>
    public IEditSessionWriter SessionWriter { get; }

    /// <summary>Persists sessions as projects (Step 9C.6-E); the UI drives save / open.</summary>
    public SessionStore SessionStore { get; }

    /// <summary>
    /// Project catalog (module-boundary migration step 3): list / delete / rename / locate
    /// projects and the last-opened id. Owns the <c>sessions/</c> directory tree; the session
    /// content save / load stays on <see cref="SessionStore"/>.
    /// </summary>
    public ProjectService Projects { get; }

    /// <summary>
    /// Imaging-domain facade (module-boundary migration step 4): local crop / mask raster ops.
    /// The UI / controls depend on the <see cref="IImagingService"/> port, not the implementation.
    /// </summary>
    public IImagingService Imaging { get; }

    /// <summary>
    /// Raised for every backend preview frame (<c>0x02</c>, JPEG bytes) so the App
    /// can push it into the UI's pending bubble. The UI receives only
    /// <see cref="byte"/>[] — no Backend type crosses the boundary (§4).
    /// </summary>
    public event Action<byte[]>? PreviewReceived;

    public static AppContext Create(ShellService shell)
    {
        var settings = shell.LoadSettings();
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
        var commandParser = new CommandParser(Path.Combine(shell.TemplateDirectory, "commands.json"));
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

        // Prompt-rewriter flow: a second client over the shared HttpClient, its own options
        // (higher temperature), the /生成 expander and the LLM/VRAM preflight.
        var rewriterOptions = new LlmClientOptions
        {
            Endpoint = settings.LlmRewriter.Endpoint,
            Model = string.IsNullOrWhiteSpace(settings.LlmRewriter.Model) ? null : settings.LlmRewriter.Model,
            Timeout = TimeSpan.FromSeconds(settings.LlmRewriter.TimeoutSeconds),
            Temperature = settings.LlmRewriter.Temperature,
            MaxTokens = settings.LlmRewriter.MaxTokens,
            EnableThinking = settings.LlmRewriter.EnableThinking,
        };

        var rewriterLlm = new LocalLlmClient(llmHttp, rewriterOptions, ownsHttpClient: false);
        var promptExpander = new PromptExpander(rewriterLlm);
        var llmPreflight = new LlmPreflight(
            client,
            settings.LlmRewriter.Endpoint,
            settings.LlmRewriter.VramTotalMb,
            settings.LlmRewriter.VramNeedMb);

        // Step 8: one in-memory session; Step 9C.6-E: a project store. The UI drives the
        // session (already built above, before the executor).
        var sessionStore = new SessionStore();
        var projects = new ProjectService(sessionStore);

        // Module-boundary migration step 4: construct the imaging facade and clear any crop /
        // mask temp dirs orphaned by a previous run (moved here from Program, Q4).
        var imaging = new ImagingService();
        imaging.CleanupAll();

        return new AppContext(
            backend, client, llmHttp, plannerLlm, planner, tools, executor, executionQueue,
            modelProfiles, commandParser, session, session, sessionStore, projects, imaging,
            rewriterLlm, promptExpander, llmPreflight);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LlmClient.Dispose();
        _rewriterLlm.Dispose();
        _llmHttp.Dispose();
        Client.Dispose();
        _backend.Dispose();
        _executionQueue.Dispose();
    }
}