using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// Deterministic planner used as the Z22 fallback (and as the first usable
/// planner, per <c>ARCHITECTURE.md</c> §11.8: "Planner 先确定性兜底"). It never
/// depends on an LLM and, given a valid request, never fails: it emits a single
/// step that hands the user prompt straight to the backend.
/// </summary>
public sealed class FallbackPlanner : IPlanner
{
    /// <summary>Tool used when a mask is present (turn the prompt into a local edit).</summary>
    public const string InpaintToolName = "inpaint";

    /// <summary>Tool used when no mask is present (edit the whole image).</summary>
    public const string ImageToImageToolName = "img2img";

    public Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.MainImagePath))
        {
            throw new ArgumentException(
                "PlanRequest.MainImagePath is required (SPEC.md §3.1: the main image is mandatory).",
                nameof(request));
        }

        var useMask = request.Mask is not null;
        var step = new EditStep
        {
            Order = 1,
            ToolName = useMask ? InpaintToolName : ImageToImageToolName,
            Parameters = new Dictionary<string, string>
            {
                ["prompt"] = request.Prompt ?? "",
                ["steps"] = DefaultSteps,
                ["denoise"] = DefaultDenoise,
            },
        };

        var plan = new EditPlan
        {
            SourcePrompt = request.Prompt ?? "",
            MainImagePath = request.MainImagePath,
            ReferenceImagePath = request.ReferenceImagePath,
            Mask = request.Mask,
            Steps = new[] { step },
        };

        return Task.FromResult(plan);
    }

    /// <summary>Default sampling steps, kept in sync with <c>InpaintRequest.Steps</c> (FROZEN Step 6.3).</summary>
    public const string DefaultSteps = "25";

    /// <summary>Full-denoise edit, matching <c>InpaintRequest.Denoise</c>.</summary>
    public const string DefaultDenoise = "1.0";
}