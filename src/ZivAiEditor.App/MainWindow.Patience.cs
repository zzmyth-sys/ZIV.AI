using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZivAiEditor.App;

/// <summary>
/// User-patience layer (U1/U2, Step 9C.20): while a generation runs, a 5-minute
/// timer may show a modal "continue waiting / end task" dialog. The dialog is a
/// modal shell only — the generation task itself keeps running (D4).
/// </summary>
public partial class MainWindow
{
    /// <summary>Delay before the patience dialog (U1); settable for tests, default 5 min.</summary>
    internal int UserPromptAfterMs { get; set; } = 300_000;

    /// <summary>App signal: the backend performed an L1/L2 recovery for the in-flight task.</summary>
    internal void NotifyStuckRecovery() => _flow.NotifyStuckRecovery();

    /// <summary>
    /// Runs <paramref name="run"/> while a patience watcher may ask the user to
    /// keep waiting. The watcher runs concurrently and is cancelled when the run
    /// finishes, so it never blocks (D4) or re-prompts (once per task).
    /// </summary>
    private async Task<T> RunWithPatienceAsync<T>(Func<Task<T>> run)
    {
        using var cts = new CancellationTokenSource();
        var watch = WatchPatienceAsync(cts.Token);
        try
        {
            return await run();
        }
        finally
        {
            cts.Cancel();
            try
            {
                await watch;
            }
            catch (Exception)
            {
                // The watcher is best-effort.
            }
        }
    }

    private async Task WatchPatienceAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(UserPromptAfterMs, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_vm.IsBusy)
        {
            return;
        }

        // Modal shell; the generation keeps running (D4). null (closed) counts as keep-waiting.
        var keepWaiting = await ConfirmDialog.ShowAsync(
            this, "任务已运行 5 分钟，是否继续等待？", "继续等待", "结束任务");
        if (keepWaiting == false)
        {
            _vm.CancelCurrent();
        }
    }
}
