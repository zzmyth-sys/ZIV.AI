using System.Threading;
using System.Threading.Tasks;

namespace ZivAiEditor.Contracts.Project;

/// <summary>
/// Project-metadata port (Step 8-4 · Z-006 closure): the session-format primitives the project
/// catalog needs — the project root and reading / rewriting a project's <c>name</c> metadata.
/// Declared by the <b>project</b> domain (it needs them) and implemented by the session domain's
/// <c>SessionStore</c>.
///
/// <para>Deliberately excludes the session <b>content</b> read / write (load / save / export /
/// node-artifact cleanup), which stays on <see cref="ZivAiEditor.Contracts.Session.ISessionPersistence"/>.
/// This removes the project domain's concrete dependency on <c>SessionStore</c>.</para>
/// </summary>
public interface IProjectMetadataStore
{
    /// <summary>The directory holding every project.</summary>
    string RootDirectory { get; }

    /// <summary>
    /// Reads a saved project's list metadata, or <c>null</c> for a missing file / unsupported
    /// version / unreadable JSON (never throws).
    /// </summary>
    Task<ProjectSummary?> ReadMetadataAsync(string directory, CancellationToken ct = default);

    /// <summary>
    /// Rewrites the <c>name</c> metadata of one saved project; a no-op when the file is missing /
    /// unreadable (never throws).
    /// </summary>
    Task WriteMetadataNameAsync(string directory, string name, CancellationToken ct = default);
}
