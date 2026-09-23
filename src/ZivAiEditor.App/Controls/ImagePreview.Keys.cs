using System;
using Avalonia.Input;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Keyboard half of <see cref="ImagePreview"/> (Step 9C.6-E): Esc (crop → compare → close)
/// and Ctrl+Shift+S (save as). Split out of the main file to keep it under the Z8 budget.
/// </summary>
public partial class ImagePreview
{
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            SaveRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;

        // Esc exits crop mode first, then compare mode; a further Esc closes the window.
        if (IsCropActive)
        {
            ExitCropMode();
            return;
        }

        // Esc exits compare mode first (single image); a second Esc closes the window.
        if (_compareState.IsCompareMode)
        {
            _compareState.SetCompareMode(false);
            return;
        }

        Close();
    }
}
