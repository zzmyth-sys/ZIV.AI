namespace ZivAiEditor.Contracts.Project;

/// <summary>
/// One saved project as shown in the project list (Step 9C.6-E): its session id, display name
/// and creation time. Module-boundary migration step 3: promoted from the Agent's
/// <c>ProjectInfo</c> to a kernel value object so the App / UI read project metadata through
/// <see cref="ZivAiEditor.Contracts"/> instead of an Agent type.
/// </summary>
public sealed record ProjectSummary(string SessionId, string Name, DateTimeOffset CreatedAt)
{
    /// <summary>
    /// The project's original source image (bridge D1, JSON <c>source_image</c>); <c>null</c> for
    /// legacy projects or T2I-first sessions. An init-only extra property so the positional
    /// record's primary constructor is unchanged.
    /// </summary>
    public string? SourceImage { get; init; }
}
