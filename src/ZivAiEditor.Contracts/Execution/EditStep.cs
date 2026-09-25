using System.Text.Json.Serialization;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Contracts.Execution;

public sealed class EditStep
{
    public string StepId { get; init; } = Guid.NewGuid().ToString("N");
    public int Order { get; init; }
    public string ToolName { get; init; } = "";
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Legacy single LoRA slot (Step 8-1; read-only compatibility from T3.2). New code sets
    /// <see cref="Loras"/>; kept so older plans still carry a LoRA.
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
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? ErrorMessage { get; set; }
}
