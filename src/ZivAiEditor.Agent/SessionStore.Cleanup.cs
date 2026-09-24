using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ZivAiEditor.Agent;

/// <summary>
/// Per-node artifact cleanup for a saved project (Step 9C.8-A2). Split out of
/// <c>SessionStore.cs</c> to keep each file within the Z8 budget.
/// </summary>
public sealed partial class SessionStore
{
    /// <summary>
    /// Deletes one or more nodes' saved artifacts from a project (Step 9C.8-A2): the node
    /// image <c>{nodeId}.png</c>, its crop <c>{nodeId}_crop.png</c> and mask
    /// <c>{nodeId}_mask.png</c>, and — when <paramref name="includeReferences"/> — the
    /// reference copies <c>refs/{nodeId}_ref*</c>. Used when a node is replaced or its
    /// subtree is deleted, so the project does not accumulate orphans.
    ///
    /// <para>Never throws: a missing project / file is a no-op and each delete is guarded
    /// individually (a locked file cannot abort the rest), mirroring the cleanup in
    /// <c>ImageCropper</c> / <c>MaskExporter</c>.</para>
    /// </summary>
    public void DeleteNodeArtifacts(
        string sessionId,
        IReadOnlyCollection<string> nodeIds,
        bool includeReferences)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || nodeIds is null || nodeIds.Count == 0)
        {
            return;
        }

        var directory = GetProjectDirectory(sessionId);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var nodeId in nodeIds)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                continue;
            }

            DeleteFileSafe(Path.Combine(directory, nodeId + ".png"));
            DeleteFileSafe(Path.Combine(directory, nodeId + "_crop.png"));
            DeleteFileSafe(Path.Combine(directory, nodeId + "_mask.png"));

            // Step 9C.10: extra pack images ({nodeId}_{n}.png, n >= 2) and any used/ copies.
            DeleteByPattern(directory, nodeId + "_*.png");
            DeleteByPattern(Path.Combine(directory, "used"), nodeId + "_*");

            if (includeReferences)
            {
                DeleteReferences(directory, nodeId);
            }
        }
    }

    /// <summary>
    /// Deletes every file matching <paramref name="pattern"/> in <paramref name="directory"/>
    /// (Step 9C.10). A missing directory is a no-op and each delete is guarded.
    /// </summary>
    private static void DeleteByPattern(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern))
            {
                DeleteFileSafe(file);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[session] pattern cleanup failed '{pattern}': {ex.Message}");
        }
    }

    private static void DeleteReferences(string projectDirectory, string nodeId)
    {
        var refsDirectory = Path.Combine(projectDirectory, "refs");
        if (!Directory.Exists(refsDirectory))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(refsDirectory, nodeId + "_ref*"))
            {
                DeleteFileSafe(file);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[session] ref cleanup failed '{nodeId}': {ex.Message}");
        }
    }

    private static void DeleteFileSafe(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[session] delete failed '{path}': {ex.Message}");
        }
    }
}
