namespace ZivAiEditor.Contracts.Execution;

/// <summary>Outcome of <see cref="ICommandParser.ParseAsync"/>.</summary>
public sealed class ParseResult
{
    public bool Success { get; init; }

    public EditPlan? Plan { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The matched command name (e.g. "/换背景"), or <c>null</c> for a free-form prompt.</summary>
    public string? MatchedCommand { get; init; }
}
