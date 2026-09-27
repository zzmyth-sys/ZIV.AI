using System.Text.Json;
using System.Text.Json.Serialization;
using ZivAiEditor.Diagnostics;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Agent.Execution;

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
/// Regex-free, deterministic <see cref="ICommandParser"/>. Commands are held as an
/// immutable set: the App composes the built-in + user merged view
/// (<c>ICommandTemplateService.List()</c>) and passes it in (T5/S1); the
/// <c>Template/commands.json</c> ctor remains for tests / standalone use, falling back
/// to a built-in default set when the file is missing or malformed, so the app works
/// with no external files (Z28).
///
/// Command matching is ordinal (case-sensitive). Parameter substitution is a
/// plain <c>{name}</c> string replace — no type conversion. Two structural extras
/// supply an explicit resolution (P1a): a command's own
/// <see cref="CommandDefinition.FixedResolution"/> wins; otherwise numeric
/// <c>width</c>/<c>height</c> arguments become an explicit
/// <see cref="ResolutionPolicy"/>. The UI-selected tier applies only when neither
/// is present. The name <c>/扩图</c> is special-cased (P1) to the crop-tool
/// outpaint: it requires the current node's outpaint crop, runs Qwen's
/// reference-conditioned edit on the blue-padded canvas, and carries no
/// resolution of its own (the UI-selected tier applies, like every other command).
/// </summary>
public sealed partial class CommandParser : ICommandParser
{
    /// <summary>Default commands file, resolved relative to the process working directory.</summary>
    public const string DefaultCommandsPath = "Template/commands.json";

    /// <summary>Capability marker returned for <see cref="CommandHandler.Tag"/> (T3.1; T4 executes).</summary>
    public const string TagCapability = "tag";

    /// <summary>Sentinel image count used when the caller does not know the pipeline size.</summary>
    private const int UnknownImageCount = -1;

    /// <summary>Name-based trigger for the crop-tool outpaint follow-up (never handler/tool based).</summary>
    private const string OutpaintCommandName = "/扩图";

    private readonly IReadOnlyList<CommandDefinition> _commands;

