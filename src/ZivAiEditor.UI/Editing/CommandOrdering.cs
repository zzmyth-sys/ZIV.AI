using System;
using System.Collections.Generic;
using System.Linq;
using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.UI.Editing;

/// <summary>
/// T5/S4-fix: pure ordering for the <c>/</c> suggestion list. Given the already-filtered
/// (prefix + availability) commands and a per-command usage-count map, it pins <c>/扩图</c>
/// to the front when present, then orders the rest by usage count (descending). Ties keep the
/// input order: <see cref="Enumerable.OrderByDescending{T,Key}"/> is a stable sort. No Avalonia,
/// so it is unit-testable.
/// </summary>
public static class CommandOrdering
{
    /// <summary>Name-keyed pin: the crop-tool outpaint follow-up (never handler / tool based).</summary>
    private const string OutpaintCommandName = "/扩图";

    public static IReadOnlyList<CommandDefinition> Order(
        IReadOnlyList<CommandDefinition> filtered,
        IReadOnlyDictionary<string, int> counts)
    {
        ArgumentNullException.ThrowIfNull(filtered);

        var ordered = filtered
            .OrderByDescending(command => CountOf(counts, command.Name))
            .ToList();

        var index = ordered.FindIndex(
            command => string.Equals(command.Name, OutpaintCommandName, StringComparison.Ordinal));
        if (index > 0)
        {
            var outpaint = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(0, outpaint);
        }

        return ordered;
    }

    private static int CountOf(IReadOnlyDictionary<string, int>? counts, string name)
        => counts is not null && counts.TryGetValue(name, out var count) ? count : 0;
}
