namespace ZivAiEditor.Contracts.Tools;

public sealed class ToolResult
{
    public string StepId { get; init; } = "";
    public bool Success { get; init; }
    public string? OutputImagePath { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