    /// <summary>
    /// Builds a parser over an already-resolved command set (T5/S1: the built-in + user merged
    /// view from <c>ICommandTemplateService.List()</c>). The set is held as-is; this ctor never
    /// touches the filesystem.
    /// </summary>
    public CommandParser(IReadOnlyList<CommandDefinition> commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <summary>
    /// File-backed parser (Step 8): loads a single <c>commands.json</c> and delegates to the
    /// list ctor; a missing / malformed file falls back to <see cref="BuiltInCommands"/> so the
    /// app works with no external files (Z28). Behavior is unchanged from the pre-S1 ctor.
    /// </summary>
    public CommandParser(string commandsJsonPath = DefaultCommandsPath)
        : this(LoadCommands(commandsJsonPath))
    {
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
        => ParseAsync(input, session, imageCount, resolution, outputPath: null, ct);

    public Task<ParseResult> ParseAsync(
        string input,
        IEditSession session,
        int imageCount,
        ResolutionPolicy? resolution,
        string? outputPath,
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
            ? ParseSlashCommand(text, session, imageCount, outputPath)
            : ParseNaturalLanguage(text, session, outputPath);

        return Task.FromResult(ApplyResolution(result, resolution));
    }

    /// <summary>
    /// Stamps the UI-selected <paramref name="resolution"/> onto a plan that does not
    /// carry one of its own (V3); the parser-produced resolution (a command's
    /// <see cref="CommandDefinition.FixedResolution"/> or explicit <c>width</c>/<c>height</c>
    /// arguments) always wins. A <c>null</c> resolution or a failed / planless parse is
    /// returned unchanged. <see cref="EditPlan"/> is init-only, so this rebuilds the plan
    /// to set the field.
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
                // B5: carry the plan's ModelId across the rebuild. Unreachable today (the
                // parser never sets ModelId), but a parse path that does must not silently
                // drop it. Same shape as WithAdditionalImages (ChatFlowRules / Executor).
                ModelId = plan.ModelId,
            },
        };
    }

    private ParseResult ParseSlashCommand(
        string text,
        IEditSession session,
        int imageCount,
        string? outputPath)
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
            // A malformed command (variadic with no parameter to hold the text) is a config
            // error; otherwise the user simply omitted the argument, so answer in plain words.
            if (command.Variadic && command.Params.Count < 1)
            {
                return Error($"命令「{command.Name}」配置有误：variadic 需要至少 1 个参数，但未声明任何参数（请检查 commands.json）。");
            }

            return Error(MissingArgsMessage(command));
        }

        // T3.1: route by the effective handler and validate the fields it owns (warn + ignore, never silent).
        var handler = command.EffectiveHandler;
        var warnings = new List<string>(ValidateFields(command, handler));
        if (command.Loras is { Count: > 0 } rawLoras && command.EffectiveLoras.Count < rawLoras.Count)
        {
            warnings.Add($"命令 '{command.Name}' 的 loras 含重复 path，已按首次去重（保留首次）。");
        }

        // P1 · /扩图 relocation: the follow-up after a crop-tool outpaint. Keyed by name (never by
        // handler / tool). It runs Qwen's reference-conditioned edit on the blue-padded canvas
        // (the crop gate is the authoritative rule) and carries no resolution of its own, so the
        // UI-selected tier applies like every other command.
        var isOutpaint = string.Equals(command.Name, OutpaintCommandName, StringComparison.Ordinal);
        if (isOutpaint)
        {
            var currentNode = session.GetHistory().FirstOrDefault(n => n.NodeId == session.CurrentNodeId);
            var crop = currentNode?.Crop;
            if (DiagLog.IsEnabled)
            {
                // D3 diag (observation only): mirror the gate inputs; the branch below is unchanged.
                DiagLog.Log($"D3 gate current={session.CurrentNodeId} {DiagLog.DescribeCrop(crop)} passes={crop is not null && crop.IsOutpaint()}");
            }

            if (crop is null || !crop.IsOutpaint())
            {
                return Error("「/扩图」需先做裁切外扩（当前节点没有外扩裁切）。", warnings);
            }
        }

        if (ValidateImageCount(command, handler, imageCount) is { } countError)
        {
            return Error(countError, warnings);
        }

        if (handler == CommandHandler.Tag)
        {
            // T3.1: recognized as a capability call, not an EditPlan; execution lands in T4.
            return new ParseResult
            {
                Success = false,
                MatchedCommand = command.Name,
                Capability = TagCapability,
                ErrorMessage = $"打标命令 '{command.Name}' 已识别；实际执行将在后续版本提供。",
                Warnings = warnings,
            };
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
        var mainImage = handler == CommandHandler.T2I ? "" : session.GetCurrentPipelineImagePath();
        if (isOutpaint && DiagLog.IsEnabled)
        {
            // D2 diag (observation only): the resolved /扩图 source image + current-node crop.
            var sourceNode = session.GetHistory().FirstOrDefault(n => n.NodeId == session.CurrentNodeId);
            DiagLog.Log($"D2 mainImage current={session.CurrentNodeId} {DiagLog.DescribeCrop(sourceNode?.Crop)} mainImage={mainImage}");
        }

        if (string.IsNullOrWhiteSpace(mainImage) && string.IsNullOrWhiteSpace(prompt))
        {
            return Error("No current image and no prompt; cannot build a plan.", warnings);
        }

        var step = new EditStep
        {
            Order = 1,
            ToolName = command.Tool,
            Parameters = BuildParameters(prompt, outputPath),
            // T3.2: carry the command's LoRAs (multi-slot, de-duplicated) to the executor / tool.
            Loras = BuildLoras(command.EffectiveLoras),
        };

        var plan = new EditPlan
        {
            SourcePrompt = text,
            MainImagePath = mainImage ?? "",
            // /扩图 runs maskless on the blue-padded canvas (the crop gate above); every other
            // command keeps the node's hand-drawn mask.
            Mask = isOutpaint ? null : session.GetCurrentMaskSpec(),
            Steps = new[] { step },
            Resolution = ResolveResolution(command, effectiveArgs),
        };

        return new ParseResult
        {
            Success = true,
            Plan = plan,
            MatchedCommand = command.Name,
            Warnings = warnings,
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

        var effectiveCount = command.EffectiveHandler == CommandHandler.T2I ? 0 : imageCount;
        var key = effectiveCount < 0
            ? command.DefaultVariant
            : effectiveCount >= 2 ? "multi" : "single";

        return command.Variants.TryGetValue(key, out var template) && !string.IsNullOrWhiteSpace(template)
            ? template
            : null;
    }

    private ParseResult ParseNaturalLanguage(string prompt, IEditSession session, string? outputPath)
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
            Parameters = BuildParameters(prompt, outputPath),
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

    private static Dictionary<string, string> BuildParameters(string prompt, string? outputPath)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["prompt"] = prompt,
            ["steps"] = FallbackPlanner.DefaultSteps,
            ["denoise"] = FallbackPlanner.DefaultDenoise,
        };

        // Bridge §4.1-4: a quick-edit caller pins the absolute output path, consumed downstream by
        // ToolOutputPath.Resolve → EditRequest.OutputPath → IPC submit.payload.output_path.
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            parameters["output_path"] = outputPath!;
        }

        return parameters;
    }

    /// <summary>
    /// Normalizes a command's LoRA (Step 8-1; Step 8-2 made the strengths nullable). A missing
    /// strength (<c>null</c>, i.e. the JSON key is absent) falls back to the declared default
    /// 1.0; an explicit <c>0</c> is <b>preserved</b> (0 = fully suppress that side, not "unset").
    /// A blank path drops the LoRA entirely.
    /// </summary>
    private static LoraOptions? NormalizeLora(LoraOptions? lora)
    {
        if (lora is null || string.IsNullOrWhiteSpace(lora.Path))
        {
            return null;
        }

        return new LoraOptions
        {
            Path = lora.Path,
            StrengthModel = lora.StrengthModel ?? 1.0,
            StrengthClip = lora.StrengthClip ?? 1.0,
        };
    }

    /// <summary>
    /// Normalizes the effective LoRAs for an edit step (T3.2): drops blank paths and applies the
    /// Step 8-2 strength defaults; returns <c>null</c> when none remain.
    /// </summary>
    private static List<LoraOptions>? BuildLoras(IReadOnlyList<LoraOptions> effective)
    {
        var resolved = effective
            .Select(lora => NormalizeLora(lora))
            .Where(lora => lora is not null)
            .Select(lora => lora!)
            .ToList();
        return resolved.Count > 0 ? resolved : null;
    }

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
    /// P1a resolution precedence: a command's own <see cref="CommandDefinition.FixedResolution"/>
    /// wins; otherwise valid <c>width</c>/<c>height</c> arguments become an explicit policy;
    /// otherwise <c>null</c> so the UI-selected tier is injected later.
    /// </summary>
    private static ResolutionPolicy? ResolveResolution(CommandDefinition command, IReadOnlyList<string> args)
        => command.FixedResolution ?? BuildResolution(command, args);

    /// <summary>
    /// Lifts numeric <c>width</c>/<c>height</c> arguments into an explicit policy. Any command
    /// that declares those parameters qualifies (P1a; no longer tied to any single tool, and
    /// unused by the built-in set after the <c>/扩图</c> relocation). Invalid / missing values
    /// return <c>null</c>.
    /// </summary>
    private static ResolutionPolicy? BuildResolution(CommandDefinition command, IReadOnlyList<string> args)
    {
        var widthIndex = command.Params.IndexOf("width");
        var heightIndex = command.Params.IndexOf("height");
        if (widthIndex < 0 || heightIndex < 0 || widthIndex >= args.Count || heightIndex >= args.Count)
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

    private static ParseResult Error(string message, IReadOnlyList<string>? warnings = null)
        => new()
        {
            Success = false,
            ErrorMessage = message,
            Warnings = warnings ?? Array.Empty<string>(),
        };

    /// <summary>
    /// T3.1 field-ownership check: a field that does not belong to the command's handler is
    /// ignored and reported (never silently). One message per offending field.
    /// </summary>
    private static IReadOnlyList<string> ValidateFields(CommandDefinition command, CommandHandler handler)
    {
        var warnings = new List<string>();
        if (handler == CommandHandler.Tag)
        {
            if (!string.IsNullOrWhiteSpace(command.Template))
            {
                warnings.Add($"handler=Tag 不支持 template（已忽略）：'{command.Name}'。");
            }

            if (command.Variants is { Count: > 0 })
            {
                warnings.Add($"handler=Tag 不支持 variants（已忽略）：'{command.Name}'。");
            }

            if (command.T2i)
            {
                warnings.Add($"handler=Tag 不支持 t2i（已忽略）：'{command.Name}'。");
            }

            if (!string.IsNullOrWhiteSpace(command.Tool))
            {
                warnings.Add($"handler=Tag 不支持 tool（已忽略）：'{command.Name}'。");
            }

            if (command.EffectiveLoras.Count > 0)
            {
                warnings.Add($"handler=Tag 不支持 loras（已忽略）：'{command.Name}'。");
            }

            if (command.FixedResolution is not null)
            {
                warnings.Add($"handler=Tag 不支持 fixed_resolution（已忽略）：'{command.Name}'。");
            }
        }
        else if (command.T2i && command.Handler != CommandHandler.Edit)
        {
            // t2i is the legacy source of T2I; when the handler is given explicitly it is redundant.
            warnings.Add($"handler={handler} 时 t2i 为冗余字段（已忽略）：'{command.Name}'。");
        }

        return warnings;
    }

    /// <summary>
    /// T3.1 handler input constraints (裁决 2): T2I takes no image; Outpaint / Tag take exactly
    /// one; Edit takes one or many. An unknown count (<c>-1</c>) is unconstrained. Returns an
    /// error message, or <c>null</c> when the count is acceptable.
    /// </summary>
    private static string? ValidateImageCount(CommandDefinition command, CommandHandler handler, int imageCount)
    {
        if (imageCount < 0)
        {
            return null;
        }

        return handler switch
        {
            CommandHandler.T2I when imageCount > 0 =>
                $"命令 '{command.Name}' 为文生图（handler=T2I），不接受输入图（收到 {imageCount} 张）。",
            CommandHandler.Outpaint when imageCount != 1 =>
                $"命令 '{command.Name}'（handler=Outpaint）需要恰好 1 张输入图（收到 {imageCount} 张）。",
            CommandHandler.Tag when imageCount != 1 =>
                $"打标命令 '{command.Name}'（handler=Tag）需要恰好 1 张输入图（收到 {imageCount} 张）。",
            _ => null,
        };
    }

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

}
