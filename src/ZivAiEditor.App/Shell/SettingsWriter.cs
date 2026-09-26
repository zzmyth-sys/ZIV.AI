using System;
using System.Collections.Generic;
using System.IO;

namespace ZivAiEditor.App;

/// <summary>
/// Key-value writer for the program-directory <c>settings.ini</c> (settings window).
/// Only the targeted keys are touched: every other line, comment and the overall order are
/// preserved. Writes are atomic (temp file + <see cref="File.Move(string, string, bool)"/>)
/// so a crash mid-write cannot truncate the live settings file.
/// </summary>
internal static class SettingsWriter
{
    /// <summary>Writes <c>[models] dit_path / te_path / vae_path</c>; empty clears a key.</summary>
    public static void WriteModelPaths(string settingsPath, string ditPath, string tePath, string vaePath)
        => WriteSectionValues(
            settingsPath,
            "models",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dit_path"] = ditPath ?? string.Empty,
                ["te_path"] = tePath ?? string.Empty,
                ["vae_path"] = vaePath ?? string.Empty,
            });

    /// <summary>Writes <c>[backend] python_exe</c>; empty clears the key.</summary>
    public static void WritePythonExe(string settingsPath, string pythonExe)
        => WriteSectionValues(
            settingsPath,
            "backend",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["python_exe"] = pythonExe ?? string.Empty,
            });

    /// <summary>Writes <c>[backend] script</c> (the backend main.py); empty clears the key.</summary>
    public static void WriteScript(string settingsPath, string scriptPath)
        => WriteSectionValues(
            settingsPath,
            "backend",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["script"] = scriptPath ?? string.Empty,
            });

    /// <summary>Writes <c>[backend] comfy_root</c> (ComfyUI source tree); empty clears the key.</summary>
    public static void WriteComfyRoot(string settingsPath, string comfyRoot)
        => WriteSectionValues(
            settingsPath,
            "backend",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["comfy_root"] = comfyRoot ?? string.Empty,
            });

    private static void WriteSectionValues(
        string path,
        string section,
        IReadOnlyDictionary<string, string> values)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";
        var temp = Path.Combine(directory, Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N"));

        try
        {
            var lines = File.Exists(fullPath) ? File.ReadAllLines(fullPath) : Array.Empty<string>();

            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                var value = pair.Value;
                normalized[pair.Key] = string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : Path.GetFullPath(value).Trim();
            }

            var pending = new Dictionary<string, string>(normalized, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(lines.Length + normalized.Count + 2);
            var sectionFound = false;
            var inTarget = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && trimmed[0] == '[')
                {
                    if (inTarget)
                    {
                        FlushPending(result, pending);
                    }

                    var name = trimmed.Trim('[', ']').Trim();
                    inTarget = string.Equals(name, section, StringComparison.OrdinalIgnoreCase);
                    sectionFound |= inTarget;
                    result.Add(line);
                    continue;
                }

                if (inTarget
                    && pending.Count > 0
                    && !IsComment(trimmed)
                    && TryReadKey(trimmed, out var key)
                    && pending.Remove(key, out var value))
                {
                    result.Add(Format(key, value));
                    continue;
                }

                result.Add(line);
            }

            if (inTarget)
            {
                FlushPending(result, pending);
            }

            if (!sectionFound)
            {
                if (result.Count > 0 && result[^1].Trim().Length > 0)
                {
                    result.Add(string.Empty);
                }

                result.Add($"[{section}]");
                foreach (var pair in normalized)
                {
                    result.Add(Format(pair.Key, pair.Value));
                }
            }

            Directory.CreateDirectory(directory);
            File.WriteAllLines(temp, result);
            File.Move(temp, fullPath, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new ApplicationException(
                $"Failed to write settings section [{section}] to '{fullPath}'.", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception)
            {
                // Best effort: a leftover temp file must never mask the real failure.
            }
        }
    }

    private static void FlushPending(List<string> result, Dictionary<string, string> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var pair in pending)
        {
            result.Add(Format(pair.Key, pair.Value));
        }

        pending.Clear();
    }

    private static string Format(string key, string value)
        => value.Length == 0 ? $"{key} =" : $"{key} = {value}";

    private static bool IsComment(string trimmed)
        => trimmed.Length > 0 && trimmed[0] is ';' or '#';

    private static bool TryReadKey(string line, out string key)
    {
        key = string.Empty;
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        key = line[..separator].Trim();
        return key.Length > 0;
    }
}
