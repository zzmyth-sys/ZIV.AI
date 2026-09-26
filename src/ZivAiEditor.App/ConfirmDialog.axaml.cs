using System.Threading.Tasks;
using Avalonia.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Minimal self-drawn confirmation dialog (Avalonia has no built-in message box).
/// Used by the close flow to ask whether to export the session (INTERACTION.md §4).
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private ConfirmDialog(string message)
        : this()
    {
        if (this.FindControl<TextBlock>("PART_Message") is { } text)
        {
            text.Text = message;
        }

        if (this.FindControl<Button>("PART_Yes") is { } yes)
        {
            yes.Click += (_, _) => Close(true);
        }

        if (this.FindControl<Button>("PART_No") is { } no)
        {
            no.Click += (_, _) => Close(false);
        }
    }

    /// <summary>Shows the dialog modally; returns <c>true</c> for "yes", <c>false</c> for "no".</summary>
    public static Task<bool?> ShowAsync(Window owner, string message)
        => new ConfirmDialog(message).ShowDialog<bool?>(owner);

    private ConfirmDialog(string message, string yesText, string noText)
        : this(message)
    {
        if (this.FindControl<Button>("PART_Yes") is { } yes)
        {
            yes.Content = yesText;
        }

        if (this.FindControl<Button>("PART_No") is { } no)
        {
            no.Content = noText;
        }
    }

    /// <summary>
    /// Shows the dialog modally with custom button labels; <c>true</c> for the
    /// first (yes) button, <c>false</c> for the second (no) button.
    /// </summary>
    public static Task<bool?> ShowAsync(Window owner, string message, string yesText, string noText)
        => new ConfirmDialog(message, yesText, noText).ShowDialog<bool?>(owner);
}
