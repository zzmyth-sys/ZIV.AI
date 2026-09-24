using System;
using System.Threading;

namespace ZivAiEditor.App.Flows;

/// <summary>
/// Cancel half of <see cref="FlowRunner"/> (module-boundary migration step 6): the in-flight
/// cancellation source and the idempotent <see cref="CancelCurrent"/>. Moved out of
/// <see cref="ZivAiEditor.UI.Chat.SessionViewModel"/>; the view model exposes only the
/// observable <c>LastRunCanceled</c> / <c>IsBusy</c> the App reads.
/// </summary>
internal sealed partial class FlowRunner
{
    /// <summary>
    /// The in-flight run's cancellation source (Step 9C.8-B), owned by the runner. <c>null</c>
    /// when idle. Created and cleared on the UI thread (single-threaded).
    /// </summary>
    private CancellationTokenSource? _inFlightCts;

    /// <summary>
    /// True once a cancel was requested for the current run; makes <see cref="CancelCurrent"/>
    /// idempotent (a second click returns <c>false</c>). Reset when a run starts. Never read by
    /// the OOM retry — the token is the single source of truth for actual interruption.
    /// </summary>
    private bool _cancelRequested;

    /// <summary>
    /// Requests cancellation of the current in-flight submit / re-run (Step 9C.8-B). Returns
    /// <c>false</c> when there is nothing to cancel, when a cancel was already requested for this
    /// run, or when the source was already disposed; never throws. The cancellation is forwarded
    /// to the backend through the run's token (Z11).
    /// </summary>
    public bool CancelCurrent()
    {
        if (_cancelRequested || _inFlightCts is not { } cts)
        {
            return false;
        }

        try
        {
            cts.Cancel();
        }
        catch (Exception)
        {
            // Never throw (R1): a disposed / faulting source just means there is nothing to cancel.
            return false;
        }

        _cancelRequested = true;
        return true;
    }
}
