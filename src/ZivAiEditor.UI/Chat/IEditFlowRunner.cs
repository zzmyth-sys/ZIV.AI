using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// UI-side port for the edit flow orchestration (module-boundary migration step 6). The App's
/// <c>FlowRunner</c> implements it; <see cref="SessionViewModel"/> holds it and forwards
/// <c>SubmitAsync</c> / <c>RerunNodeAsync</c> / <c>CancelCurrent</c> to it. Defined in the UI
/// assembly so the App can implement it without a reverse (UI → App) dependency.
///
/// <para>The signatures are verbatim those of the corresponding <see cref="SessionViewModel"/>
/// members, so the view model's wrappers stay thin and existing callers are unaffected.</para>
/// </summary>
public interface IEditFlowRunner
{
    Task<bool> SubmitAsync(
        string input,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyList<string>? additionalImages = null,
        string? displayText = null);

    Task<bool> RerunNodeAsync(
        string nodeId,
        IProgress<TaskProgress>? progress = null,
        CancellationToken ct = default);

    bool CancelCurrent();
}
