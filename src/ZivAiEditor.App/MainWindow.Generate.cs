using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.App;

/// <summary>
/// <c>/生成</c> (text-to-image) half of <see cref="MainWindow"/>: it intercepts the command,
/// rewrites the description through the LLM, asks the user to confirm the expanded prompt,
/// then submits the result. Split out of the main file to keep each file under the Z8 budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>Whether <paramref name="text"/> starts with a command whose <c>T2i</c> is set.</summary>
    private bool IsGenerateCommand(string text)
    {
        var name = FirstToken(text);
        return name is { Length: > 0 }
            && _commands.Any(command =>
                command.T2i && string.Equals(command.Name, name, StringComparison.Ordinal));
    }

    /// <summary>Everything after the leading command token, trimmed.</summary>
    private static string ExtractGenerateDescription(string text)
    {
        var trimmed = (text ?? "").TrimStart();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? "" : trimmed[(space + 1)..].Trim();
    }

    private static string? FirstToken(string? text)
    {
        var parts = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    /// <summary>
    /// Runs the rewrite → confirm → submit loop for a <c>/生成</c> input. Returns as soon as the
    /// user cancels or an error is surfaced; on confirm it submits the expanded prompt with the
    /// original text as the chat bubble.
    /// </summary>
    private async Task RunGenerateFlowAsync(string text, TextBox input)
    {
        var description = ExtractGenerateDescription(text);
        if (description.Length == 0)
        {
            _vm.AddHint("/生成 需要一句描述");
            ScrollToEnd();
            return;
        }

        if (_promptExpander is null)
        {
            _vm.AddHint("扩写器未配置");
            ScrollToEnd();
            return;
        }

        if (_llmPreflight is not null)
        {
            var pre = await _llmPreflight.CheckAsync();
            if (pre.Status == LlmPreflightStatus.LlmUnreachable)
            {
                _vm.AddHint(pre.Message);
                ScrollToEnd();
                return;
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
                expanded = await _promptExpander.ExpandAsync(description);
            }
            catch (Exception ex)
            {
                _vm.AddHint($"扩写失败：{ex.Message}");
                ScrollToEnd();
                return;
            }

            if (string.IsNullOrWhiteSpace(expanded))
            {
                _vm.AddHint("扩写结果为空");
                ScrollToEnd();
                return;
            }

            var choice = await PromptConfirmDialog.ShowAsync(this, expanded);
            if (choice == PromptChoice.Cancel)
            {
                return;
            }

            if (choice == PromptChoice.Confirm)
            {
                break;
            }
        }

        // /生成 is text-to-image: attachments are not used. Consume the strip (like a normal
        // send) and tell the user, so it does not linger after the result.
        if (_importBar is { Count: > 0 })
        {
            _vm.AddHint("文生图不使用附件，已忽略");
            _importBar.Clear();
        }

        input.Text = "";
        SetBusy(true);
        try
        {
            var progress = new Progress<TaskProgress>(OnProgress);
            await _vm.SubmitAsync(
                "/生成 " + expanded,
                progress,
                CancellationToken.None,
                additionalImages: null,
                displayText: text);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
            ScrollToEnd();
        }
    }
}