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
    /// Optional LoRA for this step (Step 8-1), copied from the command definition and passed
    /// through <c>ToolInput.Lora</c> to <c>EditRequest.Lora</c>. <c>null</c> = no LoRA.
    /// </summary>
    public LoraOptions? Lora { get; init; }
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? ErrorMessage { get; set; }
}
