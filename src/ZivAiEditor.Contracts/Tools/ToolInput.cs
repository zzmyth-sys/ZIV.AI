using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Contracts.Tools;

public sealed class ToolInput
{
    public string StepId { get; init; } = "";
    public string MainImagePath { get; init; } = "";
    public string? ReferenceImagePath { get; init; }

    /// <summary>Ordered reference images after the main image (Step 9C.5-D); empty = none.</summary>
    public IReadOnlyList<string> AdditionalImages { get; init; } = Array.Empty<string>();
    public MaskSpec? Mask { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();
    public string WorkingDirectory { get; init; } = "";

    /// <summary>Optional output resolution (Step 6.5); null = backend default.</summary>
    public ResolutionPolicy? Resolution { get; init; }

    /// <summary>Optional LoRA for this step (Step 8-1); null = none. Carried to the tool.</summary>
    public LoraOptions? Lora { get; init; }

    /// <summary>Optional model id (Step 8-2); <c>null</c> = the default model.</summary>
    public string? ModelId { get; init; }
}
