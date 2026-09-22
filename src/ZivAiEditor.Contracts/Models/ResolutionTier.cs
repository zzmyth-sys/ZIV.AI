namespace ZivAiEditor.Contracts.Models;

/// <summary>Named resolution presets offered to the user for a model.</summary>
public enum ResolutionTier
{
    Fast,
    Balanced,
    HighQuality,

    /// <summary>User-supplied size; not resolved from the profile's tier table.</summary>
    Custom
}
