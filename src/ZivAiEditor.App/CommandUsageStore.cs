using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZivAiEditor.App;

internal sealed class CommandUsageFileDto
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("counts")]
    public Dictionary<string, int>? Counts { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CommandUsageFileDto))]
internal partial class CommandUsageJsonContext : JsonSerializerContext
{
}

/// <summary>
/// T5/S4-fix: file-backed per-command usage counter for the <c>/</c> suggestion ordering. Lives
/// in the program directory (Z14) as <c>commands.usage.json</c>; a missing / malformed file is
/// treated as empty, and a failed write is silent (never fatal). App-layer state only — not a
/// contract, not part of the command set.
/// </summary>
internal sealed class CommandUsageStore
{
    public const string FileName = "commands.usage.json";

    private const int FileVersion = 1;

    private readonly string _path;
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public CommandUsageStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(directory, FileName);
    }

    /// <summary>The live counts (updated in place by <see cref="Record"/>).</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>Reads the usage file into the cache; a missing / malformed file yields empty.</summary>
    public IReadOnlyDictionary<string, int> Load()
    {
        _counts.Clear();
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var dto = JsonSerializer.Deserialize(json, CommandUsageJsonContext.Default.CommandUsageFileDto);
                if (dto?.Counts is { Count: > 0 } counts)
                {
                    foreach (var pair in counts)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                        {
                            _counts[pair.Key] = pair.Value;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // A missing / malformed file is treated as empty, never fatal.
            Debug.WriteLine($"[usage] load failed: {ex.Message}");
            _counts.Clear();
        }

        return _counts;
    }

    /// <summary>Increments the count for <paramref name="commandName"/> and persists (silent on failure).</summary>
    public void Record(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        _counts.TryGetValue(commandName, out var count);
        _counts[commandName] = count + 1;
        Write();
    }

    private void Write()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dto = new CommandUsageFileDto
            {
                Version = FileVersion,
                Counts = new Dictionary<string, int>(_counts, StringComparer.Ordinal),
            };
            var json = JsonSerializer.Serialize(dto, CommandUsageJsonContext.Default.CommandUsageFileDto);

            // Atomic-ish write (mirrors the template store): temp file + replace.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[usage] write failed: {ex.Message}");
        }
    }
}
