using System;
using System.Collections.Generic;
using System.IO;

namespace ZivAiEditor.App;

/// <summary>
/// Pure ComfyUI-root discovery + path derivation (plan B). No UI, no writes: every input is
/// injected so the whole chain is headless-testable. The effective <c>comfy_root</c> precedence
/// (env / settings.ini first) lives in <see cref="SettingsLoader"/>; this type resolves only the
/// filesystem-derived layers:
/// <list type="number">
///   <item>program-directory portable layout: <c>&lt;appDir&gt;/Comfyui/ComfyUI</c>;</item>
///   <item>Python-embeded sibling: <c>&lt;pythonExe&gt;/../ComfyUI</c>;</item>
///   <item>repository-relative (dev checkout): walk up from <c>&lt;appDir&gt;</c> for
///         <c>Comfyui/ComfyUI</c>, guarded by <c>DOC/FROZEN.md</c> so an unrelated ancestor is
///         never mistaken for the project root.</item>
/// </list>
/// </summary>
internal static class ComfyDiscovery
{
    /// <summary>Environment variable carrying the ComfyUI source tree to the Python backend.</summary>
    internal const string EnvName = "ZIV_AI_COMFY_ROOT";

    /// <summary>First existing candidate in the discovery chain, or <c>null</c> when none exists.</summary>
    internal static string? DiscoverComfyRoot(
        string? configured,
        string? appDirectory,
        string? pythonExe,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        var directoryOk = directoryExists ?? Directory.Exists;
        var fileOk = fileExists ?? File.Exists;
        foreach (var candidate in EnumerateCandidates(configured, appDirectory, pythonExe, directoryOk, fileOk))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && directoryOk(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>All discovery candidates, in precedence order (may contain a non-existing path).</summary>
    internal static IEnumerable<string?> EnumerateCandidates(
        string? configured,
        string? appDirectory,
        string? pythonExe,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        var directoryOk = directoryExists ?? Directory.Exists;
        var fileOk = fileExists ?? File.Exists;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        if (!string.IsNullOrWhiteSpace(appDirectory))
        {
            yield return Path.Combine(appDirectory!, "Comfyui", "ComfyUI");
        }

        if (!string.IsNullOrWhiteSpace(pythonExe))
        {
            var pythonDirectory = TryGetDirectoryName(pythonExe!);
            if (!string.IsNullOrEmpty(pythonDirectory))
            {
                yield return TryGetFullPath(Path.Combine(pythonDirectory!, "..", "ComfyUI"));
            }
        }

        // Repository-relative: walk up from the program directory. Trusting an ancestor only when
        // it also carries DOC/FROZEN.md mirrors SettingsLoader.FindTemplate (dev checkout guard).
        var anchor = appDirectory;
        while (!string.IsNullOrWhiteSpace(anchor))
        {
            if (fileOk(Path.Combine(anchor!, "DOC", "FROZEN.md")))
            {
                var candidate = Path.Combine(anchor!, "Comfyui", "ComfyUI");
                if (directoryOk(candidate))
                {
                    yield return candidate;
                }
            }

            var parent = TryGetDirectoryName(anchor!.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, anchor, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            anchor = parent;
        }
    }

    /// <summary>Model root derived from the ComfyUI tree (<c>&lt;root&gt;/models</c>); null when no root.</summary>
    internal static string? DeriveModelRoot(string? comfyRoot) => Join(comfyRoot, "models");

    /// <summary>LoRA directory (<c>&lt;root&gt;/models/loras</c>); null when no root.</summary>
    internal static string? DeriveLoraRoot(string? comfyRoot) => Join(comfyRoot, "models", "loras");

    /// <summary>
    /// Python interpreter default for a portable ComfyUI layout
    /// (<c>&lt;root&gt;/../python_embeded/python.exe</c>); null when no root.
    /// </summary>
    internal static string? DerivePythonExe(string? comfyRoot)
    {
        if (string.IsNullOrWhiteSpace(comfyRoot))
        {
            return null;
        }

        return TryGetFullPath(Path.Combine(comfyRoot!, "..", "python_embeded", "python.exe"));
    }

    /// <summary>The first-run wizard predicate: no ComfyUI root resolved yet.</summary>
    internal static bool ShouldPromptForComfyRoot(string? effectiveComfyRoot)
        => string.IsNullOrWhiteSpace(effectiveComfyRoot);

    private static string? Join(string? root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var path = root!;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return path;
    }

    private static string? TryGetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
