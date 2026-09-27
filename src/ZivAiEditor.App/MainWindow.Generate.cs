using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ZivAiEditor.App.Flows;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App;

/// <summary>
/// <c>/生成</c> (text-to-image) half of <see cref="MainWindow"/>: it intercepts the command and
/// delegates the rewrite → confirm → submit loop to the App's <see cref="FlowRunner"/>, keeping
/// the view effects (input / attachments / busy / scroll) here. Split out of the main file to
/// keep each file under the Z8 budget (module-boundary migration step 6).
/// </summary>
public partial class MainWindow
{
    /// <summary>Whether <paramref name="text"/> starts with a command whose <c>T2i</c> is set.</summary>
    private bool IsGenerateCommand(string text)
    {
        var name = CommandText.FirstToken(text);
        return name is { Length: > 0 }
            && _commands.Any(command =>
                command.T2i && string.Equals(command.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Delegates the generate flow to <see cref="FlowRunner.GenerateAsync"/> and handles the
    /// view effects: the confirm dialog, the pre-submit clear (attachments / input / busy), and
    /// the busy / scroll reset. A canceled confirmation leaves the input untouched.
    /// </summary>
    private async Task RunGenerateFlowAsync(string text, TextBox input)
    {
        var outcome = GenerateOutcome.Failed;
        var progress = new Progress<TaskProgress>(OnProgress);
        try
        {
            outcome = await RunWithPatienceAsync(() => _flow.GenerateAsync(
                text,
                progress,
                expanded => PromptConfirmDialog.ShowAsync(this, expanded),
                onConfirmed: () =>
                {
                    // /生成 is text-to-image: attachments are not used. Consume the strip (like a
                    // normal send) and tell the user, so it does not linger after the result.
                    if (_importBar is { Count: > 0 })
                    {
                        _vm.AddHint("文生图不使用附件，已忽略");
                        _importBar.Clear();
                    }

                    input.Text = "";
                    SetBusy(true);
                }));
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            outcome = GenerateOutcome.Failed;
        }
        finally
        {
            SetBusy(false);
            if (outcome != GenerateOutcome.Canceled)
            {
                ScrollToEnd();
            }
        }
    }
}
