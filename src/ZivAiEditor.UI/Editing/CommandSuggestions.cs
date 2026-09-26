using System;
using System.Collections.Generic;
using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.UI.Editing;

/// <summary>
/// T5/S1: pure, Avalonia-free prefix filter for the <c>/</c> command suggestion list (S4 wires
/// the Popup). Matching is ordinal and case-sensitive (command names are exact, e.g. <c>/换背景</c>);
/// the source order is preserved so the list never reshuffles between keystrokes.
/// </summary>
public static class CommandSuggestions
{
    /// <summary>
    /// Commands whose name starts with <paramref name="prefix"/>. An empty prefix or a lone
    /// <c>"/"</c> returns every command; a non-<c>/</c> prefix matches nothing (there is no
    /// bare-word command).
    /// </summary>
    public static IReadOnlyList<CommandDefinition> Filter(
        IReadOnlyList<CommandDefinition> commands,
        string prefix)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (prefix.Length == 0 || prefix == "/")
        {
            return commands;
        }

        var matches = new List<CommandDefinition>();
        foreach (var command in commands)
        {
            if (command.Name.StartsWith(prefix, StringComparison.Ordinal))
            {
                matches.Add(command);
            }
        }

        return matches;
    }
}
