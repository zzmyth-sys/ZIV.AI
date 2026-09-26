using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App;

/// <summary>
/// T5/S4: the <c>/</c> command suggestion list under <c>PART_Input</c>. When the input is a bare
/// prefix (starts with <c>/</c>, no whitespace yet) it filters the merged command set with the pure
/// <see cref="CommandSuggestions"/>, drops commands that cannot run against the current context
/// (<see cref="CommandAvailability"/>), orders the rest with the pure <see cref="CommandOrdering"/>
/// (<c>/扩图</c> pinned, then by usage count), and renders the survivors. The input keeps focus:
/// rows are non-focusable and all keys (↑↓ Enter Tab Esc) are handled in the input's tunnel handler
/// (<see cref="HandleCommandListKey"/>), which runs before the existing Enter-to-send handler.
/// Split out of <c>MainWindow.axaml.cs</c> to stay under the Z8 budget.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush SuggestionHighlightBrush = new SolidColorBrush(Color.Parse("#2E2E2E"));

    private readonly List<CommandDefinition> _suggestions = new();
    private readonly List<Border> _suggestionRows = new();

    private Popup? _commandPopup;
    private Border? _commandPopupHost;
    private StackPanel? _commandList;
    private TextBox? _commandInput;
    private CommandUsageStore? _usageStore;
    private IReadOnlyDictionary<string, int> _usageCounts = new Dictionary<string, int>();
    private int _suggestionIndex = -1;

    /// <summary>Set while committing a selection writes <c>PART_Input.Text</c> (avoids re-entry).</summary>
    private bool _suppressSuggestionRefresh;

    private void InitCommandList()
    {
        _commandPopup = this.FindControl<Popup>("PART_CommandPopup");
        _commandPopupHost = this.FindControl<Border>("PART_CommandPopupHost");
        _commandList = this.FindControl<StackPanel>("PART_CommandList");
        _commandInput = FindInput();

        // Usage counts live in the program directory (Z14); read once, mutated in place on Record.
        _usageStore = new CommandUsageStore(System.AppContext.BaseDirectory);
        _usageCounts = _usageStore.Load();

        if (_commandInput is { } input)
        {
            input.TextChanged += (_, _) =>
            {
                if (!_suppressSuggestionRefresh)
                {
                    RefreshSuggestions();
                }
            };
            input.LostFocus += (_, _) => HideSuggestions();
        }

        if (_commandPopup is { } popup)
        {
            if (_commandInput is { } target)
            {
                popup.PlacementTarget = target;
            }

            popup.IsLightDismissEnabled = false;
            popup.Closed += (_, _) => ResetSuggestionState();
        }
    }

    /// <summary>
    /// Tunnel key handler, called before the Enter-to-send handler. Consumes ↑↓ / Tab / Esc / Enter
    /// while the list is open; returns <c>false</c> (list closed, or an exact command name already
    /// typed) so the existing send path runs.
    /// </summary>
    private bool HandleCommandListKey(KeyEventArgs e)
    {
        if (_commandPopup?.IsOpen != true)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Down:
                e.Handled = true;
                MoveSelection(1);
                return true;
            case Key.Up:
                e.Handled = true;
                MoveSelection(-1);
                return true;
            case Key.Escape:
                e.Handled = true;
                HideSuggestions();
                return true;
            case Key.Tab:
                e.Handled = true;
                CommitSelection();
                return true;
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                // An exact command name is unambiguous: hand Enter back to send.
                var prefix = CommandPrefix(_commandInput?.Text);
                var exact = prefix is not null && _suggestions.Any(
                    definition => string.Equals(definition.Name, prefix, StringComparison.Ordinal));
                if (exact)
                {
                    HideSuggestions();
                    return false;
                }

                e.Handled = true;
                CommitSelection();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Recomputes the list for the current input; hides it when no longer a bare prefix.</summary>
    private void RefreshSuggestions()
    {
        var prefix = CommandPrefix(_commandInput?.Text);
        if (prefix is null)
        {
            HideSuggestions();
            return;
        }

        ShowSuggestions(prefix);
    }

    private void ShowSuggestions(string prefix)
    {
        if (_commandList is null || _commandPopup is null)
        {
            return;
        }

        // Filter by prefix, drop anything that cannot run now, then order (pin /扩图, usage desc).
        var context = CurrentAvailabilityContext();
        var available = CommandSuggestions
            .Filter(_commands, prefix)
            .Where(definition => CommandAvailability.Evaluate(definition, context).available)
            .ToList();

        _suggestions.Clear();
        _suggestions.AddRange(CommandOrdering.Order(available, _usageCounts));

        _suggestionRows.Clear();
        _commandList.Children.Clear();
        for (var i = 0; i < _suggestions.Count; i++)
        {
            _commandList.Children.Add(BuildSuggestionRow(i));
        }

        if (_suggestions.Count == 0)
        {
            HideSuggestions();
            return;
        }

        _suggestionIndex = 0;
        UpdateHighlight();

        // Match the input's width so the panel reads as an extension of the box.
        if (_commandPopupHost is { } host && _commandInput is { Bounds.Width: > 0 } input)
        {
            host.Width = input.Bounds.Width;
        }

        _commandPopup.IsOpen = true;
    }

    private Control BuildSuggestionRow(int index)
    {
        var definition = _suggestions[index];
        var captured = index;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = definition.Name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TextBrush,
        });

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            row.Children.Add(new TextBlock
            {
                Text = definition.Description,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SecondaryTextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var border = new Border
        {
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Focusable = false,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row,
        };
        border.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _suggestionIndex = captured;
            CommitSelection();
        };

        _suggestionRows.Add(border);
        return border;
    }

    private void MoveSelection(int delta)
    {
        var count = _suggestions.Count;
        if (count == 0)
        {
            return;
        }

        _suggestionIndex = _suggestionIndex < 0
            ? delta > 0 ? 0 : count - 1
            : ((_suggestionIndex + delta) % count + count) % count;
        UpdateHighlight();
    }

    private void CommitSelection()
    {
        if (_commandInput is not { } input
            || _suggestionIndex < 0
            || _suggestionIndex >= _suggestions.Count)
        {
            return;
        }

        var definition = _suggestions[_suggestionIndex];
        // Always leave the prefix segment with a trailing space, parameterless commands
        // included. Avalonia's TextBox.TextChanged is async (Dispatcher.Post), so the
        // suppressed assignment below fires RefreshSuggestions *after* this method returns;
        // a bare "/扩图" would still be a valid prefix and re-open the list, while "/扩图 "
        // makes CommandPrefix return null and keeps it closed. The parser trims, so the
        // sent text is unchanged.
        var insert = definition.Name + " ";

        _suppressSuggestionRefresh = true;
        try
        {
            input.Text = insert;
            input.CaretIndex = insert.Length;
        }
        finally
        {
            _suppressSuggestionRefresh = false;
        }

        HideSuggestions();
        input.Focus();

        // Usage counts feed the ordering on the next open (persisted; silent on failure).
        _usageStore?.Record(definition.Name);
    }

    private void UpdateHighlight()
    {
        for (var i = 0; i < _suggestionRows.Count; i++)
        {
            _suggestionRows[i].Background = i == _suggestionIndex
                ? SuggestionHighlightBrush
                : Brushes.Transparent;
        }
    }

    private void HideSuggestions()
    {
        if (_commandPopup is { IsOpen: true } popup)
        {
            popup.IsOpen = false;
        }

        ResetSuggestionState();
    }

    private void ResetSuggestionState()
    {
        _suggestions.Clear();
        _suggestionRows.Clear();
        _commandList?.Children.Clear();
        _suggestionIndex = -1;
    }

    private CommandAvailability.Context CurrentAvailabilityContext()
    {
        var attachments = _importBar?.Count ?? 0;
        var imageCount = _vm.CurrentImageCount + attachments;
        return new CommandAvailability.Context(
            HasImage: imageCount > 0,
            ImageCount: imageCount,
            HasOutpaintCrop: CurrentNodeHasOutpaintCrop());
    }

    /// <summary>
    /// The suggestion prefix: the whole input when it is a single token starting with <c>/</c>;
    /// <c>null</c> otherwise (empty, no leading slash, or already past the prefix segment).
    /// </summary>
    private static string? CommandPrefix(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '/')
        {
            return null;
        }

        for (var i = 1; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return null;
            }
        }

        return text;
    }
}
