using System.Threading.Tasks;
using Avalonia.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// How a newly attached image is used when the session already has a root (Step 9C.6-C).
/// </summary>
public enum MultiImageChoice
{
    /// <summary>Dismissed / cancelled — do not send. Default so closing the window cancels.</summary>
    Cancel = 0,

    /// <summary>Use the first attachment as a brand-new root, resetting the session DAG.</summary>
    NewSession = 1,

    /// <summary>Use the attachment as a reference image (deferred to Step 9C.5-D; disabled).</summary>
    Reference = 2,
}

/// <summary>
/// Three-way prompt shown when a new image is attached to a session that already has a
/// root (Step 9C.6-C): new session / reference image (disabled) / cancel.
/// </summary>
public partial class MultiImagePromptDialog : Window
{
    public MultiImagePromptDialog()
    {
        InitializeComponent();
    }

    private MultiImagePromptDialog(bool _)
        : this()
    {
        if (this.FindControl<Button>("PART_Cancel") is { } cancel)
        {
            cancel.Click += (_, _) => Close(MultiImageChoice.Cancel);
        }

        if (this.FindControl<Button>("PART_NewSession") is { } newSession)
        {
            newSession.Click += (_, _) => Close(MultiImageChoice.NewSession);
        }

        if (this.FindControl<Button>("PART_Reference") is { } reference)
        {
            ToolTip.SetTip(reference, "多图编辑暂未实现");
            ToolTip.SetShowOnDisabled(reference, true);
        }
    }

    /// <summary>Shows the dialog modally; closing the window is treated as cancel.</summary>
    public static Task<MultiImageChoice> ShowAsync(Window owner)
        => new MultiImagePromptDialog(true).ShowDialog<MultiImageChoice>(owner);
}