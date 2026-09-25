namespace ZivAiEditor.Contracts.Execution;

/// <summary>Outcome of <see cref="ICommandParser.ParseAsync"/>.</summary>
public sealed class ParseResult
{
    public bool Success { get; init; }

    public EditPlan? Plan { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The matched command name (e.g. "/换背景"), or <c>null</c> for a free-form prompt.</summary>
    public string? MatchedCommand { get; init; }

    /// <summary>
    /// T3.1: non-null when the command routes to a non-plan capability (currently
    /// <c>"tag"</c>) instead of an <see cref="EditPlan"/>. Execution is wired in T4; until then
    /// the result also carries an <see cref="ErrorMessage"/>.
    /// </summary>
    public string? Capability { get; init; }

    /// <summary>
    /// T3.1: non-fatal field-ownership warnings — a field that does not belong to the command's
    /// handler is ignored, never silently. Empty when clean.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
