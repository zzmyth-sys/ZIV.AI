using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Tools;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 6 executor orchestration tests (no GPU, no LLM): multi-step ordering,
/// intermediate chaining (Z24), failure retention, cancellation and dependency
/// handling, all driven by mock <see cref="IEditTool"/>s.
/// </summary>
public class ExecutorTests
{
    private const string Main = @"C:\img\main.png";

    [Fact]
    public async Task Single_Step_Plan_Executes_Successfully()
    {
        var tool = new FakeTool("QW21edit", (input, _) => Task.FromResult(
            Ok(input.StepId, @"C:\out\one.png")));
        var executor = CreateExecutor(tool);

        var state = await executor.ExecuteAsync(Plan(Step("s1", 1, "QW21edit")));

        Assert.Equal(TaskStatus.Succeeded, state.Status);
        Assert.Equal(@"C:\out\one.png", state.OutputImagePath);
        var stepState = Assert.Single(state.StepStates);
        Assert.Equal(StepStatus.Succeeded, stepState.Status);
        Assert.Equal(@"C:\out\one.png", stepState.OutputImagePath);
        Assert.Equal(Main, Assert.Single(tool.Received).MainImagePath);
    }

    [Fact]
    public async Task MultiStep_Plan_Executes_In_Order()
    {
        var calls = new List<string>();
        var toolA = new FakeTool("a", (input, _) =>
        {
            calls.Add($"a:{input.MainImagePath}");
            return Task.FromResult(Ok(input.StepId, @"C:\out\a.png"));
        });
        var toolB = new FakeTool("b", (input, _) =>
        {
            calls.Add($"b:{input.MainImagePath}");
            return Task.FromResult(Ok(input.StepId, @"C:\out\b.png"));
        });
        var executor = CreateExecutor(toolA, toolB);

        var state = await executor.ExecuteAsync(Plan(
            Step("s1", 1, "a"),
            Step("s2", 2, "b", dependsOn: new[] { "s1" })));

        Assert.Equal(TaskStatus.Succeeded, state.Status);
        Assert.Equal(new[] { $"a:{Main}", @"b:C:\out\a.png" }, calls);
        Assert.Equal(@"C:\out\b.png", state.OutputImagePath);
        Assert.All(state.StepStates, step => Assert.Equal(StepStatus.Succeeded, step.Status));
    }

    [Fact]
    public async Task MultiStep_Plan_With_Three_Steps_Chains_Outputs()
    {
        var calls = new List<string>();
        FakeTool Tool(string name, string output) => new(name, (input, _) =>
        {
            calls.Add($"{name}:{input.MainImagePath}");
            return Task.FromResult(Ok(input.StepId, output));
        });
        var executor = CreateExecutor(
            Tool("a", @"C:\out\a.png"),
            Tool("b", @"C:\out\b.png"),
            Tool("c", @"C:\out\c.png"));

        var state = await executor.ExecuteAsync(Plan(
            Step("s1", 1, "a"),
            Step("s2", 2, "b", dependsOn: new[] { "s1" }),
            Step("s3", 3, "c", dependsOn: new[] { "s2" })));

        Assert.Equal(TaskStatus.Succeeded, state.Status);
        Assert.Equal(
            new[] { $"a:{Main}", @"b:C:\out\a.png", @"c:C:\out\b.png" },
            calls);
        Assert.Equal(@"C:\out\c.png", state.OutputImagePath);
    }

