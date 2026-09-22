using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Planning;

public sealed class EditPlan
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString("N");
    public string SourcePrompt { get; init; } = "";
    public string MainImagePath { get; init; } = "";
    public string? ReferenceImagePath { get; init; }
    public MaskSpec? Mask { get; init; }
    public IReadOnlyList<EditStep> Steps { get; init; } = Array.Empty<EditStep>();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Optional output resolution (Step 6.5); null = backend default.</summary>
    public ResolutionPolicy? Resolution { get; init; }
}
