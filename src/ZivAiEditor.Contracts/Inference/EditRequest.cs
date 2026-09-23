namespace ZivAiEditor.Contracts.Inference;

using ZivAiEditor.Contracts.Imaging;

/// <summary>
/// Generalized single-inference request (Step 7). It carries the operation kind
/// plus every field the supported operations share, so one submit entry point
/// (<see cref="IInferenceClient.SubmitEditAsync"/>) can express text-to-image,
/// masked editing and outpainting. The frozen <see cref="InpaintRequest"/> stays
/// unchanged and is expressed as an <see cref="EditRequest"/> with
/// <see cref="Op"/> = <see cref="EditOps.Inpaint"/>.
///
/// <para><b>Op / field constraints</b> (values in <see cref="EditOps"/>):</para>
/// <list type="bullet">
/// <item><description><c>t2i</c>: <see cref="ImagePath"/> is <see langword="null"/>;
/// <see cref="MaskPath"/> and <see cref="Anchor"/> are ignored; <see cref="Resolution"/>
/// must not use <see cref="ResolutionMode.Scale"/> (there is no source long edge).
/// <see cref="Prompt"/> must be non-empty.</description></item>
/// <item><description><c>inpaint</c>: <see cref="ImagePath"/> is required;
/// <see cref="MaskPath"/> is optional (masked local edit vs. reference-conditioned edit);
/// <see cref="Anchor"/> is ignored.</description></item>
/// <item><description><c>outpaint</c>: <see cref="ImagePath"/> is required;
/// <see cref="MaskPath"/> is produced by the backend; <see cref="Anchor"/> selects the
/// source placement (9-grid, see <see cref="Anchor"/>); <see cref="Resolution"/> should
/// be <see cref="ResolutionMode.Explicit"/> to give the target canvas size.</description></item>
/// </list>
///
/// <para><b>Multi-image editing</b> (Step 9C.5-D): the first image (<see cref="ImagePath"/>)
/// is the main image; <see cref="AdditionalImages"/> carries optional reference images.
/// The user prompt references them positionally with the <c>&lt;imageN&gt;</c> token
/// (<c>&lt;image1&gt;</c> = main, <c>&lt;image2&gt;</c> = first additional, ...). The tokenizer
/// inserts those markers automatically; the prompt itself passes through verbatim.</para>
/// </summary>
public sealed class EditRequest
{
    /// <summary>
    /// Operation kind. Use <see cref="EditOps.T2I"/> / <see cref="EditOps.Inpaint"/> /
    /// <see cref="EditOps.Outpaint"/>; see the type-level remarks for per-op field rules.
    /// </summary>
    public string Op { get; init; } = EditOps.Inpaint;

    /// <summary>Source image path; <see langword="null"/> for text-to-image (<c>t2i</c>).</summary>
    public string? ImagePath { get; init; }

    /// <summary>Binary mask PNG (Z19); optional for <c>inpaint</c>, backend-generated for <c>outpaint</c>.</summary>
    public string? MaskPath { get; init; }

    public string Prompt { get; init; } = "";

    public int Steps { get; init; } = 25;

    public long Seed { get; init; } = -1;

    public double Denoise { get; init; } = 1.0;

    public string? OutputPath { get; init; }

    /// <summary>Optional output resolution (Step 6.5); null = backend default.</summary>
    public ResolutionPolicy? Resolution { get; init; }

    public LoraOptions? Lora { get; init; }

    public OptimizationOptions? Optimizations { get; init; }

    /// <summary>
    /// Outpaint placement of the source inside the expanded canvas (9-grid):
    /// <c>center</c> / <c>left</c> / <c>right</c> / <c>top</c> / <c>bottom</c> /
    /// <c>top-left</c> / <c>top-right</c> / <c>bottom-left</c> / <c>bottom-right</c>.
    /// <see langword="null"/> lets the backend default to <c>center</c>; an
    /// unrecognized value also degrades to <c>center</c> on the backend.
    /// </summary>
    public string? Anchor { get; init; }

    /// <summary>
    /// Ordered reference images (Step 9C.5-D) after the main image; the backend encodes
    /// them as additional <c>&lt;imageN&gt;</c> inputs. Empty = no references.
    /// </summary>
    public IReadOnlyList<string> AdditionalImages { get; init; } = Array.Empty<string>();
}
