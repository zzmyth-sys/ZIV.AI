using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ZivAiEditor.UI.Chat;

namespace ZivAiEditor.App.Flows;

/// <summary>
/// Delete flow (module-boundary migration step 6): remove a node and its whole subtree from the
/// DAG, clean up its temp / project artifacts and refresh the history / context. The confirmation
/// dialog stays the view's responsibility (MainWindow), so the runner keeps no shell / view
/// reference.
/// </summary>
internal sealed partial class FlowRunner
{
    /// <summary>
    /// Deletes <paramref name="nodeId"/> together with its descendants. Returns <c>false</c>
    /// without mutating anything when the runner is busy, the node is unknown, or it is a root
    /// node (no parent). The file cleanup runs off the UI thread and each step is guarded so a
    /// locked file cannot abort the flow. User-directory output images are never touched.
    /// </summary>
    public async Task<bool> DeleteNodeAsync(string nodeId)
    {
        if (_vm.IsBusy)
        {
            _vm.AddHint("生成中，无法删除");
            return false;
        }

        var node = ChatFlowRules.FindNode(_session, nodeId);
        if (node is null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(node.ParentNodeId))
        {
            return false;
        }

        var removed = _writer.RemoveNodeAndSubtree(nodeId);
        if (removed.Count == 0)
        {
            return false;
        }

        var ids = removed.Select(n => n.NodeId).ToArray();
        var sessionId = _session.SessionId;
        var cleaner = _nodeArtifactsCleaner;
        var imaging = _imaging;

        await Task.Run(() =>
        {
            try
            {
                imaging?.CleanupNode(sessionId, ids);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[delete] imaging cleanup failed: {ex.Message}");
            }

            try
            {
                cleaner?.Invoke(sessionId, ids, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[delete] project artifact cleanup failed: {ex.Message}");
            }
        });

        _vm.RefreshHistory();
        _vm.RebuildContext();
        return true;
    }
}
