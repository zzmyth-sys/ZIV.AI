using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZivAiEditor.Contracts.Project;

/// <summary>
/// Project-domain port (module-boundary migration step 7-C): the on-disk project catalog —
/// enumerate / delete / rename / locate projects and remember the last opened one. Implemented
/// by the Agent's <c>ProjectService</c>; the App / UI depend only on this port rather than the
/// implementation. It mirrors the frozen <c>ProjectService</c> public surface verbatim.
/// </summary>
public interface IProjectService
{
    /// <summary>The directory holding every project.</summary>
    string RootDirectory { get; }

    /// <summary>The directory of one project.</summary>
    string GetDirectory(string sessionId);

    /// <summary>Scans the project root and returns the readable projects, newest first.</summary>
    Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Deletes one project directory. Never throws.</summary>
    Task DeleteAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Renames a saved project (rewrites <c>name</c> in its JSON). No-op when missing.</summary>
    Task RenameAsync(string sessionId, string newName, CancellationToken ct = default);

    /// <summary>The last opened project id, or <c>null</c> when none / unreadable.</summary>
    string? GetLastProjectId();

    /// <summary>Records (or clears, when blank) the last opened project id. Never throws.</summary>
    Task SetLastProjectIdAsync(string? sessionId, CancellationToken ct = default);
}
