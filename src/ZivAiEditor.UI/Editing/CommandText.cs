namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Small text helpers shared by the App command/chat layer (extracted to remove the verbatim
/// duplicate between <c>CommandRequirements</c> and <c>MainWindow.Generate</c>).
/// </summary>
public static class CommandText
{
    /// <summary>
    /// The first whitespace-delimited token of <paramref name="text"/>, or <c>null</c> when the
    /// text is empty / whitespace-only. A leading <c>/</c> is <b>not</b> stripped.
    /// </summary>
    public static string? FirstToken(string? text)
    {
        var parts = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }
}
