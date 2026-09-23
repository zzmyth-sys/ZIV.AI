using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace ZivAiEditor.App;

/// <summary>
/// Minimal self-drawn single-line text prompt (Step 9C.6-E), used to rename a project.
/// Enter confirms, Esc / the cancel button returns <c>null</c>.
/// </summary>
public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        InitializeComponent();
    }

    private TextPromptDialog(string title, string initial)
        : this()
    {
        if (this.FindControl<TextBlock>("PART_Message") is { } message)
        {
            message.Text = title;
        }

        var input = this.FindControl<TextBox>("PART_Input");
        if (input is not null)
        {
            input.Text = initial;
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Close(input.Text);
                }
            };
        }

        if (this.FindControl<Button>("PART_Cancel") is { } cancel)
        {
            cancel.Click += (_, _) => Close(null);
        }

        if (this.FindControl<Button>("PART_Ok") is { } ok)
        {
            ok.Click += (_, _) => Close(input?.Text);
        }

        Opened += (_, _) =>
        {
            input?.Focus();
            input?.SelectAll();
        };
    }

    /// <summary>Shows the prompt modally; returns the entered text, or <c>null</c> on cancel.</summary>
    public static Task<string?> ShowAsync(Window owner, string title, string initial)
        => new TextPromptDialog(title, initial).ShowDialog<string?>(owner);
}
