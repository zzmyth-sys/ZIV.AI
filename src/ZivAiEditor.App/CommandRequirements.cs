using System;
using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.App;

/// <summary>
/// Step 9C.9-A1 (D3): resolves whether a typed command needs more images than are currently
/// available, so the App can pre-block the send and show a hint before the parser rejects it.
/// Pure and UI-free, so it is unit-testable without Avalonia.
/// </summary>
internal static class CommandRequirements
{
    /// <summary>
    /// Returns <c>true</c> (with a user-facing <paramref name="hint"/>) when <paramref name="text"/>
    /// names a multi-only command (its variants lack a "single" template, e.g. <c>/合照</c>) but the
    /// effective image count (<paramref name="currentImageCount"/> + attachments) is below two.
    /// </summary>
    public static bool RequiresMoreImages(
        IReadOnlyList<CommandDefinition> commands,
        string? text,
        int currentImageCount,
        int attachmentCount,
        out string? hint)
    {
        hint = null;
        var name = FirstToken(text);
        if (name is null || name.Length == 0 || name[0] != '/')
        {
            return false;
        }

        CommandDefinition? command = null;
        foreach (var candidate in commands)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                command = candidate;
                break;
            }
        }

        if (command is null || command.T2i || command.Variants is not { Count: > 0 })
        {
            return false;
        }

        if (command.Variants.ContainsKey("single"))
        {
            // Both / single-only variants: the image count never blocks the send.
            return false;
        }

        // Mirrors the parser's effective count: the current node's image pack (its size, so a
        // multi-image root counts as N) plus the attachments.
        var effective = currentImageCount + attachmentCount;
        if (effective >= 2)
        {
            return false;
        }

        hint = $"命令 {command.Name} 需要至少 2 张图";
        return true;
    }

    private static string? FirstToken(string? text)
    {
        var parts = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }
}
