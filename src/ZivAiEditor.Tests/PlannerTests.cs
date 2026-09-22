using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 5 planner tests (no GPU). Covers the Z22 degradation chain:
/// <see cref="LlmPlanner"/> produces a plan on valid JSON and throws on
/// malformed output, <see cref="FallbackPlanner"/> is a deterministic never-fail
/// floor, and <see cref="ResilientPlanner"/> bridges the two.
/// </summary>
public class PlannerTests
{
    private static PlanRequest Request(string? mask = null)
    {
        return new PlanRequest
        {
            MainImagePath = @"C:\img\main.png",
            ReferenceImagePath = null,
            Mask = mask is null ? null : new MaskSpec { MaskImagePath = mask },
            Prompt = "replace the sky with a sunset",
        };
    }

    [Fact]
    public async Task FallbackPlanner_No_Mask_Produces_Inpaint_Tool()
    {
        var plan = await new FallbackPlanner().PlanAsync(Request());

        Assert.Equal("replace the sky with a sunset", plan.SourcePrompt);
        Assert.Equal(@"C:\img\main.png", plan.MainImagePath);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(1, step.Order);
        Assert.Equal(FallbackPlanner.EditToolName, step.ToolName);
        Assert.Empty(step.DependsOn);
        Assert.Equal("replace the sky with a sunset", step.Parameters["prompt"]);
        Assert.Equal(FallbackPlanner.DefaultSteps, step.Parameters["steps"]);
        Assert.Equal(FallbackPlanner.DefaultDenoise, step.Parameters["denoise"]);
    }

    [Fact]
    public async Task FallbackPlanner_With_Mask_Produces_Inpaint_Tool()
    {
        var plan = await new FallbackPlanner().PlanAsync(Request(@"C:\img\mask.png"));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(FallbackPlanner.EditToolName, step.ToolName);
        Assert.NotNull(plan.Mask);
    }

    [Fact]
    public async Task FallbackPlanner_MissingMainImage_WithPrompt_Produces_T2I()
    {
        var request = new PlanRequest { Prompt = "a cat" };

        var plan = await new FallbackPlanner().PlanAsync(request);

        Assert.True(string.IsNullOrWhiteSpace(plan.MainImagePath));
        var step = Assert.Single(plan.Steps);
        Assert.Equal(FallbackPlanner.EditToolName, step.ToolName);
        Assert.Equal("a cat", step.Parameters["prompt"]);
    }

    [Fact]
    public async Task FallbackPlanner_MissingMainImage_NoPrompt_Throws()
    {
        var request = new PlanRequest();
        await Assert.ThrowsAsync<ArgumentException>(() => new FallbackPlanner().PlanAsync(request));
    }

    [Fact]
    public async Task LlmPlanner_Parses_Json_Into_Plan()
    {
        const string json =
            "{\"steps\":[{\"tool\":\"inpaint\",\"params\":{\"prompt\":\"snow\",\"steps\":\"40\",\"denoise\":\"0.8\"},\"depends_on\":[]}]}";
        var planner = new LlmPlanner(new FakeLlmClient(json), new EmptyToolRegistry());

        var plan = await planner.PlanAsync(Request(@"C:\img\mask.png"));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(1, step.Order);
        Assert.Equal("inpaint", step.ToolName);
        Assert.Equal("snow", step.Parameters["prompt"]);
        Assert.Equal("40", step.Parameters["steps"]);
        Assert.Equal("0.8", step.Parameters["denoise"]);
    }

    [Fact]
    public async Task LlmPlanner_Tolerates_Fenced_Json()
    {
        const string response =
            "Here is the plan:\n```json\n{\"steps\":[{\"tool\":\"img2img\",\"params\":{\"prompt\":\"oil\"}}]}\n```\n";
        var planner = new LlmPlanner(new FakeLlmClient(response), new EmptyToolRegistry());

        var plan = await planner.PlanAsync(Request());

        var step = Assert.Single(plan.Steps);
        Assert.Equal("img2img", step.ToolName);
    }

    [Fact]
    public async Task LlmPlanner_No_MainImage_WithPrompt_Produces_T2I()
    {
        const string json =
            "{\"steps\":[{\"tool\":\"QW21edit\",\"params\":{\"prompt\":\"a cat\"}}]}";
        var planner = new LlmPlanner(new FakeLlmClient(json), new EmptyToolRegistry());
        var request = new PlanRequest { Prompt = "a cat" };

        var plan = await planner.PlanAsync(request);

        Assert.True(string.IsNullOrWhiteSpace(plan.MainImagePath));
        var step = Assert.Single(plan.Steps);
        Assert.Equal(FallbackPlanner.EditToolName, step.ToolName);
        Assert.Equal("a cat", step.Parameters["prompt"]);
    }

