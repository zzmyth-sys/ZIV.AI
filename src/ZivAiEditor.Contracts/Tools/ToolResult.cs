namespace ZivAiEditor.Contracts.Tools;

public sealed class ToolResult
{
    public string StepId { get; init; } = "";
    public bool Success { get; init; }
    public string? OutputImagePath { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Wall-clock of one IPC submission from the tool's perspective — it includes IPC
    /// round-trip, lazy model load and queue wait. Distinct from the backend's
    /// sampling-only <c>InferenceResultDetail.DurationMs</c> and the UI's end-to-end
    /// click-to-bubble stopwatch (Step 9C.3-R #2). The three are not interchangeable
    /// and are not cross-checked.
    /// </summary>
    public TimeSpan Duration { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
