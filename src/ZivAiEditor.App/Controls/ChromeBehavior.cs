using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Media;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Shared wiring for the self-drawn chrome (Step 9C.2): decoration roles, title-bar
/// button clicks and the maximize-icon swap. Parts are read from the
/// <see cref="ChromeTitleBar"/> (a window's FindControl cannot cross a UserControl's
/// name scope). Called from each window's constructor after InitializeComponent.
/// </summary>
internal static class ChromeBehavior
{
    public static void Init(Window window, ChromeTitleBar chrome)
    {
        if (chrome.TitleBar is { } titleBar)
        {
            WindowDecorationProperties.SetElementRole(titleBar, WindowDecorationsElementRole.TitleBar);
        }

        SetRole(chrome.BtnMinimize, WindowDecorationsElementRole.MinimizeButton);
        SetRole(chrome.BtnMaximize, WindowDecorationsElementRole.MaximizeButton);
        SetRole(chrome.BtnClose, WindowDecorationsElementRole.CloseButton);

        if (chrome.BtnMinimize is { } minimize)
        {
            minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;
        }

        if (chrome.BtnMaximize is { } maximize)
        {
            maximize.Click += (_, _) => window.WindowState =
                window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        if (chrome.BtnClose is { } close)
        {
            close.Click += (_, _) => window.Close();
        }

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                UpdateMaximizeIcon(window, chrome);
            }
        };

        UpdateMaximizeIcon(window, chrome);
    }

    private static void UpdateMaximizeIcon(Window window, ChromeTitleBar chrome)
    {
        if (chrome.IconMaximize is not { } icon)
        {
            return;
        }

        icon.Data = Geometry.Parse(window.WindowState == WindowState.Maximized
            ? "M0 3H7V10H0Z M3 0H10V7H3Z"
            : "M0 0H10V10H0Z");
    }

    private static void SetRole(Button? button, WindowDecorationsElementRole role)
    {
        if (button is not null)
        {
            WindowDecorationProperties.SetElementRole(button, role);
        }
    }
}