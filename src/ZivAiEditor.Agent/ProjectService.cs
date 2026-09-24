using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// Project catalog (module-boundary migration step 3): owns the on-disk project listing —
/// enumerate / delete / rename / locate projects and remember the last opened one. It is the
/// project-domain counterpart of <see cref="SessionStore"/>: the store keeps the session
/// <b>content</b> format (<c>session.json</c> + node images, save / load / export), while this
/// service owns the <b>catalog</b> (the <c>sessions/</c> directory tree and
/// <c>last_project.txt</c>).
///
/// <para>Format-touching reads / writes stay in the store as primitives
/// (<see cref="SessionStore.ReadMetadataAsync"/> / <see cref="SessionStore.WriteMetadataNameAsync"/>),
/// so the session JSON schema has a single owner; this service only orchestrates the directory
/// scan. Pure BCL file IO — no platform / GPU dependency — so it is unit-testable.</para>
/// </summary>
public sealed class ProjectService
{
    private const string LastProjectFileName = "last_project.txt";

    private readonly SessionStore _store;

    public ProjectService(SessionStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The directory holding every project.</summary>
    public string RootDirectory => _store.RootDirectory;

    /// <summary>The directory of one project.</summary>
    public string GetDirectory(string sessionId) => Path.Combine(_store.RootDirectory, sessionId);

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
        => _store.WriteMetadataNameAsync(GetDirectory(sessionId), newName, ct);

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

            await File.WriteAllTextAsync(path, sessionId.Trim(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[project] write last-project failed: {ex.Message}");
        }
    }
}
