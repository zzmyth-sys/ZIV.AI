using System.Text.Json;
using System.Text.Json.Serialization;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// Parses a user chat input into a single-step <see cref="EditPlan"/> without any
/// LLM (INTERACTION.md §2). An input that starts with "/" selects a command from
/// <c>Template/commands.json</c>; anything else is taken verbatim as the prompt.
///
/// The parser only produces plans — it never calls the Executor and never touches
/// the backend (Step 8 scope). It is deterministic and offline, so it can be used
/// as the first layer before the LLM-based rewriting lands.
/// </summary>
public interface ICommandParser
{
    /// <summary>
    /// Parses <paramref name="input"/> into an <see cref="EditPlan"/> using the
    /// current working image from <paramref name="session"/> as the source.
    /// </summary>
    Task<ParseResult> ParseAsync(string input, IEditSession session, CancellationToken ct = default);

    /// <summary>
    /// Parses <paramref name="input"/> as <see cref="ParseAsync(string, IEditSession, CancellationToken)"/>
    /// and, when the produced plan carries no resolution of its own, stamps
    /// <paramref name="resolution"/> onto <see cref="EditPlan.Resolution"/> (V3: moved
    /// down from the UI, which used to rebuild the whole plan). The UI-selected
    /// resolution therefore never overrides a resolution the parser already produced
    /// (e.g. the explicit <c>/扩图</c> width / height). A <c>null</c>
    /// <paramref name="resolution"/> leaves the plan unchanged.
    /// </summary>
    Task<ParseResult> ParseAsync(
        string input,
        IEditSession session,
        ResolutionPolicy? resolution,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="ParseAsync(string, IEditSession, ResolutionPolicy?, CancellationToken)"/>
    /// but also supplies the number of images in the pipeline (main + references), used to pick a
    /// command's single / multi template variant. Pass <c>-1</c> when the count is unknown (variant
    /// selection then falls back to the command's <c>defaultVariant</c>).
    /// </summary>
    Task<ParseResult> ParseAsync(
        string input,
        IEditSession session,
        int imageCount,
        ResolutionPolicy? resolution,
        CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="ICommandParser.ParseAsync"/>.</summary>
public sealed class ParseResult
{
    public bool Success { get; init; }

    public EditPlan? Plan { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The matched command name (e.g. "/换背景"), or <c>null</c> for a free-form prompt.</summary>
    public string? MatchedCommand { get; init; }
}

/// <summary>One command entry from <c>Template/commands.json</c>.</summary>
public sealed class CommandDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("params")]
    public List<string> Params { get; init; } = new();

    [JsonPropertyName("tool")]
    public string Tool { get; init; } = "";

    [JsonPropertyName("template")]
    public string Template { get; init; } = "";

    /// <summary>
    /// Image-count dependent templates keyed by variant name ("single" / "multi"). Empty for
    /// commands that only carry a flat <see cref="Template"/>.
    /// </summary>
    [JsonPropertyName("variants")]
    public Dictionary<string, string> Variants { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Variant used when the image count is unknown (<c>imageCount &lt; 0</c>).</summary>
    [JsonPropertyName("defaultVariant")]
    public string DefaultVariant { get; init; } = "";

    /// <summary>Whether trailing arguments are folded into the last parameter (multi-word input).</summary>
    [JsonPropertyName("variadic")]
    public bool Variadic { get; init; }

    /// <summary>Text-to-image: the plan never carries a main image, regardless of the session.</summary>
    [JsonPropertyName("t2i")]
    public bool T2i { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}

internal sealed class CommandsFileDto
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("commands")]
    public List<CommandDefinition>? Commands { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CommandsFileDto))]
internal partial class CommandJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Regex-free, deterministic <see cref="ICommandParser"/>. Commands are loaded
/// once from <c>Template/commands.json</c>; if the file is missing or malformed a
/// built-in default set is used, so the app works with no external files (Z28).
///
/// Command matching is ordinal (case-sensitive). Parameter substitution is a
/// plain <c>{name}</c> string replace — no type conversion. The only structural
/// extra is for <c>/扩图</c>: the <c>QW21outpaint</c> tool requires an explicit
/// resolution (FROZEN 7.5), so <c>width</c>/<c>height</c> arguments are also
/// translated into an <see cref="ResolutionPolicy"/>.
/// </summary>
public sealed class CommandParser : ICommandParser
{
    /// <summary>Default commands file, resolved relative to the process working directory.</summary>
    public const string DefaultCommandsPath = "Template/commands.json";