    [Fact]
    public async Task Step_Failure_Marks_Task_Failed_And_Keeps_Intermediate()
    {
        var toolA = new FakeTool("a", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\a.png")));
        var toolB = new FakeTool("b", (_, _) => throw new InvalidOperationException("boom"));
        var toolC = new FakeTool("c", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\c.png")));
        var executor = CreateExecutor(toolA, toolB, toolC);

        var state = await executor.ExecuteAsync(Plan(
            Step("s1", 1, "a"),
            Step("s2", 2, "b", dependsOn: new[] { "s1" }),
            Step("s3", 3, "c", dependsOn: new[] { "s2" })));

        Assert.Equal(TaskStatus.Failed, state.Status);
        Assert.Equal(StepStatus.Succeeded, state.StepStates[0].Status);
        Assert.Equal(@"C:\out\a.png", state.StepStates[0].OutputImagePath);
        Assert.Equal(StepStatus.Failed, state.StepStates[1].Status);
        Assert.Equal("boom", state.StepStates[1].ErrorMessage);
        Assert.Equal(StepStatus.Skipped, state.StepStates[2].Status);
        Assert.Empty(toolC.Received);
    }

    [Fact]
    public async Task Cancel_Stops_Execution()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeTool("slow", async (input, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok(input.StepId, @"C:\out\slow.png");
        });
        var after = new FakeTool("after", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\after.png")));
        var executor = CreateExecutor(slow, after);

        string? taskId = null;
        var progress = new InlineProgress<TaskProgress>(report => taskId ??= report.TaskId);

        var execution = executor.ExecuteAsync(
            Plan(
                Step("s1", 1, "slow"),
                Step("s2", 2, "after", dependsOn: new[] { "s1" })),
            progress,
            CancellationToken.None);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await executor.CancelAsync(taskId!));

        var state = await execution;

        Assert.Equal(TaskStatus.Canceled, state.Status);
        Assert.Equal(StepStatus.Canceled, state.StepStates[0].Status);
        Assert.Equal(StepStatus.Canceled, state.StepStates[1].Status);
        Assert.Empty(after.Received);
    }

    [Fact]
    public async Task External_Token_Cancels_Task()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new FakeTool("slow", async (input, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok(input.StepId, @"C:\out\slow.png");
        });
        var executor = CreateExecutor(slow);

        using var cts = new CancellationTokenSource();
        var execution = executor.ExecuteAsync(Plan(Step("s1", 1, "slow")), null, cts.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        var state = await execution;

        Assert.Equal(TaskStatus.Canceled, state.Status);
        Assert.Equal(StepStatus.Canceled, state.StepStates[0].Status);
    }

    [Fact]
    public async Task DependsOn_Violation_Skips_Dependent_Step()
    {
        var toolA = new FakeTool("a", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\a.png")));
        var toolB = new FakeTool("b", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\b.png")));
        var executor = CreateExecutor(toolA, toolB);

        var state = await executor.ExecuteAsync(Plan(
            Step("s1", 1, "a"),
            Step("s2", 2, "b", dependsOn: new[] { "missing" })));

        Assert.Equal(StepStatus.Succeeded, state.StepStates[0].Status);
        Assert.Equal(StepStatus.Skipped, state.StepStates[1].Status);
        Assert.Equal(TaskStatus.Failed, state.Status);
        Assert.Empty(toolB.Received);
    }

    [Fact]
    public async Task Plan_With_Resolution_Transfers_To_ToolInput()
    {
        var tool = new FakeTool("QW21edit", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\one.png")));
        var executor = CreateExecutor(tool);
        var policy = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 2048 };
        var plan = new EditPlan
        {
            MainImagePath = Main,
            Resolution = policy,
            Steps = new[] { Step("s1", 1, "QW21edit") },
        };

        var state = await executor.ExecuteAsync(plan);

        Assert.Equal(TaskStatus.Succeeded, state.Status);
        Assert.Same(policy, Assert.Single(tool.Received).Resolution);
    }

    [Fact]
    public async Task Plan_AdditionalImages_Transfers_To_ToolInput()
    {
        var tool = new FakeTool("QW21edit", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\one.png")));
        var executor = CreateExecutor(tool);
        var plan = new EditPlan
        {
            MainImagePath = Main,
            AdditionalImages = new[] { @"C:\img\ref1.png", @"C:\img\ref2.png" },
            Steps = new[] { Step("s1", 1, "QW21edit") },
        };

        var state = await executor.ExecuteAsync(plan);

        Assert.Equal(TaskStatus.Succeeded, state.Status);
        Assert.Equal(
            new[] { @"C:\img\ref1.png", @"C:\img\ref2.png" },
            Assert.Single(tool.Received).AdditionalImages);
    }

    [Fact]
    public async Task Plan_Without_Resolution_Leaves_Null()
    {
        var tool = new FakeTool("QW21edit", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\one.png")));
        var executor = CreateExecutor(tool);

        await executor.ExecuteAsync(Plan(Step("s1", 1, "QW21edit")));

        Assert.Null(Assert.Single(tool.Received).Resolution);
    }

    [Fact]
    public async Task Cancel_Unknown_Task_Returns_False()
    {
        var executor = CreateExecutor(new FakeTool("a", (input, _) => Task.FromResult(Ok(input.StepId, "x"))));

        Assert.False(await executor.CancelAsync("does-not-exist"));
    }

    [Fact]
    public async Task Rerun_Rebuilds_Plan_From_Node_And_Restores_Current()
    {
        var tool = new FakeTool("QW21edit", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\r.png")));
        var session = new EditSession();
        var executor = CreateExecutor(session, TempParser(), tool);

        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var node = session.AppendNode(rootId, @"C:\img\out.png", "把天空换成日落");
        var resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1024 };
        session.SetNodeRerun(node.NodeId, new RerunSpec
        {
            Resolution = resolution,
            AdditionalImages = new[] { @"C:\img\ref.png" },
        });

        var state = await executor.RerunAsync(node.NodeId);

        Assert.Equal(TaskStatus.Succeeded, state.Status);
        var input = Assert.Single(tool.Received);
        // Source image comes from the parent node (the root), not the node's own output.
        Assert.Equal(@"C:\img\root.png", input.MainImagePath);
        Assert.Equal("把天空换成日落", input.Parameters["prompt"]);
        Assert.Same(resolution, input.Resolution);
        Assert.Equal(new[] { @"C:\img\ref.png" }, input.AdditionalImages);
        // The previous current node is restored after the rebuild.
        Assert.Equal(node.NodeId, session.CurrentNodeId);
        Assert.Equal(2, session.GetHistory().Count);
    }

    [Fact]
    public async Task Rerun_Uses_MultiVariant_When_References_Present()
    {
        var tool = new FakeTool("QW21edit", (input, _) => Task.FromResult(Ok(input.StepId, @"C:\out\r.png")));
        var session = new EditSession();
        var executor = CreateExecutor(session, TempParser(), tool);

        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var node = session.AppendNode(rootId, @"C:\img\out.png", "/换背景 一片森林");
        session.SetNodeRerun(node.NodeId, new RerunSpec
        {
            AdditionalImages = new[] { @"C:\img\ref.png" },
        });

        var state = await executor.RerunAsync(node.NodeId);

        Assert.Equal(TaskStatus.Succeeded, state.Status);
        var input = Assert.Single(tool.Received);
        Assert.Contains("<image2>", input.Parameters["prompt"]);
        Assert.Contains("一片森林", input.Parameters["prompt"]);
    }

    [Fact]
    public async Task Rerun_Parentless_Node_Throws()
    {
        var session = new EditSession();
        var executor = CreateExecutor(session, TempParser(),
            new FakeTool("a", (input, _) => Task.FromResult(Ok(input.StepId, "x"))));
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RerunAsync(rootId));
    }

    [Fact]
    public async Task Rerun_Unknown_Node_Throws()
    {
        var session = new EditSession();
        var executor = CreateExecutor(session, TempParser(),
            new FakeTool("a", (input, _) => Task.FromResult(Ok(input.StepId, "x"))));

        await Assert.ThrowsAsync<ArgumentException>(() => executor.RerunAsync("missing"));
    }

    private static Executor CreateExecutor(params IEditTool[] tools)
    {
        var session = new EditSession();
        return CreateExecutor(session, TempParser(), tools);
    }

    private static Executor CreateExecutor(EditSession session, ICommandParser parser, params IEditTool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
        {
            registry.Register(tool);
        }

        return new Executor(registry, new ExecutionQueue(), session, session, parser);
    }

    private static CommandParser TempParser()
        => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"));

    private static EditPlan Plan(params EditStep[] steps)
        => new() { MainImagePath = Main, Steps = steps };

    private static EditStep Step(string stepId, int order, string toolName, string[]? dependsOn = null)
        => new()
        {
            StepId = stepId,
            Order = order,
            ToolName = toolName,
            DependsOn = dependsOn ?? Array.Empty<string>(),
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

    private static ToolResult Ok(string stepId, string outputPath)
        => new()
        {
            StepId = stepId,
            Success = true,
            OutputImagePath = outputPath,
            Duration = TimeSpan.FromMilliseconds(1),
        };

    private sealed class FakeTool : IEditTool
    {
        private readonly Func<ToolInput, CancellationToken, Task<ToolResult>> _handler;

        public FakeTool(string name, Func<ToolInput, CancellationToken, Task<ToolResult>> handler)
        {
            Name = name;
            _handler = handler;
        }

        public string Name { get; }

        public string Description => "fake";

        public IReadOnlyList<string> Capabilities => Array.Empty<string>();

        public List<ToolInput> Received { get; } = new();

        public bool CanHandle(EditStep step) => step.ToolName == Name;

        public Task<ToolResult> ExecuteAsync(
            ToolInput input,
            IProgress<StepProgress>? progress = null,
            CancellationToken ct = default)
        {
            Received.Add(input);
            return _handler(input, ct);
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public InlineProgress(Action<T> onReport)
        {
            _onReport = onReport;
        }

        public void Report(T value) => _onReport(value);
    }
}
