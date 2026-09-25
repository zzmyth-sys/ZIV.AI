using System.Text.Json.Serialization;
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

    /// <summary>
    /// Legacy single LoRA slot (Step 8-1; read-only compatibility from T3.2). New code sets
    /// <see cref="Loras"/>.
    /// </summary>
    public LoraOptions? Lora { get; init; }

    /// <summary>Multi-slot LoRAs (T3.2); when non-empty it wins over <see cref="Lora"/>.</summary>
    [JsonPropertyName("loras")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LoraOptions>? Loras { get; init; }

    /// <summary>
    /// Effective LoRAs (T3.2): <see cref="Loras"/> de-duplicated by path (first wins), else
    /// <see cref="Lora"/> as a one-element list, else empty. Not serialized.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<LoraOptions> EffectiveLoras => LoraSlots.Resolve(Loras, Lora);

    /// <summary>Optional model id (Step 8-2); <c>null</c> = the default model.</summary>
    public string? ModelId { get; init; }
}
