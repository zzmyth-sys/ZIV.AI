using System;
using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.App;

/// <summary>
/// T5/S1: pure, UI-free context availability for one command — whether it can run against the
/// current session state, and a user-facing reason when it cannot. The <c>/</c> suggestion list
/// (S4) uses it to grey out unavailable commands; <see cref="CommandRequirements"/> reuses it as
/// the single source of truth for the send pre-gate. No Avalonia, no session type: callers pass
/// the few facts it needs.
/// </summary>
public static class CommandAvailability
{
    /// <summary>The session facts a command can be gated on (unknown image count is not modeled).</summary>
    public readonly record struct Context(bool HasImage, int ImageCount, bool HasOutpaintCrop);

    /// <summary>Command name whose crop-tool outpaint crop is required (P1 relocation, name-keyed).</summary>
    private const string OutpaintCommandName = "/扩图";

    /// <summary>
    /// Evaluates the command against <paramref name="ctx"/>. Axes are checked in order; the first
    /// failure returns <c>available:false</c> with its reason. All pass → <c>(true, null)</c>.
    /// </summary>
    public static (bool available, string? reason) Evaluate(CommandDefinition def, Context ctx)
    {
        ArgumentNullException.ThrowIfNull(def);

        // a. T2I takes no input image (T3.1 behavior: an input image is rejected).
        if (def.EffectiveHandler == CommandHandler.T2I)
        {
            return ctx.HasImage
                ? (false, "文生图命令不接受输入图")
                : (true, null);
        }

        // b. Every other command edits an existing image.
        if (!ctx.HasImage)
        {
            return (false, "需要图片");
        }

        // c. A multi-only command (variants without a "single" key, e.g. /合照) needs 2+ images.
        if (def.Variants is { Count: > 0 } && !def.Variants.ContainsKey("single") && ctx.ImageCount < 2)
        {
            return (false, "需要至少 2 张图");
        }

        // d. /扩图 follows a crop-tool outpaint: the current node must carry the outpaint crop.
        if (string.Equals(def.Name, OutpaintCommandName, StringComparison.Ordinal) && !ctx.HasOutpaintCrop)
        {
            return (false, "需先做裁切外扩");
        }

        return (true, null);
    }
}
