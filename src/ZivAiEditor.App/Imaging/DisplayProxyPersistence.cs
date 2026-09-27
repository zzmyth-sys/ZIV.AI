using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ZivAiEditor.Contracts.Session;
using ZivAiEditor.Imaging;

namespace ZivAiEditor.App.Imaging;

/// <summary>
/// Project-save half of the 8K proxy scheme: writes a bounded display proxy beside each node
/// original (and crop result) that exceeds the display cap, using the same relative naming the
/// project save uses (<c>{nodeId}.png</c> → <c>{nodeId}_proxy.png</c>). On reopen
/// <see cref="DisplayImageLoader"/> finds that sibling and skips re-decoding the full-size image.
///
/// <para>App-owned (Agent cannot reference <c>ZivAiEditor.Imaging</c>); pure file IO, never throws,
/// so a proxy failure only costs a re-decode later. Moved out of <c>MainWindow</c> so it is
/// unit-testable without a window.</para>
/// </summary>
internal static class DisplayProxyPersistence
{
    /// <summary>
    /// Writes proxies for every node in <paramref name="nodes"/> into
    /// <paramref name="projectDirectory"/>. A source already within the cap is skipped.
    /// </summary>
    public static void WriteNodeProxies(
        IReadOnlyList<IEditNode> nodes,
        string projectDirectory,
        int maxSide = IDisplayImageLoader.MaxDisplaySide)
    {
        if (nodes is null || string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return;
        }

        try
        {
            foreach (var node in nodes)
            {
                if (node is null)
                {
                    continue;
                }

                var pack = node.ImagePaths.Count > 0 ? node.ImagePaths : new[] { node.ImagePath };
                for (var index = 0; index < pack.Count; index++)
                {
                    var name = index == 0 ? node.NodeId : $"{node.NodeId}_{index + 1}";
                    WriteIfNeeded(pack[index], Path.Combine(projectDirectory, name + "_proxy.png"), maxSide);
                }

                if (node.Crop is { ResultImagePath.Length: > 0 } crop)
                {
                    WriteIfNeeded(crop.ResultImagePath, Path.Combine(projectDirectory, node.NodeId + "_crop_proxy.png"), maxSide);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[proxy] project proxy save failed for '{projectDirectory}': {ex.Message}");
        }
    }

    private static void WriteIfNeeded(string source, string destination, int maxSide)
    {
        var (width, height) = ProxyImageCache.ReadPixelSize(source);
        if (width <= 0 || height <= 0 || Math.Max(width, height) <= maxSide)
        {
            return;
        }

        ProxyImageCache.SaveProjectProxy(source, maxSide, destination);
    }
}
