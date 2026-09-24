using System.Diagnostics;
using System.Globalization;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Tools;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tools;

/// <summary>
/// Qwen-Image-2.1 outpainting tool (Step 7). It expands the source canvas and
/// forwards a single <see cref="EditRequest"/> with
/// <see cref="EditOps.Outpaint"/> to the injected <see cref="IInferenceClient"/>.
///
/// The backend pastes <see cref="ToolInput.MainImagePath"/> at
/// <c>anchor</c> (9-grid, default <c>center</c>) and generates the new region
/// from <c>prompt</c>. The target canvas must be explicit
/// (<see cref="ResolutionMode.Explicit"/>); without it the tool fails without
/// submitting.
///
/// Output always targets a new file (Z24): an explicit <c>output_path</c>
/// parameter wins, otherwise the path is derived from
/// <see cref="ToolInput.WorkingDirectory"/> and the step id so multi-step plans
/// can chain intermediates.
/// </summary>
public sealed class QwenImage21OutpaintTool : IEditTool
{
    /// <summary>Stable tool name written into <see cref="EditStep.ToolName"/> (QW21 = Qwen-Image-2.1).</summary>
    public const string ToolName = "QW21outpaint";

    private const string DefaultSteps = "25";
    private const string DefaultSeed = "-1";
    private const string DefaultDenoise = "1.0";
    private const string DefaultAnchor = "center";

    private readonly IInferenceClient _client;

    public QwenImage21OutpaintTool(IInferenceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string Name => ToolName;

    public string Description => "Qwen-Image-2.1 扩图（按 anchor 扩展画布，prompt 描述扩展内容）";

    public IReadOnlyList<string> Capabilities { get; } =
        new[] { "outpaint", "expand", "extend-canvas" };

    public bool CanHandle(EditStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return string.Equals(step.ToolName, ToolName, StringComparison.Ordinal);
    }

    public async Task<ToolResult> ExecuteAsync(
        ToolInput input,
        IProgress<StepProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var started = Stopwatch.StartNew();
        var parameters = input.Parameters;

        var prompt = GetString(parameters, "prompt") ?? "";
        var anchor = GetString(parameters, "anchor") ?? DefaultAnchor;

        var resolution = input.Resolution;
        if (resolution is null || resolution.Mode != ResolutionMode.Explicit)
        {
            return Failure(
                input.StepId,
                "QW21outpaint requires an explicit resolution (ResolutionMode.Explicit).",
                started.Elapsed);
        }

        var outputPath = ToolOutputPath.Resolve(parameters, input);
        var request = new EditRequest
        {
            Op = EditOps.Outpaint,
            ImagePath = input.MainImagePath,
            MaskPath = null,
            Prompt = prompt,
            Steps = GetInt(parameters, "steps", int.Parse(DefaultSteps, CultureInfo.InvariantCulture)),
            Seed = GetLong(parameters, "seed", long.Parse(DefaultSeed, CultureInfo.InvariantCulture)),
            Denoise = GetDouble(parameters, "denoise", double.Parse(DefaultDenoise, CultureInfo.InvariantCulture)),
            OutputPath = outputPath,
            Resolution = resolution,
            Anchor = anchor,
            // Step 8-2: carry the plan's model id (null = backend default).
            ModelId = input.ModelId,
        };

        var stepProgress = progress is null
            ? null
            : new StepProgressAdapter(progress, input.StepId);

        try
        {
            var handle = await _client.SubmitEditAsync(request, stepProgress, ct).ConfigureAwait(false);
            if (handle.Status != TaskStatus.Succeeded)
            {
                return Failure(input.StepId, $"inference ended with status {handle.Status}.", started.Elapsed);
            }

            return new ToolResult
            {
                StepId = input.StepId,
                Success = true,
                OutputImagePath = outputPath,
                Duration = started.Elapsed,
                Metadata = new Dictionary<string, string>
                {
                    ["task_id"] = handle.TaskId,
                },
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Let the Executor classify cancellation (it owns the task state).
            throw;
        }
        catch (Exception ex)
        {
            return Failure(input.StepId, ex.Message, started.Elapsed);
        }
    }

    private static ToolResult Failure(string stepId, string message, TimeSpan duration)
        => new()
        {
            StepId = stepId,
            Success = false,
            ErrorMessage = message,
            Duration = duration,
        };

    private static string? GetString(IReadOnlyDictionary<string, string> parameters, string key)
        => parameters.TryGetValue(key, out var value) ? value : null;

    private static int GetInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
        => parameters.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

    private static long GetLong(IReadOnlyDictionary<string, string> parameters, string key, long fallback)
        => parameters.TryGetValue(key, out var value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

    private static double GetDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
        => parameters.TryGetValue(key, out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

    /// <summary>Forwards <see cref="InferenceProgress"/> as <see cref="StepProgress"/> synchronously.</summary>
    private sealed class StepProgressAdapter : IProgress<InferenceProgress>
    {
        private readonly IProgress<StepProgress> _inner;
        private readonly string _stepId;

        public StepProgressAdapter(IProgress<StepProgress> inner, string stepId)
        {
            _inner = inner;
            _stepId = stepId;
        }

        public void Report(InferenceProgress value)
            => _inner.Report(new StepProgress
            {
                StepId = _stepId,
                Fraction = value.Fraction,
                Message = value.Message,
            });
    }
}