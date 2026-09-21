using System;
using System.Collections.Generic;
using System.IO;

namespace ZivAiEditor.App;

internal sealed class BackendSettings
{
    public string PipeName { get; init; } = "zivai.infer.v1";

    public string PythonExe { get; init; } = @"D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe";

    public string Script { get; init; } = @"D:\devlop\ZIV.AI\python\server\main.py";
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

        var values = Parse(File.Exists(path) ? path : null);
        var defaults = new BackendSettings();
        return new BackendSettings
        {
            PipeName = Get(values, "pipe_name", defaults.PipeName),
            PythonExe = Get(values, "python_exe", defaults.PythonExe),
            Script = Get(values, "script", defaults.Script),
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

    private static Dictionary<string, string> Parse(string? path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (path is null || !File.Exists(path))
        {
            return values;
        }

        var inBackend = false;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                inBackend = line.Trim('[', ']').Trim().Equals("backend", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inBackend)
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
                values[key] = value;
            }
        }

        return values;
    }

    private static string Get(Dictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;
}
