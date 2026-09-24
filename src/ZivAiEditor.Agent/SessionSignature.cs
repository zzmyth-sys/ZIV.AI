using System.IO;
using System.Text;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

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

        return builder.ToString();
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
