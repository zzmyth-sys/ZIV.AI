namespace ZivAiEditor.App.Flows;

/// <summary>
/// L1/L2 recovery surfacing (Step 9C.20): the App forwards the backend's
/// <c>StuckRecoveryTriggered</c> event here; the next failure / cancel bubble for
/// the in-flight task shows "生成失败，已重启后端" instead of the raw error.
/// </summary>
internal sealed partial class FlowRunner
{
    private bool _stuckRecoveryPending;

    /// <summary>App signal: the backend recovered (stuck watchdog / mechanism error) for the in-flight task.</summary>
    public void NotifyStuckRecovery() => _stuckRecoveryPending = true;

    /// <summary>Consumes the recovery flag and returns the bubble text to use.</summary>
    internal string TakeFailureText(string fallback)
    {
        if (_stuckRecoveryPending)
        {
            _stuckRecoveryPending = false;
            return "生成失败，已重启后端";
        }

        return fallback;
    }
}
