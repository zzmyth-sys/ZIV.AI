using System;
using System.Threading.Tasks;
using ZivAiEditor.UI.Chat;

namespace ZivAiEditor.App;

/// <summary>
/// Delete half of <see cref="MainWindow"/>: the bubble × confirmation and its wiring to the
/// view model's delete flow. Split out of the main file to keep each file under the Z8 budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Confirms, then deletes the node (and its subtree) behind a completed AI bubble. Closes the
    /// preview window when it was showing a deleted node, and reports the outcome in the status.
    /// </summary>
    private async Task DeleteNodeAsync(string nodeId)
    {
        if (_vm is null || _vm.IsBusy)
        {
            return;
        }

        if (!await _shell.ConfirmAsync(this, "删除该节点及其后续编辑？此操作不可撤销。"))
        {
            return;
        }

        try
        {
            if (!await _vm.DeleteNodeAsync(nodeId))
            {
                return;
            }

            if (_imagePreview is { NodeId: { Length: > 0 } previewNode } preview
                && ChatFlowRules.FindNode(_vm.Session, previewNode) is null)
            {
                _imagePreview = null;
                preview.Close();
            }

            SetStatus("已删除节点");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }
}