    private const string OutpaintToolName = "QW21outpaint";

    /// <summary>Sentinel image count used when the caller does not know the pipeline size.</summary>
    private const int UnknownImageCount = -1;

    private readonly IReadOnlyList<CommandDefinition> _commands;

    public CommandParser(string commandsJsonPath = DefaultCommandsPath)
    {
        _commands = LoadCommands(commandsJsonPath);
    }

    /// <summary>The loaded command set (file or built-in default).</summary>
    public IReadOnlyList<CommandDefinition> Commands => _commands;

    public Task<ParseResult> ParseAsync(string input, IEditSession session, CancellationToken ct = default)
        => ParseAsync(input, session, resolution: null, ct);

    public Task<ParseResult> ParseAsync(
        string input,
        IEditSession session,
        ResolutionPolicy? resolution,
        CancellationToken ct = default)
        => ParseAsync(input, session, UnknownImageCount, resolution, ct);

    public Task<ParseResult> ParseAsync(
        string input,
        IEditSession session,
        int imageCount,
        ResolutionPolicy? resolution,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ct.ThrowIfCancellationRequested();

        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            return Task.FromResult(Error("Input is empty; enter a command or a prompt."));
        }

        var result = text.StartsWith('/')
            ? ParseSlashCommand(text, session, imageCount)
            : ParseNaturalLanguage(text, session);

        return Task.FromResult(ApplyResolution(result, resolution));
    }

    /// <summary>
    /// Stamps the UI-selected <paramref name="resolution"/> onto a plan that does not
    /// carry one of its own (V3). The parser-produced resolution (explicit
    /// <c>/扩图</c> width / height) always wins; a <c>null</c> resolution or a failed /
    /// planless parse is returned unchanged. <see cref="EditPlan"/> is init-only, so
    /// this rebuilds the plan to set the field.
    /// </summary>
    private static ParseResult ApplyResolution(ParseResult result, ResolutionPolicy? resolution)
    {
        if (resolution is null || result.Plan is not { Resolution: null } plan)
        {
            return result;
        }

        return new ParseResult
        {
            Success = result.Success,
            MatchedCommand = result.MatchedCommand,
            ErrorMessage = result.ErrorMessage,
            Plan = new EditPlan
            {
                PlanId = plan.PlanId,
                SourcePrompt = plan.SourcePrompt,
                MainImagePath = plan.MainImagePath,
                ReferenceImagePath = plan.ReferenceImagePath,
                AdditionalImages = plan.AdditionalImages,
                Mask = plan.Mask,
                Steps = plan.Steps,
                CreatedAt = plan.CreatedAt,
                Resolution = resolution,
            },
        };
    }

