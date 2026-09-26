using System.Text.Json.Serialization;

namespace ZivAiEditor.UI;

/// <summary>
/// Startup request for the editor: either the parsed CLI arguments or the payload
/// handed over by a second instance through the single-instance pipe (Z27). Every
/// field is optional — an empty request starts an empty session.
///
/// Parsing is intentionally lenient: an unknown flag or a flag without a value is
/// ignored so the app still starts with a default session (Step 9A requirement;
/// Z28: no external dependency, no exit on bad input). The type has no Avalonia
/// dependency, so it can be unit-tested without a UI thread.
/// </summary>
public sealed class LaunchOptions
{
    /// <summary>Main image to edit; <c>null</c> starts an empty / T2I-first session.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Initial prompt to prefill the input box.</summary>
    public string? Prompt { get; init; }

    /// <summary>Optional mask path (CLI <c>--mask</c>; the mask UI landed in Step 9C.7).</summary>
    public string? MaskPath { get; init; }

    /// <summary>
    /// Quick-edit template id (CLI <c>--quick</c>, bridge §10): when set the request runs the
    /// headless / in-editor quick path instead of opening the editor.
    /// </summary>
    public string? QuickTemplateId { get; init; }

    /// <summary>Absolute output path for a quick edit (CLI <c>--output</c>, bridge §10).</summary>
    public string? OutputPath { get; init; }

    /// <summary>Absolute status file path (CLI <c>--notify</c>, bridge §9/§10).</summary>
    public string? NotifyPath { get; init; }

    /// <summary>Resolution tier text (CLI <c>--resolution</c>: <c>fast</c> / <c>balanced</c> / <c>high_quality</c>).</summary>
    public string? Resolution { get; init; }

    /// <summary>True when the request carries a quick-edit template id (bridge §10).</summary>
    public bool IsQuick => !string.IsNullOrEmpty(QuickTemplateId);

    /// <summary>True when no field carries a value.</summary>
    public bool IsEmpty
        => string.IsNullOrEmpty(ImagePath)
           && string.IsNullOrEmpty(Prompt)
           && string.IsNullOrEmpty(MaskPath)
           && string.IsNullOrEmpty(QuickTemplateId)
           && string.IsNullOrEmpty(OutputPath)
           && string.IsNullOrEmpty(NotifyPath)
           && string.IsNullOrEmpty(Resolution);

    /// <summary>
    /// Parses <c>--image &lt;path&gt;</c> / <c>--prompt &lt;text&gt;</c> /
    /// <c>--mask &lt;path&gt;</c> / <c>--quick &lt;id&gt;</c> / <c>--output &lt;abs&gt;</c> /
    /// <c>--notify &lt;abs&gt;</c> / <c>--resolution &lt;tier&gt;</c>. Hand-written and
    /// reflection-free (AOT-friendly); never throws.
    /// </summary>
    public static LaunchOptions Parse(string[]? args)
    {
        string? image = null;
        string? prompt = null;
        string? mask = null;
        string? quick = null;
        string? output = null;
        string? notify = null;
        string? resolution = null;

        if (args is { Length: > 0 })
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--image":
                        image = NextValue(args, ref i) ?? image;
                        break;
                    case "--prompt":
                        prompt = NextValue(args, ref i) ?? prompt;
                        break;
                    case "--mask":
                        mask = NextValue(args, ref i) ?? mask;
                        break;
                    case "--quick":
                        quick = NextValue(args, ref i) ?? quick;
                        break;
                    case "--output":
                        output = NextValue(args, ref i) ?? output;
                        break;
                    case "--notify":
                        notify = NextValue(args, ref i) ?? notify;
                        break;
                    case "--resolution":
                        resolution = NextValue(args, ref i) ?? resolution;
                        break;
                }
            }
        }

        return new LaunchOptions
        {
            ImagePath = image,
            Prompt = prompt,
            MaskPath = mask,
            QuickTemplateId = quick,
            OutputPath = output,
            NotifyPath = notify,
            Resolution = resolution,
        };
    }

    /// <summary>
    /// Consumes the token after a flag. A missing / empty value or another flag is
    /// treated as "no value" (the flag is dropped) rather than a parse failure.
    /// </summary>
    private static string? NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            return null;
        }

        var value = args[index + 1];
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        index++;
        return value;
    }
}

/// <summary>Source-generated JSON for the single-instance payload (AOT-friendly).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(LaunchOptions))]
public partial class LaunchOptionsJsonContext : JsonSerializerContext
{
}
