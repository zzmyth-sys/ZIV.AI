using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using ZivAiEditor.App.Controls;

namespace ZivAiEditor.App;

/// <summary>
/// Main-window control initialisation (split out of MainWindow.axaml.cs to keep each file under
/// Z8's 600-line limit): the send button, the chat input, the history list and the title-bar
/// buttons (sidebar toggle / settings entry) plus the resolution picker.
/// </summary>
public partial class MainWindow
{
    private void InitChat()
    {
        if (this.FindControl<Button>("PART_BtnSend") is { } send)
        {
            send.Click += (_, _) => _ = SubmitAsync();
        }

        if (FindInput() is { } input)
        {
            // Multi-line input: Enter sends, Shift+Enter inserts a newline. Intercept on
            // the TUNNEL phase, because with AcceptsReturn the TextBox's own class handler
            // consumes Enter (inserting a newline) before the bubbling KeyDown reaches us.
            input.AddHandler(
                InputElement.KeyDownEvent,
                (_, e) =>
                {
                    // T5/S4: while the / suggestion list is open it owns ↑↓ / Tab / Esc / Enter.
                    if (HandleCommandListKey(e))
                    {
                        return;
                    }

                    if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        e.Handled = true;
                        _ = SubmitAsync();
                    }
                },
                RoutingStrategies.Tunnel);
        }

        if (this.FindControl<ListBox>("PART_HistoryList") is { } history)
        {
            history.SelectionChanged += OnHistorySelectionChanged;
        }

        // Title-bar sidebar button toggles the history pane (Step 9C.3 UI pass).
        // Marked "User" so the OS treats it as client content inside the caption
        // (otherwise the title-bar hit-test swallows the click).
        if (this.FindControl<Button>("PART_BtnToggleSidebar") is { } toggleSidebar
            && this.FindControl<Border>("PART_HistoryPane") is { } historyPane)
        {
            WindowDecorationProperties.SetElementRole(toggleSidebar, WindowDecorationsElementRole.User);
            toggleSidebar.Click += (_, _) => historyPane.IsVisible = !historyPane.IsVisible;
        }

        // Settings entry (Step 9C.15): "User" role so the caption hit-test does not swallow
        // the click; the dialog is modal with Owner = this window.
        if (this.FindControl<Button>("PART_BtnSettings") is { } settingsButton)
        {
            WindowDecorationProperties.SetElementRole(settingsButton, WindowDecorationsElementRole.User);
            settingsButton.Click += async (_, _) => await new SettingsWindow(_shell).ShowDialog(this);
        }

        // Resolution tier picker (Step 6.5 logic, first UI): selection feeds the plan.
        if (this.FindControl<ResolutionPicker>("PART_ResolutionPicker") is { } picker)
        {
            picker.Attach(_modelProfiles.Default);
            picker.SelectionChanged += (_, _) => ApplyResolution(picker);
            ApplyResolution(picker);
        }
    }
}