    private ParseResult ParseSlashCommand(string text, IEditSession session, int imageCount)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];
        var rawArgs = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        var command = _commands.FirstOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (command is null)
        {
            return Error($"Unknown command '{name}'.");
        }

        if (TryBuildArgs(command, rawArgs) is not { } effectiveArgs)
        {
            // A genuine config error (a variadic command with no parameter to hold the text)
            // keeps its own wording; otherwise the arguments are missing / wrong, so give the
            // user a concrete example of the command's expected shape.
            if (command.Variadic && command.Params.Count < 1)
            {
                return Error($"Command '{command.Name}' is variadic but declares no parameter.");
            }

            var paramList = string.Join(", ", command.Params);
            var example = command.Name switch
            {
                "/合照" => "/合照 两人在森林握手",
                "/换背景" => "/换背景 森林",
                "/换装" => "/换装 红色连衣裙",
                "/生成" => "/生成 森林里的精灵",
                _ => paramList.Length > 0 ? $"{command.Name} <{paramList}>" : command.Name,
            };
            return Error(
                $"Command '{command.Name}' expects {command.Params.Count} argument(s) ({paramList}). Try: {example}");
        }

        if (TrySelectTemplate(command, imageCount) is not { } template)
        {
            // A command whose variants lack a "single" template (e.g. /合照) is multi-only:
            // report the image requirement rather than a generic missing-template error.
            var multiOnly = command.Variants is { Count: > 0 } variants && !variants.ContainsKey("single");
            return Error(multiOnly
                ? $"Command '{command.Name}' requires at least 2 images."
                : $"Command '{command.Name}' has no template for this image count.");
        }

        var prompt = ApplyTemplate(template, command.Params, effectiveArgs);
        var mainImage = command.T2i ? "" : session.GetCurrentPipelineImagePath();
        if (string.IsNullOrWhiteSpace(mainImage) && string.IsNullOrWhiteSpace(prompt))
        {
            return Error("No current image and no prompt; cannot build a plan.");
        }

        var step = new EditStep
        {
            Order = 1,
            ToolName = command.Tool,
            Parameters = BuildParameters(prompt),
        };

        var plan = new EditPlan
        {
            SourcePrompt = text,
            MainImagePath = mainImage ?? "",
            Mask = session.GetCurrentMaskSpec(),
            Steps = new[] { step },
            Resolution = BuildResolution(command, effectiveArgs),
        };

        return new ParseResult
        {
            Success = true,
            Plan = plan,
            MatchedCommand = command.Name,
        };
    }

    /// <summary>
    /// Validates / normalizes the raw slash arguments. Non-variadic commands must be exact;
    /// variadic commands fold every trailing token into the last declared parameter so a
    /// multi-word description survives as one argument. Returns <c>null</c> on a mismatch.
    /// </summary>
    private static IReadOnlyList<string>? TryBuildArgs(CommandDefinition command, IReadOnlyList<string> rawArgs)
    {
        if (!command.Variadic)
        {
            if (rawArgs.Count != command.Params.Count)
            {
                return null;
            }

            return rawArgs;
        }

        if (command.Params.Count < 1)
        {
            return null;
        }

        // R1: a variadic command requires at least the declared parameters (non-empty input).
        if (rawArgs.Count < command.Params.Count)
        {
            return null;
        }

        var fixedCount = command.Params.Count - 1;
        var args = new string[command.Params.Count];
        for (var i = 0; i < fixedCount; i++)
        {
            args[i] = rawArgs[i];
        }

        args[fixedCount] = string.Join(' ', rawArgs.Skip(fixedCount));
        return args;
    }

    /// <summary>
    /// Picks the template for the given image count. A command with no variants uses its flat
    /// <see cref="CommandDefinition.Template"/>. Otherwise the variant key is "single" / "multi"
    /// (or <see cref="CommandDefinition.DefaultVariant"/> when the count is unknown); a known
    /// count with a missing key is an error, never a silent fallback.
    /// </summary>
    private static string? TrySelectTemplate(CommandDefinition command, int imageCount)
    {
        if (command.Variants is not { Count: > 0 })
        {
            return command.Template;
        }

        var effectiveCount = command.T2i ? 0 : imageCount;
        var key = effectiveCount < 0
            ? command.DefaultVariant
            : effectiveCount >= 2 ? "multi" : "single";

        return command.Variants.TryGetValue(key, out var template) && !string.IsNullOrWhiteSpace(template)
            ? template
            : null;
    }

    private ParseResult ParseNaturalLanguage(string prompt, IEditSession session)
    {
        var mainImage = session.GetCurrentPipelineImagePath();
        if (string.IsNullOrWhiteSpace(mainImage) && string.IsNullOrWhiteSpace(prompt))
        {
            return Error("No current image and no prompt; cannot build a plan.");
        }

        var step = new EditStep
        {
            Order = 1,
            ToolName = FallbackPlanner.EditToolName,
            Parameters = BuildParameters(prompt),
        };

        var plan = new EditPlan
        {
            SourcePrompt = prompt,
            MainImagePath = mainImage ?? "",
            Mask = session.GetCurrentMaskSpec(),
            Steps = new[] { step },
        };

        return new ParseResult { Success = true, Plan = plan };
    }

    private static Dictionary<string, string> BuildParameters(string prompt) => new(StringComparer.Ordinal)
    {
        ["prompt"] = prompt,
        ["steps"] = FallbackPlanner.DefaultSteps,
        ["denoise"] = FallbackPlanner.DefaultDenoise,
    };

    private static string ApplyTemplate(
        string template,
        IReadOnlyList<string> paramNames,
        IReadOnlyList<string> args)
    {
        var result = template;
        for (var i = 0; i < paramNames.Count && i < args.Count; i++)
        {
            result = result.Replace("{" + paramNames[i] + "}", args[i], StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// <c>QW21outpaint</c> requires <see cref="ResolutionMode.Explicit"/> (FROZEN 7.5).
    /// Commands targeting it must carry <c>width</c>/<c>height</c> parameters; when
    /// present and numeric they are lifted into the plan's resolution.
    /// </summary>
    private static ResolutionPolicy? BuildResolution(CommandDefinition command, IReadOnlyList<string> args)
    {
        if (!string.Equals(command.Tool, OutpaintToolName, StringComparison.Ordinal))
        {
            return null;
        }

        var widthIndex = command.Params.IndexOf("width");
        var heightIndex = command.Params.IndexOf("height");
        if (widthIndex < 0 || heightIndex < 0)
        {
            return null;
        }

        return int.TryParse(args[widthIndex], out var width) && width > 0
            && int.TryParse(args[heightIndex], out var height) && height > 0
                ? new ResolutionPolicy
                {
                    Mode = ResolutionMode.Explicit,
                    Width = width,
                    Height = height,
                }
                : null;
    }

    private static ParseResult Error(string message)
        => new() { Success = false, ErrorMessage = message };

    private static IReadOnlyList<CommandDefinition> LoadCommands(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var dto = JsonSerializer.Deserialize(json, CommandJsonContext.Default.CommandsFileDto);
                if (dto?.Commands is { Count: > 0 } commands)
                {
                    return commands;
                }
            }
        }
        catch (Exception)
        {
            // Any IO / parse failure falls through to the built-in defaults.
        }

        return BuiltInCommands();
    }

    private static IReadOnlyList<CommandDefinition> BuiltInCommands() => new[]
    {
        new CommandDefinition
        {
            Name = "/换背景",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Keep the character and pose in <image1> unchanged. Replace the background with {description}. Preserve the original facial identity, hair, body shape and pose.",
                ["multi"] = "Keep the character and pose in <image1> unchanged. Use the scene from <image2> as the new background. {description}. Preserve the original facial identity, hair, body shape and pose.",
            },
            Description = "替换背景（1 图直接换 / 2 图参考场景）",
        },
        new CommandDefinition
        {
            Name = "/换装",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Change the clothing of the person in <image1> to: {description}. Keep the facial identity, hair, body shape and pose unchanged, and keep the original background and lighting.",
                ["multi"] = "Use the garment from <image2> to dress the person in <image1>. {description}. Preserve the facial identity, body shape and pose, and keep the original background.",
            },
            Description = "更换服装（1 图文字描述 / 2 图参考服装）",
        },
        new CommandDefinition
        {
            Name = "/合照",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "multi",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["multi"] = "Create a new scene using the reference identities. The person from <image1> and the person from <image2> stand together. {description}. Preserve each person's identity independently; do not merge facial features or clothing between them.",
            },
            Description = "多主体合照（需至少 2 张图）",
        },
        new CommandDefinition
        {
            Name = "/生成",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            T2i = true,
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "{description}",
            },
            Description = "文生图（大模型扩写提示词）",
        },
        new CommandDefinition
        {
            Name = "/去水印",
            Params = new List<string>(),
            Tool = "QW21edit",
            Template = "Remove all watermarks, logos, and subtitles from the image. Keep all other content unchanged.",
            Description = "去除水印",
        },
        new CommandDefinition
        {
            Name = "/去物体",
            Params = new List<string> { "object" },
            Tool = "QW21edit",
            Template = "Remove the {object} from <image1>. Fill the removed area naturally to match the surrounding context. Keep all other content unchanged.",
            Description = "移除指定物体",
        },
        new CommandDefinition
        {
            Name = "/扩图",
            Params = new List<string> { "width", "height" },
            Tool = "QW21outpaint",
            Template = "Extend the canvas to {width}x{height}. Fill the extended area with content consistent with <image1>.",
            Description = "扩展画布",
        },
    };
}
