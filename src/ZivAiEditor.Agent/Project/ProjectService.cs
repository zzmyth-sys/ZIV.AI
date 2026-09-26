using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Project;

namespace ZivAiEditor.Agent.Project;

/// <summary>
/// Project catalog (module-boundary migration step 3): owns the on-disk project listing —
/// enumerate / delete / rename / locate projects and remember the last opened one. It is the
/// project-domain counterpart of the session store: the store keeps the session
/// <b>content</b> format (<c>session.json</c> + node images, save / load / export), while this
/// service owns the <b>catalog</b> (the <c>sessions/</c> directory tree and
/// <c>last_project.txt</c>).
///
/// <para>Format-touching metadata reads / writes stay in the store as primitives
/// (<c>SessionStore.ReadMetadataAsync</c> / <c>SessionStore.WriteMetadataNameAsync</c>), exposed
/// to this service through the <see cref="IProjectMetadataStore"/> port so the session JSON schema
/// has a single owner; this service only orchestrates the directory scan. Pure BCL file IO — no
/// platform / GPU dependency — so it is unit-testable.</para>
/// </summary>
public sealed class ProjectService : IProjectService
{
    private const string LastProjectFileName = "last_project.txt";

    private readonly IProjectMetadataStore _store;

    public ProjectService(IProjectMetadataStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The directory holding every project.</summary>
    public string RootDirectory => _store.RootDirectory;

    /// <summary>
    /// The directory of one project. Defense-in-depth (A5): an unsafe id (a separator, a
    /// <c>..</c>, or one escaping the root) resolves to a structurally impossible directory
    /// so <c>Directory.Exists</c> is false and no caller can touch outside the root. The
    /// <c>string</c> signature is frozen (contract), so the rejection is expressed as a
    /// sentinel path rather than an exception.
    /// </summary>
    public string GetDirectory(string sessionId) => PathSanitizer.ResolveDirectory(sessionId, _store.RootDirectory);

    /// <summary>Scans the project root and returns the readable projects, newest first.</summary>
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<ProjectSummary>();
        if (!Directory.Exists(_store.RootDirectory))
        {
            return result;
        }

        foreach (var directory in Directory.GetDirectories(_store.RootDirectory))
        {
            ct.ThrowIfCancellationRequested();
            var info = await _store.ReadMetadataAsync(directory, ct).ConfigureAwait(false);
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result.OrderByDescending(project => project.CreatedAt).ToArray();
    }

    /// <summary>Deletes one project directory. Never throws.</summary>
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
        => Task.Run(
            () =>
            {
                try
                {
                    // A5: reject an unsafe id before resolving — never recursive-delete outside root.
                    if (!PathSanitizer.IsSafe(sessionId, _store.RootDirectory))
                    {
                        Debug.WriteLine("[project] delete rejected (unsafe id)");
                        return;
                    }

                    var directory = GetDirectory(sessionId);
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[project] delete failed '{sessionId}': {ex.Message}");
                }
            },
            ct);

    /// <summary>Renames a saved project (rewrites <c>name</c> in its JSON). No-op when missing.</summary>
    public Task RenameAsync(string sessionId, string newName, CancellationToken ct = default)
        => PathSanitizer.IsSafe(sessionId, _store.RootDirectory)
            ? _store.WriteMetadataNameAsync(GetDirectory(sessionId), newName, ct)
            : Task.CompletedTask;

    /// <summary>The last opened project id, or <c>null</c> when none / unreadable.</summary>
    public string? GetLastProjectId()
    {
        try
        {
            var path = Path.Combine(_store.RootDirectory, LastProjectFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();
            return value.Length == 0 ? null : value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[project] read last-project failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds the most recently created project whose <c>source_image</c> matches
    /// <paramref name="normalizedPath"/> (bridge §4.1-2): normalized + ordinal case-insensitive.
    /// Returns <c>null</c> when no project matches.
    /// </summary>
    public async Task<ProjectSummary?> FindBySourceImageAsync(
        string normalizedPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var projects = await ListAsync(ct).ConfigureAwait(false);
        ProjectSummary? match = null;
        foreach (var project in projects)
        {
            if (project.SourceImage is not { Length: > 0 } source)
            {
                continue;
            }

            if (PathNormalizer.AreSame(source, normalizedPath)
                && (match is null || project.CreatedAt > match.CreatedAt))
            {
                match = project;
            }
        }

        return match;
    }

    /// <summary>Records (or clears, when blank) the last opened project id. Never throws.</summary>
    public async Task SetLastProjectIdAsync(string? sessionId, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_store.RootDirectory);
            var path = Path.Combine(_store.RootDirectory, LastProjectFileName);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            // A5 hardening: never persist an id that could later resolve outside the root.
            if (!PathSanitizer.IsSafe(sessionId, _store.RootDirectory))
            {
                Debug.WriteLine("[project] write last-project rejected (unsafe id)");
                return;
            }

            await File.WriteAllTextAsync(path, sessionId.Trim(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[project] write last-project failed: {ex.Message}");
        }
    }
}
