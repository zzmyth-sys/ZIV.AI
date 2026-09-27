using System;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Editing;

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
        var command = Find(commands, text);
        if (command is null)
        {
            return false;
        }

        // S1: delegate the decision to the shared CommandAvailability, isolating the variant
        // axis — the image / outpaint axes are passed as wildcards (T2I: no image; otherwise an
        // image is present) so only the "multi-only, fewer than 2 images" case can block here.
        var context = new CommandAvailability.Context(
            HasImage: command.EffectiveHandler != CommandHandler.T2I,
            ImageCount: currentImageCount + attachmentCount,
            HasOutpaintCrop: true);
        if (CommandAvailability.Evaluate(command, context).available)
        {
            return false;
        }

        hint = $"命令 {command.Name} 需要至少 2 张图";
        return true;
    }

    /// <summary>
    /// P1 · <c>/扩图</c> relocation: returns <c>true</c> (with a user-facing <paramref name="hint"/>)
    /// when <paramref name="text"/> names <c>/扩图</c> but the current node has no crop-tool outpaint
    /// crop. The parser gate remains authoritative; this only pre-blocks the send.
    /// </summary>
    public static bool RequiresOutpaintCrop(
        IReadOnlyList<CommandDefinition> commands,
        string? text,
        bool hasOutpaintCrop,
        out string? hint)
    {
        hint = null;
        var command = Find(commands, text);
        if (command is null || !string.Equals(command.Name, "/扩图", StringComparison.Ordinal))
        {
            return false;
        }

        // S1: only the outpaint axis can block here; the other axes are wildcards.
        var context = new CommandAvailability.Context(
            HasImage: true,
            ImageCount: 1,
            HasOutpaintCrop: hasOutpaintCrop);
        if (CommandAvailability.Evaluate(command, context).available)
        {
            return false;
        }

        hint = "「/扩图」需先做裁切外扩";
        return true;
    }

    /// <summary>The command named by the first token of <paramref name="text"/>, or <c>null</c>.</summary>
    private static CommandDefinition? Find(IReadOnlyList<CommandDefinition> commands, string? text)
    {
        var name = CommandText.FirstToken(text);
        if (name is null || name.Length == 0 || name[0] != '/')
        {
            return null;
        }

        foreach (var candidate in commands)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }
}
