using System.Threading.Tasks;
using Avalonia.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Minimal self-drawn information dialog (Avalonia has no built-in message box): a single
/// "确定" button. Used by the settings window for the save success / failure result.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private MessageDialog(string message)
        : this()
    {
        if (this.FindControl<TextBlock>("PART_Message") is { } text)
        {
            text.Text = message;
        }

        if (this.FindControl<Button>("PART_Ok") is { } ok)
        {
            ok.Click += (_, _) => Close();
        }
    }

    /// <summary>Shows the dialog modally until the user acknowledges it.</summary>
    public static Task ShowAsync(Window owner, string message)
        => new MessageDialog(message).ShowDialog(owner);
}
