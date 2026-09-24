using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Tools;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 6 <see cref="QwenImage21EditTool"/> tests (no GPU): the tool must
/// translate a <see cref="ToolInput"/> into an <see cref="EditRequest"/>,
/// forward it to the injected <see cref="IInferenceClient"/>, relay progress,
/// and report the new output file (Z24).
/// </summary>
public class QwenImage21EditToolTests
{
    [Fact]
    public async Task Execute_Forwards_Parameters_To_InferenceClient()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Mask = new MaskSpec { MaskImagePath = @"C:\img\mask.png" },
            Parameters = new Dictionary<string, string>
            {
                ["prompt"] = "teahouse background",
                ["steps"] = "40",
                ["seed"] = "7",
                ["denoise"] = "0.8",
                ["output_path"] = @"C:\out\result.png",
            },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        var request = Assert.IsType<EditRequest>(client.LastRequest);
        Assert.Equal(EditOps.Inpaint, request.Op);
        Assert.Equal(@"C:\img\main.png", request.ImagePath);
        Assert.Equal(@"C:\img\mask.png", request.MaskPath);
        Assert.Equal("teahouse background", request.Prompt);
        Assert.Equal(40, request.Steps);
        Assert.Equal(7, request.Seed);
        Assert.Equal(0.8, request.Denoise);
        Assert.Equal(@"C:\out\result.png", request.OutputPath);
    }

    [Fact]
    public async Task Execute_Returns_OutputPath()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Parameters = new Dictionary<string, string>
            {
                ["prompt"] = "x",
                ["output_path"] = @"C:\out\result.png",
            },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        Assert.Equal(@"C:\out\result.png", result.OutputImagePath);
    }

    [Fact]
    public async Task Execute_WithoutOutputPath_Derives_From_WorkingDirectory()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "step-2",
            MainImagePath = @"C:\img\main.png",
            WorkingDirectory = @"C:\out",
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        Assert.Equal(Path.Combine(@"C:\out", "step-2.png"), result.OutputImagePath);
        Assert.Equal(Path.Combine(@"C:\out", "step-2.png"), client.LastRequest!.OutputPath);
    }

    [Theory]
    [InlineData(@"C:\img\main.png")]
    [InlineData(@"c:\IMG\MAIN.PNG")]
    [InlineData(@"C:\img\sub\..\main.png")]
    public async Task QwenImage21EditTool_OutputPath_Equals_MainImage_Falls_Back(string requested)
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            WorkingDirectory = @"C:\out",
            Parameters = new Dictionary<string, string>
            {
                ["prompt"] = "x",
                ["output_path"] = requested,
            },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        var expected = Path.Combine(@"C:\out", "s1.png");
        Assert.Equal(expected, result.OutputImagePath);
        Assert.Equal(expected, client.LastRequest!.OutputPath);
    }

    [Fact]
    public async Task Execute_ReferenceImagePath_Becomes_First_AdditionalImage()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            ReferenceImagePath = @"C:\img\ref.png",
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        Assert.Equal(new[] { @"C:\img\ref.png" }, client.LastRequest!.AdditionalImages);
    }

    [Fact]
    public async Task Execute_AdditionalImages_Keep_Legacy_Then_Order()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            ReferenceImagePath = @"C:\img\legacy.png",
            AdditionalImages = new[] { @"C:\img\r1.png", @"C:\img\r2.png" },
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        Assert.Equal(
            new[] { @"C:\img\legacy.png", @"C:\img\r1.png", @"C:\img\r2.png" },
            client.LastRequest!.AdditionalImages);
    }

    [Fact]
    public async Task Execute_AdditionalImages_Drop_Blanks()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            ReferenceImagePath = "   ",
            AdditionalImages = new[] { "", @"C:\img\r1.png", "  ", @"C:\img\r2.png" },
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        Assert.Equal(new[] { @"C:\img\r1.png", @"C:\img\r2.png" }, client.LastRequest!.AdditionalImages);
    }

    [Fact]
    public async Task Execute_No_References_Produces_Empty_AdditionalImages()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        Assert.Empty(client.LastRequest!.AdditionalImages);
    }

    [Fact]
    public async Task Execute_Reports_Progress()
    {
        var client = new FakeInferenceClient { ProgressFractions = new[] { 0.25, 0.75, 1.0 } };
        var tool = new QwenImage21EditTool(client);
        var reports = new List<StepProgress>();
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input, new InlineProgress<StepProgress>(reports.Add));

        Assert.Equal(3, reports.Count);
        Assert.All(reports, report => Assert.Equal("s1", report.StepId));
        Assert.Equal(new[] { 0.25, 0.75, 1.0 }, reports.Select(report => report.Fraction));
    }

    [Fact]
    public async Task QwenImage21EditTool_No_MainImage_WithPrompt_Uses_T2I()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = "   ",
            Mask = new MaskSpec { MaskImagePath = @"C:\img\mask.png" },
            Parameters = new Dictionary<string, string> { ["prompt"] = "a mountain lake" },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        var request = Assert.IsType<EditRequest>(client.LastRequest);
        Assert.Equal(EditOps.T2I, request.Op);
        Assert.Null(request.MaskPath);
        Assert.Null(request.ImagePath);
    }

    [Fact]
    public async Task QwenImage21EditTool_T2I_NoWorkingDirectory_Derives_OutputPath()
    {
        // T2I has no source image, so the Executor supplies no working directory;
        // the tool must still return a usable (non-null) output path.
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = "",
            WorkingDirectory = "",
            Parameters = new Dictionary<string, string> { ["prompt"] = "a mountain lake" },
        };

        var result = await tool.ExecuteAsync(input);

        Assert.True(result.Success);
        var expected = Path.Combine(Path.GetTempPath(), "zivai", "s1.png");
        Assert.Equal(expected, result.OutputImagePath);
        Assert.Equal(expected, client.LastRequest!.OutputPath);
    }

    [Fact]
    public async Task Execute_MissingPrompt_Returns_Failure()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
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
    public async Task Execute_Forwards_Resolution_To_InpaintRequest()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var policy = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536, MaxPixels = 4_194_304 };
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Resolution = policy,
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        Assert.Same(policy, client.LastRequest!.Resolution);
    }

    [Fact]
    public async Task Execute_Null_Resolution_Remains_Null()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\main.png",
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        Assert.Null(client.LastRequest!.Resolution);
    }

    [Fact]
    public void QwenImage21EditTool_CanHandle_QW21edit_Returns_True()
    {
        var tool = new QwenImage21EditTool(new FakeInferenceClient());

        Assert.True(tool.CanHandle(new EditStep { ToolName = "QW21edit" }));
        Assert.False(tool.CanHandle(new EditStep { ToolName = "inpaint" }));
        Assert.False(tool.CanHandle(new EditStep { ToolName = "img2img" }));
    }

    [Fact]
    public void Tool_Registers_Under_QW21edit_Only()
    {
        var registry = new ToolRegistry();
        var tool = new QwenImage21EditTool(new FakeInferenceClient());

        registry.Register(tool);

        Assert.Same(tool, registry.Get("QW21edit"));
        Assert.Null(registry.Get("inpaint"));
        Assert.Null(registry.Get("img2img"));
    }

    [Fact]
    public async Task Lora_Is_Forwarded_To_EditRequest()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var lora = new LoraOptions { Path = "anime_v2", StrengthModel = 0.8, StrengthClip = 0.7 };
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\a.png",
            Lora = lora,
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        var request = Assert.IsType<EditRequest>(client.LastRequest);
        Assert.Same(lora, request.Lora);
    }

    [Fact]
    public async Task ModelId_Is_Forwarded_To_EditRequest()
    {
        var client = new FakeInferenceClient();
        var tool = new QwenImage21EditTool(client);
        var input = new ToolInput
        {
            StepId = "s1",
            MainImagePath = @"C:\img\a.png",
            ModelId = "beta",
            Parameters = new Dictionary<string, string> { ["prompt"] = "x" },
        };

        await tool.ExecuteAsync(input);

        Assert.Equal("beta", Assert.IsType<EditRequest>(client.LastRequest).ModelId);
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
