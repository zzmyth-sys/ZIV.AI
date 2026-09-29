using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ZivAiEditor.Backend;

/// <summary>
/// In-process reader for <c>Template/loras.json</c> (mirrors <see cref="ModelProfileRegistry"/>).
/// The App layer looks up a LoRA id to show its real weight path / description; the Python
/// backend still resolves ids against the same file at use-time, so this reader is display-only.
/// The file is read once at construction; a missing / unreadable / malformed file yields an empty
/// table and never throws (Z28: no external dependency, never fails).
/// </summary>
public sealed class LoraRegistry
{
    private const string LoraFileName = "loras.json";

    private readonly Dictionary<string, LoraEntryDto> _entries;

    /// <param name="lorasFilePath">
    /// Overrides the registry path (tests / App wiring pass the template directory's file).
    /// Defaults to <c>{AppContext.BaseDirectory}/Template/loras.json</c>.
    /// </param>
    public LoraRegistry(string? lorasFilePath = null)
    {
        _entries = Load(lorasFilePath);
    }

    /// <summary>The entry for <paramref name="id"/>, or <c>null</c> when unknown / blank.</summary>
    public LoraEntryDto? TryGet(string? id)
        => !string.IsNullOrWhiteSpace(id) && _entries.TryGetValue(id.Trim(), out var entry)
            ? entry
            : null;

    /// <summary>Every registered LoRA entry (declaration order is not guaranteed).</summary>
    public IReadOnlyList<LoraEntryDto> All => _entries.Values.ToArray();

    private static Dictionary<string, LoraEntryDto> Load(string? lorasFilePath)
    {
        var entries = new Dictionary<string, LoraEntryDto>(StringComparer.OrdinalIgnoreCase);

        var path = lorasFilePath ?? Path.Combine(AppContext.BaseDirectory, "Template", LoraFileName);
        if (!File.Exists(path))
        {
            return entries;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize(stream, LoraJsonContext.Default.LorasFileDto);
            foreach (var entry in dto?.Loras ?? new List<LoraEntryDto>())
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    continue;
                }

                entries[entry.Id.Trim()] = entry;
            }
        }
        catch (Exception ex)
        {
            // A missing / malformed file is treated as empty, never fatal.
            Debug.WriteLine($"[lora] loras.json load failed: {ex.Message}");
        }

        return entries;
    }
}
