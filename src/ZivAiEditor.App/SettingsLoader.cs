using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ZivAiEditor.App;

internal sealed class BackendSettings
{
    public string PipeName { get; init; } = "zivai.infer.v1";

    public string PythonExe { get; init; } = @"D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe";

    public string Script { get; init; } = @"D:\devlop\ZIV.AI\python\server\main.py";

    public LlmPlannerSettings LlmPlanner { get; init; } = new();

    public LlmRewriterSettings LlmRewriter { get; init; } = new();
}

/// <summary>
/// Planner-scoped LLM settings (<c>[llm.planner]</c>). Kept as a distinct type
/// so later scenarios (prompt rewriting / multi-image) can each get their own
/// settings block and <c>LlmClientOptions</c> without sharing a class.
/// </summary>
internal sealed class LlmPlannerSettings
{
    public string Endpoint { get; init; } = "http://127.0.0.1:8080/v1/chat/completions";

    public string? Model { get; init; }

    public double Temperature { get; init; } = 0.1;

    public int MaxTokens { get; init; } = 2048;

    public bool EnableThinking { get; init; }

    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Prompt-rewriter LLM settings (<c>[llm.rewriter]</c>). Shares the <c>LlmClientOptions</c>
/// shape with the planner but keeps higher temperature / its own VRAM budget for the
/// <c>/生成</c> preflight.
/// </summary>
internal sealed class LlmRewriterSettings
{
    public string Endpoint { get; init; } = "http://127.0.0.1:8080/v1/chat/completions";

    public string? Model { get; init; }

    public double Temperature { get; init; } = 0.7;

    public int MaxTokens { get; init; } = 2048;

    public bool EnableThinking { get; init; }

    public int TimeoutSeconds { get; init; } = 60;

    public double VramTotalMb { get; init; } = 16376;

    public double VramNeedMb { get; init; } = 9800;
}

/// <summary>
/// Reads the program-directory <c>settings.ini</c> (Z14). When the file is
/// missing it is seeded from the repository-root template if one can be found;
/// otherwise the built-in defaults are used (Z28: no external dependency).
/// </summary>
internal static class SettingsLoader
{
    public const string FileName = "settings.ini";

    public static BackendSettings Load(string? programDirectory = null)
    {
        var directory = programDirectory ?? System.AppContext.BaseDirectory;
        var path = Path.Combine(directory, FileName);
        EnsurePresent(path, directory);

        var sections = ParseSections(File.Exists(path) ? path : null);
        var backend = Section(sections, "backend");
        var planner = Section(sections, "llm.planner");
        var rewriter = Section(sections, "llm.rewriter");

        var defaults = new BackendSettings();
        var plannerDefaults = defaults.LlmPlanner;
        var rewriterDefaults = defaults.LlmRewriter;
        return new BackendSettings
        {
            PipeName = Get(backend, "pipe_name", defaults.PipeName),
            PythonExe = Get(backend, "python_exe", defaults.PythonExe),
            Script = Get(backend, "script", defaults.Script),
            LlmPlanner = new LlmPlannerSettings
            {
                Endpoint = Get(planner, "endpoint", plannerDefaults.Endpoint),
                Model = GetOptional(planner, "model") ?? plannerDefaults.Model,
                Temperature = GetDouble(planner, "temperature", plannerDefaults.Temperature),
                MaxTokens = GetInt(planner, "max_tokens", plannerDefaults.MaxTokens),
                EnableThinking = GetBool(planner, "enable_thinking", plannerDefaults.EnableThinking),
                TimeoutSeconds = GetInt(planner, "timeout_seconds", plannerDefaults.TimeoutSeconds),
            },
            LlmRewriter = new LlmRewriterSettings
            {
                Endpoint = Get(rewriter, "endpoint", rewriterDefaults.Endpoint),
                Model = GetOptional(rewriter, "model") ?? rewriterDefaults.Model,
                Temperature = GetDouble(rewriter, "temperature", rewriterDefaults.Temperature),
                MaxTokens = GetInt(rewriter, "max_tokens", rewriterDefaults.MaxTokens),
                EnableThinking = GetBool(rewriter, "enable_thinking", rewriterDefaults.EnableThinking),
                TimeoutSeconds = GetInt(rewriter, "timeout_seconds", rewriterDefaults.TimeoutSeconds),
                VramTotalMb = GetDouble(rewriter, "vram_total_mb", rewriterDefaults.VramTotalMb),
                VramNeedMb = GetDouble(rewriter, "vram_need_mb", rewriterDefaults.VramNeedMb),
            },
        };
    }

    private static void EnsurePresent(string path, string directory)
    {
        if (File.Exists(path))
        {
            return;
        }

        var template = FindTemplate();
        if (template is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.Copy(template, path, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? FindTemplate()
    {
        var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "DOC", "FROZEN.md")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseSections(string? path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (path is null || !File.Exists(path))
        {
            return sections;
        }

        Dictionary<string, string>? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var name = line.Trim('[', ']').Trim();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections[name] = current;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                current[key] = value;
            }
        }

        return sections;
    }

    private static Dictionary<string, string> Section(
        Dictionary<string, Dictionary<string, string>> sections,
        string name)
        => sections.TryGetValue(name, out var section) ? section : new Dictionary<string, string>();

    private static string Get(Dictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    private static string? GetOptional(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static int GetInt(Dictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var value)
           && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static double GetDouble(Dictionary<string, string> values, string key, double fallback)
        => values.TryGetValue(key, out var value)
           && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static bool GetBool(Dictionary<string, string> values, string key, bool fallback)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback,
        };
    }
}