    [Fact]
    public async Task LlmPlanner_No_MainImage_NoPrompt_Throws()
    {
        var llm = new FakeLlmClient("{\"steps\":[{\"tool\":\"QW21edit\",\"params\":{\"prompt\":\"x\"}}]}");
        var planner = new LlmPlanner(llm, new EmptyToolRegistry());

        await Assert.ThrowsAsync<ArgumentException>(() => planner.PlanAsync(new PlanRequest()));

        Assert.Equal(0, llm.CallCount);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"steps\": [] }")]
    [InlineData("{\"steps\":[{\"params\":{\"prompt\":\"x\"}}]}")]
    [InlineData("")]
    public async Task LlmPlanner_BadOutput_Throws_PlannerException(string response)
    {
        var planner = new LlmPlanner(new FakeLlmClient(response), new EmptyToolRegistry());

        await Assert.ThrowsAsync<PlannerException>(() => planner.PlanAsync(Request()));
    }

    [Fact]
    public async Task LlmPlanner_Timeout_Throws_PlannerException()
    {
        var planner = new LlmPlanner(
            new FakeLlmClient(delay: TimeSpan.FromSeconds(5), response: "{}"),
            new EmptyToolRegistry(),
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<PlannerException>(() => planner.PlanAsync(Request()));
    }

    [Fact]
    public async Task ResilientPlanner_Degrades_To_Fallback_On_Llm_Failure()
    {
        var degraded = new List<Exception>();
        var planner = new ResilientPlanner(
            new LlmPlanner(new FakeLlmClient("garbage"), new EmptyToolRegistry()),
            new FallbackPlanner(),
            degraded.Add);

        var plan = await planner.PlanAsync(Request());

        var step = Assert.Single(plan.Steps);
        Assert.Equal(FallbackPlanner.EditToolName, step.ToolName);
        Assert.Single(degraded);
        Assert.IsType<PlannerException>(degraded[0]);
    }

    [Fact]
    public async Task ResilientPlanner_Uses_Primary_When_It_Succeeds()
    {
        const string json = "{\"steps\":[{\"tool\":\"inpaint\",\"params\":{\"prompt\":\"a\"}}]}";
        var planner = new ResilientPlanner(
            new LlmPlanner(new FakeLlmClient(json), new EmptyToolRegistry()),
            new FallbackPlanner());

        var plan = await planner.PlanAsync(Request(@"C:\img\mask.png"));

        var step = Assert.Single(plan.Steps);
        Assert.Equal("inpaint", step.ToolName);
    }

    [Fact]
    public async Task LlmPlanner_Parses_MultiStep_Json_With_Order_And_DependsOn()
    {
        const string json =
            "{\"steps\":[" +
            "{\"tool\":\"img2img\",\"params\":{\"prompt\":\"ink style\"},\"depends_on\":[]}," +
            "{\"tool\":\"inpaint\",\"params\":{\"prompt\":\"teahouse background\",\"steps\":\"40\"},\"depends_on\":[\"step-1\"]}" +
            "]}";
        var planner = new LlmPlanner(new FakeLlmClient(json), new EmptyToolRegistry());

        var plan = await planner.PlanAsync(Request(@"C:\img\mask.png"));

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(1, plan.Steps[0].Order);
        Assert.Equal("img2img", plan.Steps[0].ToolName);
        Assert.Equal(2, plan.Steps[1].Order);
        Assert.Equal("inpaint", plan.Steps[1].ToolName);
        Assert.Equal("40", plan.Steps[1].Parameters["steps"]);
        Assert.Equal(new[] { "step-1" }, plan.Steps[1].DependsOn);
    }

    private sealed class FakeLlmClient : ILlmClient
    {
        private readonly string _response;
        private readonly TimeSpan _delay;

        public FakeLlmClient(string response, TimeSpan delay = default)
        {
            _response = response;
            _delay = delay;
        }

        public int CallCount { get; private set; }

        public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            CallCount++;
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, ct);
            }

            return _response;
        }

        public void Dispose()
        {
        }
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