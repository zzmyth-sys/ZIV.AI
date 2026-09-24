using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace ZivAiEditor.App;

/// <summary>
/// What to do with an expanded <c>/生成</c> prompt (prompt-rewriter flow).
/// </summary>
public enum PromptChoice
{
    /// <summary>Dismissed / cancelled — abort the send. Default so closing the window cancels.</summary>
    Cancel = 0,

    /// <summary>Send the expanded prompt as-is.</summary>
    Confirm = 1,

    /// <summary>Re-run the rewriter and show a new prompt.</summary>
    Rewrite = 2,
}

/// <summary>
/// Shows an expanded text prompt for review before a text-to-image send (prompt-rewriter
/// flow): the user can cancel, ask for another rewrite, or confirm. Esc / closing cancels.
/// </summary>
public partial class PromptConfirmDialog : Window
{
    public PromptConfirmDialog()
    {
        InitializeComponent();
    }

    private PromptConfirmDialog(string prompt)
        : this()
    {
        if (this.FindControl<TextBox>("PART_Prompt") is { } text)
        {
            text.Text = prompt;
        }

        if (this.FindControl<Button>("PART_Cancel") is { } cancel)
        {
            cancel.Click += (_, _) => Close(PromptChoice.Cancel);
        }

        if (this.FindControl<Button>("PART_Rewrite") is { } rewrite)
        {
            rewrite.Click += (_, _) => Close(PromptChoice.Rewrite);
        }

        if (this.FindControl<Button>("PART_Confirm") is { } confirm)
        {
            confirm.Click += (_, _) => Close(PromptChoice.Confirm);
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(PromptChoice.Cancel);
            }
        };
    }

    /// <summary>Shows the prompt modally; closing the window is treated as cancel.</summary>
    public static Task<PromptChoice> ShowAsync(Window owner, string prompt)
        => new PromptConfirmDialog(prompt).ShowDialog<PromptChoice>(owner);
}