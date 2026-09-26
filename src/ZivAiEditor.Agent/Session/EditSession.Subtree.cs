using System;
using System.Collections.Generic;
using System.Linq;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Agent.Session;

/// <summary>
/// Subtree mutation for <see cref="EditSession"/> (Step 9C.8-A2): removing a node's
/// descendants (<see cref="RemoveSubtree"/>) or a node together with its descendants
/// (<see cref="RemoveNodeAndSubtree"/>) in one pass. Split out of <c>EditSession.cs</c> to
/// keep each file within the Z8 budget.
/// </summary>
public sealed partial class EditSession
{
    /// <summary>
    /// Removes the whole subtree <b>below</b> <paramref name="nodeId"/> — every descendant,
    /// keeping the node itself — and returns the removed nodes (Step 9C.8-A2). A no-op that
    /// returns an empty list when <paramref name="nodeId"/> is unknown. The BFS is bounded
    /// by the node count (a cycle cannot arise from <see cref="AppendNode"/>; the
    /// <c>seen</c> set is purely defensive).
    /// </summary>
    public IReadOnlyList<IEditNode> RemoveSubtree(string nodeId)
        => RemoveRange(nodeId, includeStart: false);

    /// <summary>
    /// Removes <paramref name="nodeId"/> <b>and its whole subtree</b> (every descendant) and
    /// returns the removed nodes, including the start node. A no-op that returns an empty
    /// list when <paramref name="nodeId"/> is unknown. When the current node is removed it is
    /// re-pointed at <paramref name="nodeId"/>'s parent (or the root / <c>null</c> when the
    /// parent is gone, e.g. the root itself was removed).
    /// </summary>
    public IReadOnlyList<IEditNode> RemoveNodeAndSubtree(string nodeId)
        => RemoveRange(nodeId, includeStart: true);

    private IReadOnlyList<IEditNode> RemoveRange(string nodeId, bool includeStart)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var start))
        {
            return Array.Empty<IEditNode>();
        }

        var parentId = start.ParentNodeId;

        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in Nodes.Values)
        {
            var parent = node.ParentNodeId;
            if (string.IsNullOrEmpty(parent))
            {
                continue;
            }

            if (!children.TryGetValue(parent, out var list))
            {
                list = new List<string>();
                children[parent] = list;
            }

            list.Add(node.NodeId);
        }

        var removed = new List<IEditNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (includeStart)
        {
            queue.Enqueue(nodeId);
        }
        else
        {
            EnqueueChildren(nodeId);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !Nodes.TryGetValue(id, out var node))
            {
                continue;
            }

            removed.Add(node);
            EnqueueChildren(id);
        }

        foreach (var node in removed)
        {
            Nodes.Remove(node.NodeId);
        }

        if (_rootNode is not null && !Nodes.ContainsKey(_rootNode.NodeId))
        {
            _rootNode = Nodes.Values.FirstOrDefault(candidate => string.IsNullOrEmpty(candidate.ParentNodeId));
        }

        if (includeStart && CurrentNodeId is not null && !Nodes.ContainsKey(CurrentNodeId))
        {
            CurrentNodeId = string.IsNullOrEmpty(parentId)
                ? _rootNode?.NodeId
                : (Nodes.ContainsKey(parentId) ? parentId : null);
        }

        return removed;

        void EnqueueChildren(string parent)
        {
            if (children.TryGetValue(parent, out var list))
            {
                foreach (var childId in list)
                {
                    queue.Enqueue(childId);
                }
            }
        }
    }
}
