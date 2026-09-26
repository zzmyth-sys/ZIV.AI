using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Chat;

namespace ZivAiEditor.App;

/// <summary>
/// Re-run half of <see cref="MainWindow"/> (Step 9C.8-A): a right-click "重跑" entry on
/// each re-runnable history node, driving <see cref="SessionViewModel.RerunNodeAsync"/>.
/// Split out of the main file to keep each file under the Z8 budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Adds a "重跑" context menu to a history node. Nodes with no parent (the source-image
    /// root, or a T2I-first node) have no source image and cannot be re-run, so no menu is
    /// attached.
    /// </summary>
    private void AttachRerunMenu(ListBoxItem item, HistoryItem history)
    {
        if (string.IsNullOrEmpty(history.Node.ParentNodeId))
        {
            return;
        }

        var rerun = new MenuItem { Header = "重跑" };
        rerun.Click += (_, _) => _ = RerunAsync(history.Node.NodeId);
        item.ContextMenu = new ContextMenu { Items = { rerun } };
    }

    /// <summary>
    /// Adds a "重跑" context menu to a chat result image (Step 9C.8-A). The image is
    /// resolved back to its session node; a root / T2I-first image (no parent) is not
    /// re-runnable, so no menu is attached.
    /// </summary>
    private void AttachRerunMenuToImage(Image image, string path)
    {
        var node = FindNodeByImagePath(path);
        if (node is null || string.IsNullOrEmpty(node.ParentNodeId))
        {
            return;
        }

        var rerun = new MenuItem { Header = "重跑" };
        rerun.Click += (_, _) => _ = RerunAsync(node.NodeId);
        image.ContextMenu = new ContextMenu { Items = { rerun } };
    }

    private async Task RerunAsync(string nodeId)
    {
        if (_vm is null || _vm.IsBusy || _busy)
        {
            return;
        }

        // Bridge §7.3 / P1: a re-run is an editor manual task, so it holds engine.lock too —
        // the viewer's probe and any headless quick process see the engine as busy.
        if (!TryAcquireEngineLock())
        {
            SetStatus("AI 引擎忙，请稍后再试");
            return;
        }

        SetBusy(true);

        // Step 9C.8-B: the view model owns the in-flight CTS and exposes CancelCurrent().
        var progress = new Progress<TaskProgress>(OnProgress);
        try
        {
            await RunWithPatienceAsync(() => _vm.RerunNodeAsync(nodeId, progress, CancellationToken.None));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            ReleaseEngineLock();
            SetBusy(false);
            ScrollToEnd();
        }
    }
}
