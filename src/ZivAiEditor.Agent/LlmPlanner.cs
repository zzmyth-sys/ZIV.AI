using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;

namespace ZivAiEditor.Agent;

/// <summary>
/// LLM-backed planner: it asks an injected <see cref="ILlmClient"/> to turn the
/// user prompt plus the available tool list into a JSON <c>EditPlan</c>.
///
/// Per Z22 this planner is <b>allowed to fail</b>: a parse error, a malformed or
/// empty response, or a timeout throws <see cref="PlannerException"/>, and the
/// caller (e.g. <see cref="ResilientPlanner"/>) falls back to
/// <see cref="FallbackPlanner"/>. No LLM/runtime is loaded here (Z17); the
/// client is injected by the App layer.
/// </summary>
public sealed class LlmPlanner : IPlanner
{
    /// <summary>Default wall-clock budget for a single planning call.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ILlmClient _llm;
    private readonly IToolRegistry _tools;
    private readonly TimeSpan _timeout;

    public LlmPlanner(ILlmClient llm, IToolRegistry tools, TimeSpan? timeout = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<EditPlan> PlanAsync(PlanRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.MainImagePath))
        {
            throw new ArgumentException(
                "PlanRequest.MainImagePath is required (SPEC.md §3.1: the main image is mandatory).",
                nameof(request));
        }

        var systemPrompt = BuildSystemPrompt(_tools.All);
        var userPrompt = BuildUserPrompt(request);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        string response;
        try
        {
            response = await _llm.CompleteAsync(systemPrompt, userPrompt, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PlannerException(
                $"LLM planning timed out after {_timeout.TotalSeconds:0.#}s.");
        }
        catch (Exception ex) when (ex is not PlannerException)
        {
            throw new PlannerException("LLM planning call failed.", ex);
        }

        return ParsePlan(response, request);
    }

    private static EditPlan ParsePlan(string response, PlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new PlannerException("LLM returned an empty response.");
        }

        var json = ExtractJsonObject(response);

        LlmPlanDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, PlannerJsonContext.Default.LlmPlanDto);
        }
        catch (JsonException ex)
        {
            throw new PlannerException("LLM response is not valid EditPlan JSON.", ex);
        }

        if (dto?.Steps is null || dto.Steps.Count == 0)
        {
            throw new PlannerException("LLM plan contained no steps.");
        }

        var steps = new List<EditStep>(dto.Steps.Count);
        var order = 1;
        foreach (var step in dto.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Tool))
            {
                throw new PlannerException("A planned step is missing its tool name.");
            }

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (step.Params is not null)
            {
                foreach (var (key, value) in step.Params)
                {
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    parameters[key] = ValueToString(value);
                }
            }

            steps.Add(new EditStep
            {
                Order = order++,
                ToolName = step.Tool.Trim(),
                Parameters = parameters,
                DependsOn = (IReadOnlyList<string>?)step.DependsOn ?? Array.Empty<string>(),
            });
        }

        return new EditPlan
        {
            SourcePrompt = request.Prompt ?? "",
            MainImagePath = request.MainImagePath,
            ReferenceImagePath = request.ReferenceImagePath,
            Mask = request.Mask,
            Steps = steps,
        };
    }

    /// <summary>
    /// Tolerates a model that wraps the JSON in prose or a fenced code block by
    /// slicing from the first <c>{</c> to the last <c>}</c>.
    /// </summary>
    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new PlannerException("LLM response did not contain a JSON object.");
        }

        return raw.Substring(start, end - start + 1);
    }

    private static string ValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        _ => value.GetRawText(),
    };

    private static string BuildSystemPrompt(IReadOnlyList<IEditTool> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are ZIV.AI's image-edit planner. You convert a user's natural-language request");
        sb.AppendLine("plus the provided image slots into an ordered, executable edit plan.");
        sb.AppendLine();
        sb.AppendLine("Available tools (use the exact name):");

        if (tools.Count == 0)
        {
            sb.AppendLine("- QW21edit: edit the image using the prompt (a mask enables a local edit; no mask does a reference-conditioned edit).");
        }
        else
        {
            foreach (var tool in tools)
            {
                var caps = tool.Capabilities.Count > 0
                    ? string.Join(", ", tool.Capabilities)
                    : "general";
                sb.AppendLine($"- {tool.Name} [{caps}]: {tool.Description}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Use \"QW21edit\" for edits, with or without a mask.");
        sb.AppendLine("- Prefer the fewest steps that satisfy the request.");
        sb.AppendLine("- Parameter values must be JSON strings.");
        sb.AppendLine("- Respond with ONLY a JSON object, no prose and no markdown fences.");
        sb.AppendLine();
        sb.AppendLine("Output schema:");
        sb.AppendLine("{\"steps\":[{\"tool\":\"<tool name>\",\"params\":{\"prompt\":\"<text>\",\"steps\":\"25\",\"denoise\":\"1.0\"},\"depends_on\":[]}]}");
        sb.AppendLine();
        sb.AppendLine("Example 1 - prompt \"replace the sky with a sunset\", mask present:");
        sb.AppendLine("{\"steps\":[{\"tool\":\"QW21edit\",\"params\":{\"prompt\":\"replace the sky with a sunset\",\"steps\":\"25\",\"denoise\":\"1.0\"}}]}");
        sb.AppendLine("Example 2 - prompt \"make it look like an oil painting\", no mask:");
        sb.AppendLine("{\"steps\":[{\"tool\":\"QW21edit\",\"params\":{\"prompt\":\"make it look like an oil painting\",\"steps\":\"25\",\"denoise\":\"1.0\"}}]}");
        return sb.ToString();
    }

    private static string BuildUserPrompt(PlanRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("User request: ").AppendLine(request.Prompt ?? "");
        sb.Append("Main image: ").AppendLine(string.IsNullOrWhiteSpace(request.MainImagePath) ? "(none)" : "(provided)");
        sb.Append("Reference image: ").AppendLine(request.ReferenceImagePath is null ? "no" : "yes");
        sb.Append("Mask: ").AppendLine(request.Mask is null ? "no" : "yes");

        if (request.Options.Count > 0)
        {
            sb.AppendLine("Options:");
            foreach (var (key, value) in request.Options)
            {
                sb.Append("- ").Append(key).Append(": ").AppendLine(value);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Raised when <see cref="LlmPlanner"/> cannot produce a plan; the caller falls back (Z22).</summary>
public sealed class PlannerException : Exception
{
    public PlannerException(string message)
        : base(message)
    {
    }

    public PlannerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class LlmPlanDto
{
    [JsonPropertyName("steps")]
    public List<LlmStepDto>? Steps { get; set; }
}

internal sealed class LlmStepDto
{
    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    [JsonPropertyName("params")]
    public Dictionary<string, JsonElement>? Params { get; set; }

    [JsonPropertyName("depends_on")]
    public List<string>? DependsOn { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(LlmPlanDto))]
internal partial class PlannerJsonContext : JsonSerializerContext
{
}