using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Session;

/// <summary>
/// The minimal per-node snapshot needed to re-run an edit (Step 9C.8-A). Most of an
/// <see cref="EditPlan"/> is re-derivable: the prompt / tool / steps / denoise come
/// from re-parsing <see cref="IEditNode.Command"/>, and the source image / mask come
/// from the parent node. Only the two inputs the DAG cannot reconstruct are stored:
/// the UI-selected <see cref="Resolution"/> and the reference
/// <see cref="AdditionalImages"/>.
///
/// <para>Rerun semantics are "run again" (a fresh random seed), not bit-exact
/// reproduction — the parser never emits a seed.</para>
/// </summary>
public sealed class RerunSpec
{
    /// <summary>UI-selected output resolution; <c>null</c> = backend default.</summary>
    public ResolutionPolicy? Resolution { get; init; }

    /// <summary>Ordered reference images after the main image; empty = none.</summary>
    public IReadOnlyList<string> AdditionalImages { get; init; } = Array.Empty<string>();
}
