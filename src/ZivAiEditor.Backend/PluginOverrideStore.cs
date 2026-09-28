using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZivAiEditor.Backend;

/// <summary>
/// Writes the plugin user-override file <c>plugins.user.json</c> (batch 3 · 注销登记). Only the
/// one operation the UI needs is exposed: drop one id from the user list. The built-in
/// <c>plugins.json</c> is never touched — "注销" means "remove my override", so an id that is
/// also built-in simply reverts to the built-in entry on the next <see cref="PluginRegistry"/>
/// load (the documented outcome of the whole-entry merge). When the resulting list is empty the
/// user file is deleted (mirrors <c>CommandTemplateService.ResetAll</c>).
///
/// Reads stay in <see cref="PluginRegistry"/> (single JSON reader); this type only writes, and
/// its writes are atomic (temp file + <see cref="File.Move(string, string, bool)"/>). It never
/// throws: a read / parse / IO failure degrades to "no override removed" with a Debug trace,
/// matching the never-fail plugin path.
/// </summary>
public static class PluginOverrideStore
{
    /// <summary>
    /// Removes <paramref name="pluginId"/> from the user override file next to
    /// <paramref name="builtInPath"/>. A missing file / id is a no-op. Returns true when the file
    /// changed (an entry was dropped), false otherwise. Never throws.
    /// </summary>
    public static bool RemoveEntry(string builtInPath, string pluginId)
    {
        if (string.IsNullOrWhiteSpace(builtInPath) || string.IsNullOrWhiteSpace(pluginId))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(builtInPath)) ?? ".";
            var userPath = Path.Combine(directory, PluginRegistry.UserFileName);
            if (!File.Exists(userPath))
            {
                return false;
            }

            var entries = ReadEntries(userPath);
            var kept = entries
                .Where(entry => !string.Equals(Id(entry), pluginId, StringComparison.Ordinal))
                .ToList();

            if (kept.Count == entries.Count)
            {
                return false;
            }

            if (kept.Count == 0)
            {
                File.Delete(userPath);
                return true;
            }

            WriteEntries(userPath, kept);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[plugin] override removal failed: {ex.Message}");
            return false;
        }
    }

    private static List<JsonObject> ReadEntries(string userPath)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(userPath));
            if (node?["plugins"] is not JsonArray array)
            {
                return new List<JsonObject>();
            }

            return array.OfType<JsonObject>().ToList();
        }
        catch (Exception)
        {
            // A malformed user file is treated as empty, never fatal.
            return new List<JsonObject>();
        }
    }

    private static void WriteEntries(string userPath, List<JsonObject> entries)
    {
        var root = new JsonObject
        {
            ["version"] = "1",
            ["_comment"] = "用户插件覆盖（whole-entry 覆盖内置；删除条目 = 注销）。",
            ["plugins"] = new JsonArray(entries.Select(entry => (JsonNode)entry.DeepClone()).ToArray()),
        };

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temp = userPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, userPath, overwrite: true);
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

    private static string Id(JsonObject entry) => entry["id"]?.GetValue<string>() ?? string.Empty;
}