namespace ZivAiEditor.Contracts.Inference;

/// <summary>
/// Canonical <c>op</c> identifiers shared by <see cref="EditRequest"/> and the
/// IPC <c>submit</c> message (Step 7). C# code must use these constants instead
/// of hard-coding op strings; the values match <c>contracts/ipc-protocol.md</c>
/// §3.4.
/// </summary>
public static class EditOps
{
    /// <summary>Text-to-image: no source image, zero latent + text conditioning.</summary>
    public const string T2I = "t2i";

    /// <summary>Masked local edit, or reference-conditioned edit when no mask is given.</summary>
    public const string Inpaint = "inpaint";

    /// <summary>Canvas expansion: paste the source at an anchor, mask the new region.</summary>
    public const string Outpaint = "outpaint";
}
