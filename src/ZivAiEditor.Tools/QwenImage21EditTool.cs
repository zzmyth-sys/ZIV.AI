using System.Diagnostics;
using System.Globalization;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Tools;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tools;

/// <summary>
/// Qwen-Image-2.1 image-edit tool. It forwards a single edit request to the
/// injected <see cref="IInferenceClient"/> and reports the resulting image.
///
/// The tool only orchestrates the request/response; all GPU work happens in the
/// Python backend behind <see cref="IInferenceClient"/> (Z17). The op is chosen
/// from the input: with a non-empty <see cref="ToolInput.MainImagePath"/> it is
/// <see cref="EditOps.Inpaint"/> — a mask makes the backend do a masked local
/// edit, no mask makes it a reference-conditioned edit (zero latent +
/// <c>reference_latents</c>; this is <b>not</b> classic img2img, which starts
/// from the source latent — see <c>OPTIMIZATION.md</c>). Without a main image the
/// non-empty prompt selects <see cref="EditOps.T2I"/> (text-to-image).
///
/// Output always targets a new file (Z24): an explicit <c>output_path</c>
/// parameter wins, otherwise the path is derived from
/// <see cref="ToolInput.WorkingDirectory"/> and the step id so multi-step plans
/// can chain intermediates.
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
        // names and are not registered any more.
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

        var prompt = ToolParameters.GetString(parameters, "prompt") ?? "";
        var hasReferences = !string.IsNullOrWhiteSpace(input.ReferenceImagePath)
            || input.AdditionalImages.Count > 0;
        // The guard looks at the user's raw description, not the substituted template: a
        // command that opted into allow_empty_prompt fills the template with fixed English
        // text even when the description is empty, so checking `prompt` alone cannot tell a
        // meaningful edit from a degenerate one. A missing "description" key means the
        // command owns a fixed template (e.g. /去水印), so fall back to the prompt itself.
        var description = ToolParameters.GetString(parameters, "description");
        var hasText = description is not null
            ? !string.IsNullOrWhiteSpace(description)
            : !string.IsNullOrWhiteSpace(prompt);
        if (!hasText && !hasReferences)
        {
            return Failure(
                input.StepId,
                "QW21edit requires a non-empty description or prompt, or at least one reference image.",
                started.Elapsed);
        }

        var op = string.IsNullOrWhiteSpace(input.MainImagePath)
            ? EditOps.T2I
            : EditOps.Inpaint;

        var outputPath = ToolOutputPath.Resolve(parameters, input);
        var request = new EditRequest
        {
            Op = op,
            // t2i has no source image: send null, not a blank path, so the
            // backend takes its no-source branch.
            ImagePath = op == EditOps.T2I ? null : input.MainImagePath,
            MaskPath = op == EditOps.Inpaint ? input.Mask?.MaskImagePath : null,
            Prompt = prompt,
            Steps = ToolParameters.GetInt(parameters, "steps", int.Parse(DefaultSteps, CultureInfo.InvariantCulture)),
            Seed = ToolParameters.GetLong(parameters, "seed", long.Parse(DefaultSeed, CultureInfo.InvariantCulture)),
            Denoise = ToolParameters.GetDouble(parameters, "denoise", double.Parse(DefaultDenoise, CultureInfo.InvariantCulture)),
            OutputPath = outputPath,
            Resolution = input.Resolution,
            // R3 (Step 9C.5-D): the legacy reference slot is image2, followed by the
            // ordered additional references. Single owner of that ordering.
            AdditionalImages = BuildAdditionalImages(input.ReferenceImagePath, input.AdditionalImages),
            // T3.2: forward the step's LoRAs (multi-slot) to the backend.
            Loras = input.EffectiveLoras.Count > 0 ? input.EffectiveLoras.ToList() : null,
            // Step 8-2: forward the plan's model id to the backend.
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

    /// <summary>
    /// Ordered reference images for the request (Step 9C.5-D): the legacy
    /// <see cref="ToolInput.ReferenceImagePath"/> (image2) first, then
    /// <see cref="ToolInput.AdditionalImages"/>; blank entries are dropped.
    /// </summary>
    private static IReadOnlyList<string> BuildAdditionalImages(
        string? referenceImagePath,
        IReadOnlyList<string> additionalImages)
    {
        var images = new List<string>();
        if (!string.IsNullOrWhiteSpace(referenceImagePath))
        {
            images.Add(referenceImagePath);
        }

        foreach (var path in additionalImages ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                images.Add(path);
            }
        }

        return images;
    }

    private static ToolResult Failure(string stepId, string message, TimeSpan duration)
        => new()
        {
            StepId = stepId,
            Success = false,
            ErrorMessage = message,
            Duration = duration,
        };

}
