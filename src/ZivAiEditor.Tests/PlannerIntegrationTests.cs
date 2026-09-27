using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Tools;
using Xunit;
using Xunit.Abstractions;

namespace ZivAiEditor.Tests;

/// <summary>
/// End-to-end planner check against a live local llama-server (default
/// http://127.0.0.1:8080). The test self-skips when the server is unreachable
/// so the default suite stays fast and offline; with the server up it exercises
/// the Z22 chain for real and reports whether the LLM path or the deterministic
/// fallback produced the plan.
/// </summary>
public class PlannerIntegrationTests
{
    private const string HealthUrl = "http://127.0.0.1:8080/health";

    private readonly ITestOutputHelper _output;

    public PlannerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ResilientPlanner_Plans_Against_Local_Llm()
    {
        if (!await ServerReachableAsync())
        {
            _output.WriteLine($"[skip] llama-server not reachable at {HealthUrl}; live planner check not run.");
            return;
        }

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var llm = new LocalLlmClient(
            http,
            new LlmClientOptions { Timeout = TimeSpan.FromSeconds(60) });

        var request = new PlanRequest
        {
            MainImagePath = Path.Combine(FindRepositoryRoot(), "_test_step2", "user_input_1024.png"),
            Prompt = "把背景替换为古代中式茶肆，保留人物与前景",
        };

        await PrintRawLlmOutputAsync(llm, request);

        var degraded = false;
        var planner = new ResilientPlanner(
            new LlmPlanner(llm, new EmptyToolRegistry()),
            new FallbackPlanner(),
            ex =>
            {
                degraded = true;
                _output.WriteLine($"[degraded] {ex.GetType().Name}: {ex.Message}");
            });

        var plan = await planner.PlanAsync(request);

        _output.WriteLine($"path={(degraded ? "fallback" : "llm")}");
        _output.WriteLine($"Steps.Count = {plan.Steps.Count}");
        foreach (var step in plan.Steps)
        {
            _output.WriteLine(
                $"  #{step.Order} {step.ToolName} " +
                $"params=[{string.Join(", ", step.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))}] " +
                $"depends_on=[{string.Join(", ", step.DependsOn)}]");
        }

        Assert.NotEmpty(plan.Steps);
        Assert.All(plan.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.ToolName)));
    }

    [Fact]
    public async Task LlmPlanner_Handles_Complex_Prompt()
    {
        if (!await ServerReachableAsync())
        {
            _output.WriteLine($"[skip] llama-server not reachable at {HealthUrl}; live planner check not run.");
            return;
        }

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var llm = new LocalLlmClient(
            http,
            new LlmClientOptions { Timeout = TimeSpan.FromSeconds(60) });
        var planner = new LlmPlanner(llm, new EmptyToolRegistry());

        var request = new PlanRequest
        {
            MainImagePath = Path.Combine(FindRepositoryRoot(), "_test_step2", "user_input_1024.png"),
            Prompt = "先把画面整体转成水墨画风格，再把背景替换成古代中式茶肆，保留前景人物不变",
        };

        EditPlan plan;
        try
        {
            plan = await planner.PlanAsync(request);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[llm-failed] {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        _output.WriteLine($"Steps.Count = {plan.Steps.Count}");
        foreach (var step in plan.Steps)
        {
            _output.WriteLine(
                $"  #{step.Order} {step.ToolName} " +
                $"params=[{string.Join(", ", step.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))}] " +
                $"depends_on=[{string.Join(", ", step.DependsOn)}]");
        }

        Assert.NotEmpty(plan.Steps);
    }

    private async Task PrintRawLlmOutputAsync(LocalLlmClient llm, PlanRequest request)
    {
        try
        {
            var system = "You are a test. Reply with a single word.";
            var raw = await llm.CompleteAsync(system, "Say: pong");
            _output.WriteLine($"[raw] {raw}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[raw-error] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<bool> ServerReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync(HealthUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Walks up from the test output directory to the repository root (marked by
    /// <c>DOC/FROZEN.md</c>), so test inputs are addressed relatively (no dev-machine path).
    /// </summary>
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

    private sealed class EmptyToolRegistry : IToolRegistry
    {
        public void Register(IEditTool tool)
        {
        }

        public bool Unregister(string toolName) => false;

        public IEditTool? Get(string toolName) => null;

        public IReadOnlyList<IEditTool> All => Array.Empty<IEditTool>();
    }
}