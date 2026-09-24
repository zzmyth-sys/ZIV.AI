using System;
using System.Collections.Generic;

namespace ZivAiEditor.Agent;

/// <summary>
/// Image-pack members of <see cref="EditSession"/> (Step 9C.10): the ordered pipeline
/// images a node's edit consumed. Split out of <c>EditSession.cs</c> to keep each file
/// within the Z8 budget.
/// </summary>
public sealed partial class EditSession
{
    /// <summary>
    /// Sets the session root image <b>pack</b> (Step 9C.10): the existing DAG is dropped
    /// (as in <see cref="ResetToRoot"/>) and a fresh root node is created whose
    /// <see cref="EditNode.ImagePaths"/> is the normalized pack (<see cref="EditNode.ImagePath"/>
    /// is <c>imagePaths[0]</c>). Blank entries are dropped; an empty / all-blank list is a no-op.
    /// </summary>
    public void SetRoot(IReadOnlyList<string> imagePaths)
    {
        var paths = NormalizeImages(imagePaths, null);
        if (paths.Count == 0)
        {
            return;
        }

        Nodes.Clear();

        _rootNode = new EditNode
        {
            ParentNodeId = null,
            ImagePath = paths[0],
            ImagePaths = paths,
            Command = RootCommand,
        };

        Nodes[_rootNode.NodeId] = _rootNode;
        CurrentNodeId = _rootNode.NodeId;
    }

    /// <summary>
    /// Sets the ordered pipeline images a node's edit consumed (Step 9C.10). The node is
    /// rebuilt in place with the same identity / parent / image(s) / command / timestamp /
    /// crop / mask / re-run snapshot; only <see cref="EditNode.UsedImagePaths"/> changes.
    /// Blank entries are dropped. A no-op when <paramref name="nodeId"/> is unknown.
    /// </summary>
    public void SetNodeUsedImages(string nodeId, IReadOnlyList<string> imagePaths)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var updated = new EditNode
        {
            NodeId = node.NodeId,
            ParentNodeId = node.ParentNodeId,
            ImagePath = node.ImagePath,
            ImagePaths = node.ImagePaths,
            UsedImagePaths = NormalizeImages(imagePaths, null),
            Command = node.Command,
            CreatedAt = node.CreatedAt,
            Crop = node.Crop,
            Mask = node.Mask,
            Rerun = node.Rerun,
        };

        Nodes[nodeId] = updated;
        if (ReferenceEquals(_rootNode, node))
        {
            _rootNode = updated;
        }
    }

    /// <summary>
    /// Normalizes an image list (Step 9C.10): the supplied list wins when it has any
    /// non-blank entry (blanks dropped), otherwise the legacy single <paramref name="fallback"/>
    /// image is wrapped. Returns an empty list when both are blank.
    /// </summary>
    private static IReadOnlyList<string> NormalizeImages(IReadOnlyList<string>? images, string? fallback)
    {
        if (images is { Count: > 0 })
        {
            var kept = new List<string>(images.Count);
            foreach (var image in images)
            {
                if (!string.IsNullOrWhiteSpace(image))
                {
                    kept.Add(image);
                }
            }

            if (kept.Count > 0)
            {
                return kept;
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? Array.Empty<string>() : new[] { fallback };
    }
}
