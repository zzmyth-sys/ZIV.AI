namespace ZivAiEditor.Contracts.Planning;

/// <summary>
/// One saved project as shown in the project list (Step 9C.6-E): its session id, display name
/// and creation time. Module-boundary migration step 3: promoted from the Agent's
/// <c>ProjectInfo</c> to a kernel value object so the App / UI read project metadata through
/// <see cref="ZivAiEditor.Contracts"/> instead of an Agent type.
/// </summary>
public sealed record ProjectSummary(string SessionId, string Name, DateTimeOffset CreatedAt);
