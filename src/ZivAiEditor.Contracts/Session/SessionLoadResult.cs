using System.Collections.Generic;

namespace ZivAiEditor.Contracts.Session;

/// <summary>
/// Result of loading a project (Step 9C.6-E): the restored session, its display name, and any
/// non-fatal warnings (missing images / dropped crops). Module-boundary migration step 7-C:
/// promoted from the Agent's <c>SessionLoader</c> to a kernel value object so
/// <see cref="ISessionPersistence.LoadAsync"/> can be declared in <see cref="ZivAiEditor.Contracts"/>.
/// <see cref="Session"/> is the read-only <see cref="IEditSession"/> view (revising migration
/// step 2 Q4's "keep concrete" wording; see <c>FROZEN.md</c> step 7-C).
/// </summary>
public sealed class SessionLoadResult
{
    public IEditSession Session { get; init; } = null!;

    public string Name { get; init; } = "";

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
