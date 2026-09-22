using ZivAiEditor.Contracts.Tools;

namespace ZivAiEditor.Tools;

/// <summary>
/// Shared output-path resolution for the Qwen-Image-2.1 tools (Z24).
///
/// An explicit <c>output_path</c> parameter wins; otherwise a new file is derived
/// from the working directory so step N's output can feed step N+1 (Z24).
///
/// A requested path that resolves to the source image is rejected: the backend
/// would ignore it (Z24) and write a default path instead, so the returned path
/// would not match the file on disk and multi-step chaining would break. In that
/// case a step-scoped new file is used instead.
///
/// The result is never <see langword="null"/>: when there is no working directory
/// (e.g. t2i with no source image) a per-step file under the OS temp directory is
/// used, so the caller always gets a path that matches the file on disk (the
/// backend honors the requested <c>output_path</c>).
///
/// No logging here: the <c>ZivAiEditor.Tools</c> layer has no logging
/// infrastructure (kept dependency-free; see ARCHITECTURE.md §4).
/// </summary>
internal static class ToolOutputPath
{
    /// <summary>
    /// Resolve the output path for a tool invocation using the <c>output_path</c>
    /// parameter and <see cref="ToolInput.MainImagePath"/> /
    /// <see cref="ToolInput.WorkingDirectory"/>.
    /// </summary>
    public static string Resolve(IReadOnlyDictionary<string, string> parameters, ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(input);

        if (parameters.TryGetValue("output_path", out var requested)
            && !string.IsNullOrWhiteSpace(requested)
            && !PathsEqual(requested, input.MainImagePath))
        {
            return requested;
        }

        return Derive(input);
    }

    /// <summary>
    /// Step-scoped new file in the working directory; falls back to a per-step
    /// file under the OS temp directory when there is no working directory.
    /// </summary>
    private static string Derive(ToolInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.WorkingDirectory))
        {
            return Path.Combine(input.WorkingDirectory, $"{input.StepId}.png");
        }

        return Path.Combine(Path.GetTempPath(), "zivai", $"{input.StepId}.png");
    }

    /// <summary>
    /// Windows-aware path comparison: normalize with <see cref="Path.GetFullPath(string)"/>
    /// and compare case-insensitively (falling back to a raw case-insensitive
    /// compare when a path is malformed).
    /// </summary>
    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}