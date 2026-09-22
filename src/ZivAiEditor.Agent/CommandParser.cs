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
    Task<ParseResult> ParseAsync(string input, EditSession session, CancellationToken ct = default);
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

    private readonly IReadOnlyList<CommandDefinition> _commands;

    public CommandParser(string commandsJsonPath = DefaultCommandsPath)
    {
        _commands = LoadCommands(commandsJsonPath);
    }

    /// <summary>The loaded command set (file or built-in default).</summary>
    public IReadOnlyList<CommandDefinition> Commands => _commands;

    public Task<ParseResult> ParseAsync(string input, EditSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ct.ThrowIfCancellationRequested();

        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            return Task.FromResult(Error("Input is empty; enter a command or a prompt."));
        }

        var result = text.StartsWith('/')
            ? ParseSlashCommand(text, session)
            : ParseNaturalLanguage(text, session);

        return Task.FromResult(result);
    }

    private ParseResult ParseSlashCommand(string text, EditSession session)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];
        var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        var command = _commands.FirstOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (command is null)
        {
            return Error($"Unknown command '{name}'.");
        }

        if (args.Length != command.Params.Count)
        {
            return Error(
                $"Command '{command.Name}' expects {command.Params.Count} argument(s) " +
                $"({string.Join(", ", command.Params)}), got {args.Length}.");
        }

        var prompt = ApplyTemplate(command, args);
        var mainImage = session.GetCurrentImagePath();
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
            Steps = new[] { step },
            Resolution = BuildResolution(command, args),
        };

        return new ParseResult
        {
            Success = true,
            Plan = plan,
            MatchedCommand = command.Name,
        };
    }

    private ParseResult ParseNaturalLanguage(string prompt, EditSession session)
    {
        var mainImage = session.GetCurrentImagePath();
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

    private static string ApplyTemplate(CommandDefinition command, IReadOnlyList<string> args)
    {
        var result = command.Template;
        for (var i = 0; i < command.Params.Count && i < args.Count; i++)
        {
            result = result.Replace("{" + command.Params[i] + "}", args[i], StringComparison.Ordinal);
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
            Params = new List<string> { "target" },
            Tool = "QW21edit",
            Template = "Keep the character and pose in <image1> unchanged. Replace the background with {target}. Preserve the original facial identity, hair, body shape and pose.",
            Description = "替换图像背景",
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
