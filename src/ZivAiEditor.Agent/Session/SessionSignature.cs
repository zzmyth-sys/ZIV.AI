using System.IO;
using System.Text;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Agent.Session;

/// <summary>
/// Pure dirty-signature computation for a session (module-boundary migration step 3): a stable
/// string describing the current DAG — session id, current node, and every node's id / image /
/// crop rectangle / mask path + write stamp. The App compares it against the last saved / loaded
/// snapshot to decide whether to prompt on close. No UI / platform dependency, so it is
/// unit-testable.
/// </summary>
public static class SessionSignature
{
    public static string Compute(IEditSession session)
    {
        var builder = new StringBuilder();
        builder.Append(session.SessionId).Append('|').Append(session.CurrentNodeId).Append('|');
        foreach (var node in session.GetHistory())
        {
            builder.Append(node.NodeId).Append(':').Append(node.ImagePath).Append(':');
            if (node.Crop is { } crop)
            {
                // The crop result path is geometry-independent (per-node file), so the
                // rectangle must be part of the signature or a re-crop would look clean.
                builder.Append(crop.X).Append(',').Append(crop.Y).Append(',')
                       .Append(crop.Width).Append(',').Append(crop.Height).Append(',')
                       .Append(crop.ResultImagePath);
            }

            if (node.Mask is { } mask)
            {
                // Step 9C.7: a mask adds no node, so without this a mask-only edit would
                // look clean and the close prompt would not fire. The per-node file is
                // overwritten in place, so its write stamp + length stand in for content.
                builder.Append(mask.MaskImagePath).Append(',').Append(mask.Width).Append(',')
                       .Append(mask.Height);
                AppendFileStamp(builder, mask.MaskImagePath);
            }

            builder.Append(';');
        }

        // B13: the fields above are the legacy signature; the ones below are appended as a
        // SEPARATE tail block so the legacy portion stays a strict prefix (adding fields never
        // changes the bytes of the legacy part; a re-open is not falsely dirty). These cover the
        // node/session fields whose in-place edits previously did not mark the session dirty:
        // Command, ImagePaths, UsedImagePaths, Rerun, SourceImage. DurationMs is deliberately
        // excluded — it is a runtime metric, not user content (including it would mark a session
        // dirty right after a run).
        builder.Append("||src=").Append(session.SourceImage ?? "").Append('|');
        foreach (var node in session.GetHistory())
        {
            builder.Append(node.NodeId).Append(':').Append(node.Command).Append(':');
            AppendList(builder, node.ImagePaths);
            builder.Append(':');
            AppendList(builder, node.UsedImagePaths);
            builder.Append(':');
            if (node.Rerun is { } rerun)
            {
                // 'R' distinguishes a present-but-empty rerun snapshot from "no snapshot".
                builder.Append('R');
                AppendResolution(builder, rerun.Resolution);
                builder.Append(':');
                AppendList(builder, rerun.AdditionalImages);
            }

            builder.Append(';');
        }

        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            builder.Append(value).Append(',');
        }
    }

    /// <summary>
    /// Appends every <see cref="ResolutionPolicy"/> field (all 7), or <c>null</c> for an absent
    /// policy, so a change to any resolution dimension dirties the session.
    /// </summary>
    private static void AppendResolution(StringBuilder builder, ResolutionPolicy? policy)
    {
        if (policy is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append(policy.Mode).Append(',').Append(policy.Side).Append(',')
               .Append(policy.Area).Append(',').Append(policy.Scale).Append(',')
               .Append(policy.Width).Append(',').Append(policy.Height).Append(',')
               .Append(policy.MaxPixels);
    }

    /// <summary>
    /// Appends a file's last-write ticks + length as a cheap content proxy (Step 9C.7).
    /// A missing / unreadable file contributes nothing (treated as absent).
    /// </summary>
    private static void AppendFileStamp(StringBuilder builder, string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var info = new FileInfo(path);
            builder.Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(':').Append(info.Length);
        }
        catch (System.Exception)
        {
            // Unreadable stamp is not fatal — the mask path itself is already in the signature.
        }
    }
}
