using System.Diagnostics;
using System.Globalization;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tools;

/// <summary>
/// Qwen-Image-2.1 image-edit tool. It forwards a single edit request to the
/// injected <see cref="IInferenceClient"/> and reports the resulting image.
///
/// The tool only orchestrates the request/response; all GPU work happens in the
/// Python backend behind <see cref="IInferenceClient"/> (Z17). A mask is
/// optional: with one the backend does a masked local edit; without one it does
/// a reference-conditioned edit (zero latent + <c>reference_latents</c>). Both
/// are "edits" and share the same tool name (this is <b>not</b> classic
/// img2img, which starts from the source latent — see <c>OPTIMIZATION.md</c>).
///
/// Output always targets a new file (Z24): an explicit <c>output_path</c>
/// parameter wins, otherwise the path is derived from
/// <see cref="ToolInput.WorkingDirectory"/> and the step id so multi-step plans
/// can chain intermediates.
///
/// Note: the IPC <c>submit.payload.op</c> field stays <c>"inpaint"</c>; that is a
/// transport-layer identifier, separate from this C# tool name.
/// </summary>
public sealed class QwenImage21EditTool : IEditTool
{
    /// <summary>Stable tool name written into <see cref="EditStep.ToolName"/> (QW21 = Qwen-Image-2.1).</summary>
    public const string ToolName = "QW21edit";

    private const string DefaultSteps = "25";
    private const string DefaultSeed = "-1";
    private const string DefaultDenoise = "1.0";

    private readonly IInferenceClient _client;

    public QwenImage21EditTool(IInferenceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string Name => ToolName;

    public string Description => "Qwen-Image-2.1 图像编辑（有 mask 时局部编辑；无 mask 时参考条件编辑）";

    public IReadOnlyList<string> Capabilities { get; } =
        new[] { "edit", "inpaint", "reference-edit", "background-replace" };

    public bool CanHandle(EditStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        // No legacy "inpaint" / "img2img" aliasing: those were the old C# tool
        // names and are not registered any more. The IPC op stays "inpaint".
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
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Failure(input.StepId, "QW21edit requires a non-empty 'prompt' parameter.", started.Elapsed);
        }

        var outputPath = ResolveOutputPath(parameters, input);
        var request = new InpaintRequest
        {
            ImagePath = input.MainImagePath,
            MaskPath = input.Mask?.MaskImagePath,
            Prompt = prompt,
            Steps = GetInt(parameters, "steps", int.Parse(DefaultSteps, CultureInfo.InvariantCulture)),
            Seed = GetLong(parameters, "seed", long.Parse(DefaultSeed, CultureInfo.InvariantCulture)),
            Denoise = GetDouble(parameters, "denoise", double.Parse(DefaultDenoise, CultureInfo.InvariantCulture)),
            OutputPath = outputPath,
            Resolution = input.Resolution,
        };

        var stepProgress = progress is null
            ? null
            : new StepProgressAdapter(progress, input.StepId);

        try
        {
            var handle = await _client.SubmitInpaintAsync(request, stepProgress, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Explicit <c>output_path</c> wins; otherwise derive a new file from the
    /// working directory so step N's output can feed step N+1 (Z24).
    ///
    /// A requested path that resolves to the source image is rejected: the
    /// backend would ignore it (Z24) and write a default path instead, so the
    /// returned path would not match the file on disk and multi-step chaining
    /// would break. In that case a step-scoped new file is used instead.
    ///
    /// No logging here: the <c>ZivAiEditor.Tools</c> layer has no logging
    /// infrastructure (kept dependency-free; see ARCHITECTURE.md §4).
    /// </summary>
    private static string? ResolveOutputPath(IReadOnlyDictionary<string, string> parameters, ToolInput input)
    {
        if (parameters.TryGetValue("output_path", out var requested)
            && !string.IsNullOrWhiteSpace(requested)
            && !PathsEqual(requested, input.MainImagePath))
        {
            return requested;
        }

        return DeriveOutputPath(input);
    }

    /// <summary>Step-scoped new file in the working directory, or <c>null</c> to let the backend choose.</summary>
    private static string? DeriveOutputPath(ToolInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.WorkingDirectory))
        {
            return Path.Combine(input.WorkingDirectory, $"{input.StepId}.png");
        }

        return null;
    }

    /// <summary>
    /// Windows-aware path comparison: normalize with <see cref="Path.GetFullPath(string)"/>
    /// and compare case-insensitively (falling back to a raw case-insensitive
    /// compare when a path is malformed).
    /// </summary>
    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

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
