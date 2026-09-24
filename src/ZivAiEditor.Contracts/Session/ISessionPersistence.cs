using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Project;

namespace ZivAiEditor.Contracts.Session;

/// <summary>
/// Session-persistence port (module-boundary migration step 7-C): save / load / export a
/// session as a self-contained project, plus per-node artifact cleanup. Implemented by the
/// Agent's <c>SessionStore</c>; the App / UI depend only on this port rather than the concrete
/// store. <see cref="ExportToAsync"/> takes the read-only <see cref="IEditSession"/>; the store
/// keeps a concrete-<c>EditSession</c> overload forwarding to it.
/// </summary>
public interface ISessionPersistence
{
    /// <summary>
    /// Loads a project into a fresh session. Throws <c>ProjectFormatException</c> on an
    /// unsupported version and <c>ProjectCorruptException</c> on a missing / malformed file
    /// (the concrete exceptions are declared in the Agent's <c>SessionStore</c>).
    /// </summary>
    Task<SessionLoadResult> LoadAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Writes the session as a project (overwriting an existing one) and returns its metadata.
    /// </summary>
    Task<ProjectSummary> SaveAsync(IEditSession session, string name, CancellationToken ct = default);

    /// <summary>
    /// Writes the session to an arbitrary external directory (the future "save as"). Returns
    /// the directory, or <c>null</c> on any failure (never throws).
    /// </summary>
    Task<string?> ExportToAsync(
        IEditSession session,
        string name,
        string externalDirectory,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes one or more nodes' saved artifacts from a project (Step 9C.8-A2). Never throws.
    /// </summary>
    void DeleteNodeArtifacts(
        string sessionId,
        IReadOnlyCollection<string> nodeIds,
        bool includeReferences);
}
