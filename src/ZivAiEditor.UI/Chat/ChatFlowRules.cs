using System;
using System.Collections.Generic;
using System.IO;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.UI.Editing;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Pure rules / projections shared by <see cref="SessionViewModel"/> and the App's edit-flow
/// orchestration (module-boundary migration step 6). No state and no Avalonia dependency, so
/// both the view model (fallback path) and the <c>FlowRunner</c> use the <b>same</b> logic —
/// no duplicate implementations, so the two paths cannot diverge.
/// </summary>
public static class ChatFlowRules
{
    /// <summary>
    /// Whether the send button should be enabled (Step 9C.6-C). Blank text is never sendable;
    /// with attachments the mode must match the count (Single &lt;= 1, Multi &gt;= 2). With no
    /// attachments the mode does not gate the send (natural-language T2I stays available).
    /// </summary>
    public static bool CanSend(string? input, int attachmentCount, ImageEditMode mode)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (attachmentCount == 0)
        {
            return true;
        }

        return mode == ImageEditMode.Single ? attachmentCount <= 1 : attachmentCount >= 2;
    }

    /// <summary>Whether the DAG node can be re-run (it has a parent, so it has a source image).</summary>
    public static bool CanRerun(IEditSession session, string nodeId)
    {
        var node = FindNode(session, nodeId);
        return node is not null && !string.IsNullOrEmpty(node.ParentNodeId);
    }

    /// <summary>
    /// Assembles the pipeline references (Step 9C.10): the root pack's extras first, then the
    /// attachment references, truncated to <paramref name="max"/> (the tail is dropped).
    /// <paramref name="truncated"/> reports whether anything was dropped.
    /// </summary>
    public static IReadOnlyList<string> AssembleReferences(
        IReadOnlyList<string> packExtras,
        IReadOnlyList<string> attachmentRefs,
        int max,
        out bool truncated)
    {
        var all = new List<string>(packExtras.Count + attachmentRefs.Count);
        all.AddRange(packExtras);
        all.AddRange(attachmentRefs);

        truncated = all.Count > max;
        if (truncated)
        {
            all.RemoveRange(max, all.Count - max);
        }

        return all;
    }

    /// <summary>
    /// The display pack a node shows in the chat (Step 9C.10): the pipeline main
    /// (crop-aware) first, then the root's extra images. A single-image node yields one entry.
    /// </summary>
    public static IReadOnlyList<string> BuildDisplayPack(IEditNode node)
    {
        var pack = node.ImagePaths;
        var display = new List<string>(pack.Count) { PipelinePath(node) };
        for (var i = 1; i < pack.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(pack[i]))
            {
                display.Add(pack[i]);
            }
        }

        return display;
    }

    /// <summary>Drops blank root images and keeps order; a <c>null</c> / empty list yields empty.</summary>
    public static List<string> NormalizeRootImages(IReadOnlyList<string>? imagePaths)
    {
        var kept = new List<string>();
        if (imagePaths is null)
        {
            return kept;
        }

        foreach (var path in imagePaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                kept.Add(path);
            }
        }

        return kept;
    }

    /// <summary>Whether a task succeeded with an output image.</summary>
    public static bool IsSuccess(TaskState state)
        => state.Status == TaskStatus.Succeeded && !string.IsNullOrWhiteSpace(state.OutputImagePath);

    /// <summary>
    /// True when a failed task looks like a CUDA out-of-memory error (Step 9C.6-D). The backend
    /// surfaces <c>AcceleratorError: CUDA error: out of memory</c> / torch's
    /// <c>CUDA out of memory</c> as the task error message.
    /// </summary>
    public static bool IsOutOfMemory(TaskState state)
    {
        var message = state.ErrorMessage ?? "";
        return message.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
               || message.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Human-readable failure text (Step 9C.8-B): a canceled task is always reported as
    /// "已取消。" regardless of the internal error code the executor writes ("canceled").
    /// </summary>
    public static string BuildFailureMessage(TaskState state)
        => state.Status == TaskStatus.Canceled
            ? "已取消。"
            : state.ErrorMessage ?? $"执行未成功（{state.Status}）。";

    /// <summary>
    /// The ordered pipeline images an edit consumed (Step 9C.10): the main image (when present)
    /// followed by the reference images, mirroring the pipeline's <c>image1</c>..<c>imageN</c>.
    /// Blanks are dropped.
    /// </summary>
    public static IReadOnlyList<string> BuildUsedImages(EditPlan plan)
    {
        var images = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.MainImagePath))
        {
            images.Add(plan.MainImagePath);
        }

        foreach (var path in plan.AdditionalImages)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                images.Add(path);
            }
        }

        return images;
    }

    /// <summary>
    /// Drops blank entries from the UI-supplied reference images and keeps their order
    /// (Step 9C.5-D). A <c>null</c> / empty list yields an empty list.
    /// </summary>
    public static IReadOnlyList<string> NormalizeAdditionalImages(IReadOnlyList<string>? additionalImages)
    {
        if (additionalImages is null || additionalImages.Count == 0)
        {
            return Array.Empty<string>();
        }

        var images = new List<string>(additionalImages.Count);
        foreach (var path in additionalImages)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                images.Add(path);
            }
        }

        return images;
    }

    /// <summary>
    /// Rebuilds <paramref name="plan"/> with <see cref="EditPlan.AdditionalImages"/> set
    /// (Step 9C.5-D). <see cref="EditPlan"/> is init-only, so the plan is copied rather than
    /// mutated; the parser interface is untouched.
    /// </summary>
    public static EditPlan WithAdditionalImages(EditPlan plan, IReadOnlyList<string> additionalImages)
        => new()
        {
            PlanId = plan.PlanId,
            SourcePrompt = plan.SourcePrompt,
            MainImagePath = plan.MainImagePath,
            ReferenceImagePath = plan.ReferenceImagePath,
            AdditionalImages = additionalImages,
            Mask = plan.Mask,
            Steps = plan.Steps,
            CreatedAt = plan.CreatedAt,
            Resolution = plan.Resolution,
        };

    /// <summary>
    /// Builds the node's re-run snapshot (Step 9C.8-A), or <c>null</c> when the edit carried
    /// neither a UI resolution nor reference images (so <c>session.json</c> stays clean).
    /// </summary>
    public static RerunSpec? BuildRerunSpec(ResolutionPolicy? resolution, IReadOnlyList<string>? additionalImages)
    {
        if (resolution is null && (additionalImages is null || additionalImages.Count == 0))
        {
            return null;
        }

        return new RerunSpec
        {
            Resolution = resolution,
            AdditionalImages = additionalImages ?? Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Deletes one app-owned artifact file, never throwing. The session's source image is never
    /// deleted (Z24), even defensively.
    /// </summary>
    public static void DeleteArtifact(IEditSession session, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (string.Equals(path, session.RootImagePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A locked / missing file must not abort the re-run.
        }
    }

    /// <summary>
    /// The assistant bubble rendered for <paramref name="nodeId"/>, or <c>null</c> when none
    /// (Step 9C.8-A3). Used by a re-run to update that bubble in place.
    /// </summary>
    public static ChatMessage? FindAssistantMessage(IEnumerable<ChatMessage> messages, string nodeId)
    {
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.Assistant
                && string.Equals(message.NodeId, nodeId, StringComparison.Ordinal))
            {
                return message;
            }
        }

        return null;
    }

    public static IEditNode? FindNode(IEditSession session, string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return null;
        }

        foreach (var node in session.GetHistory())
        {
            if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>The current node's pack (the root's imported images); empty when no node.</summary>
    public static IReadOnlyList<string> CurrentPack(IEditSession session)
        => CurrentNode(session)?.ImagePaths ?? Array.Empty<string>();

    /// <summary>
    /// The current node's pack images beyond the main (<c>ImagePaths[1..]</c>): the references a
    /// multi-image root contributes. Empty for a single-image node / an edit node.
    /// </summary>
    public static IReadOnlyList<string> CurrentPackExtras(IEditSession session)
    {
        var pack = CurrentPack(session);
        if (pack.Count <= 1)
        {
            return Array.Empty<string>();
        }

        var extras = new List<string>(pack.Count - 1);
        for (var i = 1; i < pack.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(pack[i]))
            {
                extras.Add(pack[i]);
            }
        }

        return extras;
    }

    /// <summary>The image a node shows in the chat: its crop result, else its output.</summary>
    public static string PipelinePath(IEditNode node)
        => node.Crop is { ResultImagePath.Length: > 0 } crop ? crop.ResultImagePath : node.ImagePath;

    /// <summary>The current node (last on the root → current path), or <c>null</c> when empty.</summary>
    private static IEditNode? CurrentNode(IEditSession session)
    {
        var path = session.GetPathToCurrent();
        return path.Count > 0 ? path[^1] : null;
    }
}
