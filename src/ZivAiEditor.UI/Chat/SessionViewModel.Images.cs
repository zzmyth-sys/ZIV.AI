using System;
using System.Collections.Generic;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Image-pack members of <see cref="SessionViewModel"/> (Step 9C.10-P2; module-boundary
/// migration step 6): the multi-image "原图" root and the current pack size used by the command
/// pre-gate. The pack/reference projections moved to <see cref="ChatFlowRules"/>.
/// </summary>
public sealed partial class SessionViewModel
{
    /// <summary>Hard cap on the root image pack (Step 9C.10, Q2=B).</summary>
    private const int MaxRootImages = 10;

    /// <summary>
    /// Sets the starting image <b>pack</b> and resets the session (Step 9C.10): every node and
    /// the current-node selection are dropped, because the DAG was built on the previous root.
    /// Blank entries are ignored and the pack is trimmed to <see cref="MaxRootImages"/> (a hint
    /// is shown when trimmed). An empty / all-blank list is a no-op.
    /// </summary>
    public void SetRootImage(IReadOnlyList<string>? imagePaths)
    {
        var paths = ChatFlowRules.NormalizeRootImages(imagePaths);
        if (paths.Count == 0)
        {
            return;
        }

        var trimmed = paths.Count > MaxRootImages;
        if (trimmed)
        {
            paths = paths.GetRange(0, MaxRootImages);
        }

        // Resetting the root drops the existing DAG, so its crop / mask temp files are orphans.
        _imaging?.CleanupSession(_session.SessionId);
        _writer.SetRoot(paths);
        RefreshHistory();
        RebuildContext();

        if (trimmed)
        {
            AddInfo($"最多支持 {MaxRootImages} 张原图，多余的已忽略");
        }
    }

    /// <summary>
    /// Sets a single starting image and resets the session (Step 9C.3). Delegates to the pack
    /// overload; a blank path is a no-op.
    /// </summary>
    public void SetRootImage(string? imagePath)
        => SetRootImage(string.IsNullOrWhiteSpace(imagePath) ? null : new[] { imagePath! });

    /// <summary>
    /// The current node's image-pack size (Step 9C.10); 0 when no node is current. Drives the
    /// command image requirement (<c>CommandRequirements</c>) and the send gating.
    /// </summary>
    public int CurrentImageCount
    {
        get
        {
            var path = _session.GetPathToCurrent();
            return path.Count > 0 ? path[^1].ImagePaths.Count : 0;
        }
    }

    /// <summary>
    /// Appends a non-error informational line to the chat stream (Step 9C.10, R2). Distinct
    /// from <see cref="AddHint"/>, which renders as an error-style hint.
    /// </summary>
    public void AddInfo(string text)
        => Messages.Add(new ChatMessage { Role = ChatRole.System, Text = text, IsError = false });
}
