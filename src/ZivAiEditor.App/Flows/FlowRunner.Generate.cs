using System;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.App.Flows;

/// <summary>Outcome of <see cref="FlowRunner.GenerateAsync"/> so the App can shape the view.</summary>
internal enum GenerateOutcome
{
    /// <summary>The user cancelled the expanded-prompt confirmation; nothing was submitted.</summary>
    Canceled,

    /// <summary>A validation / preflight / rewrite failure happened; nothing was submitted.</summary>
    Failed,

    /// <summary>The expanded prompt was submitted (the result flows through the normal submit path).</summary>
    Submitted,
}

/// <summary>
/// Generate-T2I flow (module-boundary migration step 6): preflight → expand → confirm (loop) →
/// submit. Moved out of <c>MainWindow.RunGenerateFlowAsync</c>; the confirm dialog and the
/// pre-submit view effects (clear attachments / input / busy) are injected by the App, so the
/// runner stays view-free.
/// </summary>
internal sealed partial class FlowRunner
{
    /// <summary>
    /// Runs the rewrite → confirm → submit loop for a <c>/生成</c> input. The
    /// <paramref name="confirmPrompt"/> callback shows the expanded prompt and returns the user's
    /// choice; <paramref name="onConfirmed"/> performs the App's view effects right before the
    /// final submit (clear attachments / input, set busy). UI hints go through the view model.
    /// </summary>
    public async Task<GenerateOutcome> GenerateAsync(
        string text,
        IProgress<TaskProgress>? progress,
        Func<string, Task<PromptChoice>> confirmPrompt,
        Action? onConfirmed = null,
        CancellationToken ct = default)
    {
        var description = ExtractGenerateDescription(text);
        if (description.Length == 0)
        {
            _vm.AddHint("/生成 需要一句描述");
            return GenerateOutcome.Failed;
        }

        if (_promptExpander is null)
        {
            _vm.AddHint("扩写器未配置");
            return GenerateOutcome.Failed;
        }

        if (_llmPreflight is not null)
        {
            var pre = await _llmPreflight.CheckAsync(ct);
            if (pre.Status == LlmPreflightStatus.LlmUnreachable)
            {
                _vm.AddHint(pre.Message);
                return GenerateOutcome.Failed;
            }

            if (pre.Status == LlmPreflightStatus.LowVram)
            {
                _vm.AddHint(pre.Message);
            }
        }

        string expanded;
        while (true)
        {
            try
            {
                expanded = await _promptExpander.ExpandAsync(description, ct);
            }
            catch (Exception ex)
            {
                _vm.AddHint($"扩写失败：{ex.Message}");
                return GenerateOutcome.Failed;
            }

            if (string.IsNullOrWhiteSpace(expanded))
            {
                _vm.AddHint("扩写结果为空");
                return GenerateOutcome.Failed;
            }

            var choice = await confirmPrompt(expanded);
            if (choice == PromptChoice.Cancel)
            {
                return GenerateOutcome.Canceled;
            }

            if (choice == PromptChoice.Confirm)
            {
                break;
            }
        }

        // /生成 is text-to-image: attachments are not used. The App's onConfirmed clears the
        // strip (and tells the user), clears the input and sets busy — right before submit.
        onConfirmed?.Invoke();

        await SubmitAsync("/生成 " + expanded, progress, ct, additionalImages: null, displayText: text);
        return GenerateOutcome.Submitted;
    }

    /// <summary>Everything after the leading command token, trimmed.</summary>
    private static string ExtractGenerateDescription(string text)
    {
        var trimmed = (text ?? "").TrimStart();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? "" : trimmed[(space + 1)..].Trim();
    }
}
