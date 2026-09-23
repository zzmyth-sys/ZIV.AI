using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Planning;

public sealed class PlanRequest
{
    public string MainImagePath { get; init; } = "";
    public string? ReferenceImagePath { get; init; }

    /// <summary>Ordered reference images after the main image (Step 9C.5-D); empty = none.</summary>
    public IReadOnlyList<string> AdditionalImages { get; init; } = Array.Empty<string>();
    public MaskSpec? Mask { get; init; }
    public string Prompt { get; init; } = "";
    public IReadOnlyDictionary<string, string> Options { get; init; }
        = new Dictionary<string, string>();
}
