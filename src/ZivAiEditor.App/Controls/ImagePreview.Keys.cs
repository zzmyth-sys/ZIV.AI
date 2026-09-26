using System;
using Avalonia.Input;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Keyboard half of <see cref="ImagePreview"/> (Step 9C.6-E / B3): Esc (crop → compare →
/// close), Ctrl+Shift+S (save as) and Space (hold to pan in any mode). Split out of the
/// main file to keep it under the Z8 budget.
/// </summary>
public partial class ImagePreview
{
    /// <summary>True while Space is held: left-drag then pans instead of using the active tool.</summary>
    private bool _spacePan;

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

        // B3: Space turns the left button into a pan for as long as it is held, in every
        // tool mode (including crop). Tracked here; Pointer.cs consumes the flag.
        if (e.Key == Key.Space)
        {
            _spacePan = true;
            e.Handled = true;
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

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePan = false;
            e.Handled = true;
        }
    }
}
