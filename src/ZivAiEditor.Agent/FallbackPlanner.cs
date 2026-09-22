using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// Deterministic planner used as the Z22 fallback (and as the first usable
/// planner, per <c>ARCHITECTURE.md</c> §11.8: "Planner 先确定性兜底"). It never
/// depends on an LLM and, given a valid request, never fails: it emits a single
/// step that hands the user prompt straight to the backend.
///
/// A mask is optional, and both cases target the same <c>QW21edit</c> tool
/// (QW21 = Qwen-Image-2.1). Whether the edit is local or reference-conditioned
/// is carried by <see cref="PlanRequest.Mask"/> → <c>ToolInput.Mask</c>: the tool
/// and the backend pick the masked vs reference path automatically, so no
/// separate img2img tool is needed. This is not classic img2img (which starts
/// from the source latent); see <c>OPTIMIZATION.md</c>.
/// </summary>
public sealed class FallbackPlanner : IPlanner
{
    /// <summary>Tool used for the single edit step (masked or not).</summary>
    public const string EditToolName = "QW21edit";

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

        var step = new EditStep
        {
            Order = 1,
            ToolName = EditToolName,
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