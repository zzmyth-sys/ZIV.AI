using System.IO;

namespace ZivAiEditor.Agent;

/// <summary>
/// Pure project-naming rules (module-boundary migration step 3): the display name to persist is
/// an explicit / renamed name, else the root image's file name, else the unnamed default.
/// Extracted from the App so it is unit-testable and free of UI state.
/// </summary>
public static class ProjectNaming
{
    /// <summary>The default name when none is given and no root image supplies one (Step 9C.6-E).</summary>
    public const string Unnamed = "未命名";

    public static string EffectiveName(string? explicitName, string? rootImagePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitName)
            && !string.Equals(explicitName, Unnamed, System.StringComparison.Ordinal))
        {
            return explicitName;
        }

        if (!string.IsNullOrWhiteSpace(rootImagePath))
        {
            var name = Path.GetFileNameWithoutExtension(rootImagePath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return Unnamed;
    }
}
