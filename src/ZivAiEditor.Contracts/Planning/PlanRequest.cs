using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Planning;

public sealed class PlanRequest
{
    public string MainImagePath { get; init; } = "";
    public string? ReferenceImagePath { get; init; }
    public MaskSpec? Mask { get; init; }
    public string Prompt { get; init; } = "";
    public IReadOnlyDictionary<string, string> Options { get; init; }
        = new Dictionary<string, string>();
}
