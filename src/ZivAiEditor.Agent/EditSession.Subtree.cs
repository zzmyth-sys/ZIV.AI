using System;
using System.Collections.Generic;
using System.Linq;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// Subtree mutation for <see cref="EditSession"/> (Step 9C.8-A2): removing a node's
/// descendants in one pass. Split out of <c>EditSession.cs</c> to keep each file within the
/// Z8 budget.
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
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.ContainsKey(nodeId))
        {
            return Array.Empty<IEditNode>();
        }

        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in Nodes.Values)
        {
            var parentId = node.ParentNodeId;
            if (string.IsNullOrEmpty(parentId))
            {
                continue;
            }

            if (!children.TryGetValue(parentId, out var list))
            {
                list = new List<string>();
                children[parentId] = list;
            }

            list.Add(node.NodeId);
        }

        var removed = new List<IEditNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        EnqueueChildren(nodeId);

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

        return removed;

        void EnqueueChildren(string parentId)
        {
            if (children.TryGetValue(parentId, out var list))
            {
                foreach (var childId in list)
                {
                    queue.Enqueue(childId);
                }
            }
        }
    }
}
