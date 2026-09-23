namespace ZivAiEditor.UI.Projects;

/// <summary>One row in the project list (Step 9C.6-E): a saved project and whether it is
/// the one currently open. Pure data (no Avalonia dependency), rendered by the App layer.</summary>
public sealed class ProjectListItem
{
    public string SessionId { get; init; } = "";

    public string Name { get; init; } = "";

    public bool IsCurrent { get; init; }
}
