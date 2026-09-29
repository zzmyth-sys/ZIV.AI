using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.Contracts.Project;
using ZivAiEditor.Contracts.Session;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Imaging;
using ZivAiEditor.Tools;

namespace ZivAiEditor.App;

/// <summary>
/// Composition root for the App layer (Z1: no static hub). Wires the Python
/// process manager and IPC client from <c>settings.ini</c>, and builds the Z22
/// planner chain (<c>LlmPlanner</c> over <c>LocalLlmClient</c>, degrading to
/// <c>FallbackPlanner</c>) exposed as the frozen <see cref="IPlanner"/> contract.
///
/// <para>R-4 (module-boundaries closure): <see cref="Create"/> is grouped by domain
/// (<c>BuildBackend</c> / <c>BuildTools</c> / <c>BuildAgent</c> / <c>BuildLlm</c> /
/// <c>BuildPersistence</c> / <c>BuildImaging</c>). The <see cref="AppContext"/> shape is
/// unchanged; each group stays small.</para>
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
        ICommandTemplateService commandTemplates,
        IEditSession session,
        IEditSessionWriter sessionWriter,
        ISessionPersistence sessionStore,
        IProjectService projects,
        IImagingService imaging,
        LocalLlmClient rewriterLlm,
        IPromptExpander promptExpander,
        ILlmPreflight llmPreflight,
        PluginRegistry pluginRegistry)
    {
        _backend = backend;
        Client = client;

        // App-layer wiring (ARCHITECTURE §6): surface backend preview frames
        // (0x02, JPEG) as plain bytes so the UI never references Backend types (§4).
        client.PreviewReceived += frame => PreviewReceived?.Invoke(frame.JpegBytes);
        // L1/L2 recovery (Step 9C.20): surface the backend restart to the App shell.
        client.StuckRecoveryTriggered += _ => StuckRecovery?.Invoke();
        _llmHttp = llmHttp;
        LlmClient = llmClient;
        Planner = planner;
        Tools = tools;
        Executor = executor;
        _executionQueue = executionQueue;
        ModelProfiles = modelProfiles;
        _commandParser = commandParser;
        CommandTemplates = commandTemplates;
        Session = session;
        SessionWriter = sessionWriter;
        SessionStore = sessionStore;
        Projects = projects;
        Imaging = imaging;
        _rewriterLlm = rewriterLlm;
        PromptExpander = promptExpander;
        LlmPreflight = llmPreflight;
        PluginRegistry = pluginRegistry;
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

    /// <summary>
    /// Plugin registry loaded from <c>Template/plugins.json</c> (batch 1). Feeds the backend env
    /// injection and the settings-window plugin tab; empty when the file is missing.
    /// </summary>
    public PluginRegistry PluginRegistry { get; }

    /// <summary>Deterministic slash-command / prompt parser (Step 8).</summary>
    public ICommandParser CommandParser => _commandParser;

    /// <summary>
    /// The built-in + user merged command template store (T5/S1). The parser is built from
    /// <see cref="ICommandTemplateService.List"/> at startup; edits through this service take
    /// effect on the next restart (hot reload is Z-030).
    /// </summary>
    public ICommandTemplateService CommandTemplates { get; }

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
    public ISessionPersistence SessionStore { get; }

    /// <summary>
    /// Project catalog (module-boundary migration step 3): list / delete / rename / locate
    /// projects and the last-opened id. Owns the <c>sessions/</c> directory tree; the session
    /// content save / load stays on <see cref="ISessionPersistence"/>.
    /// </summary>
    public IProjectService Projects { get; }

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

    /// <summary>Raised when the backend performed an L1/L2 recovery (Step 9C.20); the shell updates the bubble.</summary>
    public event Action? StuckRecovery;

    public static AppContext Create(IShellContext shell)
    {
        var settings = shell.LoadSettings();

        // Batch 1: the plugin registry is data next to commands.json; it feeds both the backend
        // env injection (below) and the settings-window plugin tab.
        var pluginRegistry = new PluginRegistry(Path.Combine(shell.TemplateDirectory, "plugins.json"));

        // R-4: grouped by domain (backend → tools → agent → llm → persistence → imaging).
        var (backend, client) = BuildBackend(settings, shell.TemplateDirectory, pluginRegistry);
        var (tools, executionQueue) = BuildTools(client);
        var (commandParser, commandTemplates, session, executor, modelProfiles) =
            BuildAgent(shell.TemplateDirectory, tools, executionQueue);
        var llm = BuildLlm(settings, client, tools);
        var (sessionStore, projects) = BuildPersistence();
        var imaging = BuildImaging();

        var context = new AppContext(
            backend, client, llm.Http, llm.PlannerLlm, llm.Planner, tools, executor, executionQueue,
            modelProfiles, commandParser, commandTemplates, session, session, sessionStore, projects, imaging,
            llm.RewriterLlm, llm.PromptExpander, llm.LlmPreflight, pluginRegistry);

        // Optimization §10.2.1: start the Python backend now (background) so its ~4 s
        // `import comfy` + DynamicVRAM init run while the user is still setting up,
        // instead of stalling the first generate.
        if (settings.Prewarm)
        {
            context.StartBackendPrewarm();
        }

        return context;
    }

    /// <summary>
    /// Best-effort background backend warm-up (§10.2.1): start the Python process now so its
    /// prewarm thread (<c>import comfy</c> / DynamicVRAM init) runs while the user is still
    /// setting up. Only the process is started — no health round-trip, because the ~4 s import
    /// holds the GIL and would make <c>CheckHealthAsync</c> (10 s budget) time out spuriously.
    /// Never throws; the first submit starts the backend normally if this fails.
    /// </summary>
    internal void StartBackendPrewarm()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _backend.EnsureStartedAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored — lazy start on first use remains the fallback
            }
        });
    }

    /// <summary>Backend domain: the Python process manager and the IPC client over it.</summary>
    private static (PythonProcessManager Backend, IpcInferenceClient Client) BuildBackend(
        BackendSettings settings,
        string templateDirectory,
        PluginRegistry pluginRegistry)
    {
        var options = new PythonBackendOptions
        {
            PipeName = settings.PipeName,
            PythonExe = settings.PythonExe,
            Script = settings.Script,
            AutoRestartEnabled = true,
            Environment = BuildBackendEnvironment(settings, templateDirectory, pluginRegistry),
            // Step 9C.6-D diagnostic: persist the backend log under the program directory
            // (Z14) so the submit resolution / errors survive the in-memory capture.
            LogFilePath = Path.Combine(System.AppContext.BaseDirectory, "_cache", "backend.log"),
        };

        var backend = new PythonProcessManager(options);
        var client = new IpcInferenceClient(backend, ownsProcess: true);
        return (backend, client);
    }

    /// <summary>
    /// Assembles the backend process environment. Only a non-empty model path produces a key
    /// (B3), so Python's own defaults stay in effect when a path is unset / cleared. The
    /// registry paths (T1) are injected only when the file exists, which keeps C# and Python
    /// reading the <b>same</b> <c>models.json</c> / <c>loras.json</c>; a missing file is logged
    /// (Debug) instead of silently letting Python fall back. Pure and unit-testable.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildBackendEnvironment(
        BackendSettings settings,
        string templateDirectory)
        => BuildBackendEnvironment(settings, templateDirectory, plugins: null);

    /// <summary>
    /// Same as the 2-arg overload plus plugin env injection (batch 1): the
    /// <c>ZIV_AI_PLUGINS_REGISTRY</c> path (only when <c>plugins.json</c> exists, mirroring
    /// models / loras) and one <c>ZIV_AI_PLUGIN_&lt;ID&gt;</c> flag per known plugin. When
    /// <paramref name="plugins"/> is null / empty, no plugin key is added — the environment
    /// stays minimal.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildBackendEnvironment(
        BackendSettings settings,
        string templateDirectory,
        PluginRegistry? plugins)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddIfSet(environment, "ZIV_AI_DIT_PATH", settings.DitPath);
        AddIfSet(environment, "ZIV_AI_TE_PATH", settings.TePath);
        AddIfSet(environment, "ZIV_AI_VAE_PATH", settings.VaePath);
        // LoRA root: inject whenever non-empty (AddIfSet, NOT AddDirectoryIfPresent) so a
        // configured-but-not-yet-present directory still reaches Python and the LoRA resolve
        // reports the real configuration error at use-time.
        AddIfSet(environment, "ZIV_AI_LORA_ROOT", settings.LoraRoot);
        AddRegistryIfPresent(
            environment,
            "ZIV_AI_MODELS_REGISTRY",
            Path.Combine(templateDirectory, "models.json"));
        AddRegistryIfPresent(
            environment,
            "ZIV_AI_LORA_REGISTRY",
            Path.Combine(templateDirectory, "loras.json"));
        AddRegistryIfPresent(
            environment,
            "ZIV_AI_PLUGINS_REGISTRY",
            Path.Combine(templateDirectory, "plugins.json"));

        // Relative plugin `dir` resolves against the program directory. Inject the same value C#
        // PluginRegistry.ResolveDirectory uses (System.AppContext.BaseDirectory) so both sides
        // resolve an identical directory; only when the registry ships, keeping the env minimal.
        if (File.Exists(Path.Combine(templateDirectory, "plugins.json")))
        {
            environment["ZIV_AI_PLUGINS_BASE_DIR"] = System.AppContext.BaseDirectory;
        }

        AddDirectoryIfPresent(environment, "ZIV_AI_COMFY_ROOT", settings.ComfyRoot);

        // Plugin hot-switch (plan 1): hand Python the settings.ini path so it can read
        // [plugins] live; otherwise only the startup-frozen per-plugin env reaches Python
        // and a UI toggle needs an App restart. Injected only when the file exists.
        var settingsIni = Path.Combine(System.AppContext.BaseDirectory, "settings.ini");
        if (File.Exists(settingsIni))
        {
            environment["ZIV_AI_SETTINGS_PATH"] = settingsIni;
        }

        // settings.ini [plugins] wins; otherwise the registry's enabled_by_default.
        if (plugins is { } registry)
        {
            foreach (var plugin in registry.All)
            {
                var enabled = settings.PluginStates.TryGetValue(plugin.Id, out var state)
                    ? state
                    : plugin.EnabledByDefault;
                environment[PluginEnvName(plugin.Id)] = enabled ? "1" : "0";
            }
        }

        return environment;
    }

    /// <summary>
    /// Env var name for a plugin id (batch 1); delegates to <see cref="PluginRegistry.EnvName"/>
    /// so C# and Python share exactly one normalization rule.
    /// </summary>
    internal static string PluginEnvName(string pluginId) => PluginRegistry.EnvName(pluginId);

    private static void AddIfSet(Dictionary<string, string> environment, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            environment[key] = value;
        }
    }

    private static void AddRegistryIfPresent(
        Dictionary<string, string> environment,
        string key,
        string path)
    {
        if (File.Exists(path))
        {
            environment[key] = path;
        }
        else
        {
            Debug.WriteLine($"[backend] registry not found, env not injected: {path}");
        }
    }

    private static void AddDirectoryIfPresent(
        Dictionary<string, string> environment,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Unset -> let Python fall back to its default / python_exe-derived ComfyUI root.
            return;
        }

        if (Directory.Exists(value))
        {
            environment[key] = value;
        }
        else
        {
            Debug.WriteLine($"[backend] directory not found, env not injected: {key}={value}");
        }
    }

    /// <summary>Tools domain: the registry plus the single serial execution queue (Z18).</summary>
    private static (IToolRegistry Tools, ExecutionQueue Queue) BuildTools(IInferenceClient client)
    {
        // Step 6: real tool registry + single serial execution queue (Z18).
        // Step 7: add the outpainting tool; Segment / Upscale stay unregistered
        // (they need independent models — Step 7.5 candidates).
        var tools = new ToolRegistry();
        tools.Register(new QwenImage21EditTool(client));
        tools.Register(new QwenImage21OutpaintTool(client));
        return (tools, new ExecutionQueue());
    }

    /// <summary>Agent domain: the parser, the in-memory session, the executor and the model profiles.</summary>
    private static (CommandParser Parser, ICommandTemplateService Templates, EditSession Session, IExecutor Executor, IModelProfileRegistry Profiles)
        BuildAgent(
            string templateDirectory,
            IToolRegistry tools,
            ExecutionQueue executionQueue)
    {
        // T5/S1: the built-in + user merged command view feeds the parser. The user override
        // file (commands.user.json) is read here, never written; absent = built-in only, so
        // behavior matches the pre-S1 single-file parser.
        var commandTemplates = new CommandTemplateService(templateDirectory);
        var commandParser = new CommandParser(
            commandTemplates.List().Select(dto => dto.Definition).ToList());

        // Step 9C.8-A: the executor rebuilds a re-run plan from the DAG, so it needs the
        // session (read + navigate) and the deterministic command parser.
        var session = new EditSession();
        var executor = new Executor(tools, executionQueue, session, session, commandParser);

        // Step 8-2: profiles come from the data file next to commands.json; a missing file
        // falls back to the built-in Qwen-Image-2.1 profile inside the registry.
        var modelProfiles = new ModelProfileRegistry(Path.Combine(templateDirectory, "models.json"));
        return (commandParser, commandTemplates, session, executor, modelProfiles);
    }

    /// <summary>LLM domain: the planner chain, the prompt rewriter and the /生成 preflight.</summary>
    private static LlmParts BuildLlm(BackendSettings settings, IInferenceClient client, IToolRegistry tools)
    {
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

        return new LlmParts(llmHttp, plannerLlm, planner, rewriterLlm, promptExpander, llmPreflight);
    }

    /// <summary>Project / session domain: the session store and the project catalog.</summary>
    private static (ISessionPersistence Store, IProjectService Projects) BuildPersistence()
    {
        // Step 8: one in-memory session; Step 9C.6-E: a project store. The UI drives the
        // session (built in BuildAgent, before the executor).
        var sessionStore = new SessionStore();
        return (sessionStore, new ProjectService(sessionStore));
    }

    /// <summary>Imaging domain: the local crop / mask facade; clears temp dirs orphaned by a prior run.</summary>
    private static IImagingService BuildImaging()
    {
        // Module-boundary migration step 4: construct the imaging facade and clear any crop /
        // mask temp dirs orphaned by a previous run (moved here from Program, Q4).
        var imaging = new ImagingService();
        imaging.CleanupAll();
        return imaging;
    }

    /// <summary>The LLM-domain components returned as one bundle from <see cref="BuildLlm"/>.</summary>
    private sealed record LlmParts(
        HttpClient Http,
        LocalLlmClient PlannerLlm,
        IPlanner Planner,
        LocalLlmClient RewriterLlm,
        IPromptExpander PromptExpander,
        ILlmPreflight LlmPreflight);

    /// <summary>
    /// Builds an <see cref="Executor"/> over an alternate session (bridge §7.3, P1-3): the
    /// in-editor quick path shares the process-wide tools / serial queue / parser but works on a
    /// <b>temporary</b> session, never the editor's own DAG (R1).
    /// </summary>
    public IExecutor CreateExecutor(IEditSession session, IEditSessionWriter writer)
        => new Executor(Tools, _executionQueue, session, writer, _commandParser);

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
