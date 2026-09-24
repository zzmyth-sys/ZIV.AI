using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Tools;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 7 <see cref="QwenImage21OutpaintTool"/> tests (no GPU): the tool must
/// translate a <see cref="ToolInput"/> into an outpainting
/// <see cref="EditRequest"/>, forward it to the injected
/// <see cref="IInferenceClient"/>, relay progress, and report the new output
/// file (Z24).
/// </summary>
public class QwenImage21OutpaintToolTests
{
    [Fact]
    public async Task Execute_Forwards_Parameters_To_InferenceClient()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21OutpaintTool(client);
        var policy = new ResolutionPolicy { Mode = ResolutionMode.Explicit, Width = 2048, Height = 1024 };
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Resolution = policy,
            Parameters = new Dictionary<string, string>
            {
                ["prompt"] = "extend the sky",
                ["anchor"] = "left",
                ["steps"] = "40",
                ["seed"] = "7",
                ["denoise"] = "0.8",
                ["output_path"] = @"C:\out\result.png",
            },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        var request = Assert.IsType<EditRequest>(client.LastRequest);
        Assert.Equal(EditOps.Outpaint, request.Op);
        Assert.Equal(@"C:\img\main.png", request.ImagePath);
        Assert.Null(request.MaskPath);
        Assert.Equal("extend the sky", request.Prompt);
        Assert.Equal("left", request.Anchor);
        Assert.Equal(40, request.Steps);
        Assert.Equal(7, request.Seed);
        Assert.Equal(0.8, request.Denoise);
        Assert.Equal(@"C:\out\result.png", request.OutputPath);
        Assert.Same(policy, request.Resolution);
        Assert.Equal(@"C:\out\result.png", result.OutputImagePath);
    }

    [Fact]
    public async Task Execute_Defaults_Anchor_To_Center()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21OutpaintTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Explicit, Width = 2048, Height = 1024 },
            Parameters = new Dictionary<string, string>(),
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        var request = Assert.IsType<EditRequest>(client.LastRequest);
        Assert.Equal("center", request.Anchor);
        Assert.Equal("", request.Prompt);
    }

    [Fact]
    public async Task Execute_Returns_OutputPath()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21OutpaintTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Explicit, Width = 2048, Height = 1024 },
            Parameters = new Dictionary<string, string> { ["output_path"] = @"C:\out\result.png" },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        Assert.Equal(@"C:\out\result.png", result.OutputImagePath);
    }

    [Fact]
    public async Task Execute_WithoutOutputPath_Derives_From_WorkingDirectory()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21OutpaintTool(client);
        var input = new ToolInput
        {
            StepId = "step-2",
            MainImagePath = @"C:\img\main.png",
            WorkingDirectory = @"C:\out",
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Explicit, Width = 2048, Height = 1024 },
            Parameters = new Dictionary<string, string>(),
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        var expected = Path.Combine(@"C:\out", "step-2.png");
        Assert.Equal(expected, result.OutputImagePath);
        Assert.Equal(expected, client.LastRequest!.OutputPath);
    }

    [Fact]
    public async Task Execute_Reports_Progress()
    {
        var client = new FakeInferenceClient { ProgressFractions = new[] { 0.25, 0.75, 1.0 } };
        var tool = new QwenImage21OutpaintTool(client);
        var reports = new List<StepProgress>();
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Explicit, Width = 2048, Height = 1024 },
            Parameters = new Dictionary<string, string>(),
        };

        await tool.ExecuteAsync(input, new InlineProgress<StepProgress>(reports.Add));

        Assert.Equal(3, reports.Count);
        Assert.All(reports, report => Assert.Equal("s1", report.StepId));
        Assert.Equal(new[] { 0.25, 0.75, 1.0 }, reports.Select(report => report.Fraction));
    }

    [Fact]
    public async Task Execute_MissingResolution_Returns_Failure_Without_Submit()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21OutpaintTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Parameters = new Dictionary<string, string>(),
        };

        var result = await tool.ExecuteAsync(input);

        Assert.False(result.Success);
        Assert.Null(client.LastRequest);
    }

    [Fact]
    public async Task Execute_NonExplicitResolution_Returns_Failure_Without_Submit()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21OutpaintTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 },
            Parameters = new Dictionary<string, string>(),
        };

        var result = await tool.ExecuteAsync(input);

        Assert.False(result.Success);
        Assert.Null(client.LastRequest);
    }

    [Fact]
    public void QwenImage21OutpaintTool_CanHandle_QW21outpaint_Returns_True()
    {
        var tool = new QwenImage21OutpaintTool(new FakeInferenceClient());

        Assert.True(tool.CanHandle(new EditStep { ToolName = "QW21outpaint" }));
        Assert.False(tool.CanHandle(new EditStep { ToolName = "QW21edit" }));
        Assert.False(tool.CanHandle(new EditStep { ToolName = "outpaint" }));
    }

    private sealed class FakeInferenceClient : IInferenceClient
    {
        public EditRequest? LastRequest { get; private set; }

        public double[] ProgressFractions { get; init; } = Array.Empty<double>();

        public Task<InferenceTaskHandle> SubmitInpaintAsync(
            InpaintRequest request,
            IProgress<InferenceProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InferenceTaskHandle> SubmitEditAsync(
            EditRequest request,
            IProgress<InferenceProgress>? progress = null,
            CancellationToken ct = default)
        {
            LastRequest = request;
            foreach (var fraction in ProgressFractions)
            {
                progress?.Report(new InferenceProgress { Fraction = fraction, Message = $"step {fraction}" });
            }

            return Task.FromResult(new InferenceTaskHandle
            {
                TaskId = "task-1",
                Status = TaskStatus.Succeeded,
            });
        }

        public Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);

        public void Dispose()
        {
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