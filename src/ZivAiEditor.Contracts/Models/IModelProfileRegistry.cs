namespace ZivAiEditor.Contracts.Models;

/// <summary>
/// Lookup for model resolution profiles. The App layer wires the concrete
/// registry; later a Python-backed provider can replace it behind this same
/// interface without touching callers (Step 6.5 seam).
/// </summary>
public interface IModelProfileRegistry
{
    ModelProfile? Get(string modelId);

    ModelProfile Default { get; }

    IReadOnlyList<ModelProfile> All { get; }
}
