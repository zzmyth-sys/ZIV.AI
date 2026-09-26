using System.Diagnostics;
using System.Text.Json;
using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.Agent.Execution;

/// <summary>
/// File-backed <see cref="ICommandTemplateService"/> (T2). The built-in
/// <c>commands.json</c> and the user <c>commands.user.json</c> live side by side in the
/// template directory; a missing / malformed file is treated as empty (the built-in file
/// alone still falls back to <see cref="CommandParser.BuiltInCommands"/>). Writes are atomic
/// (temp file + replace) and only touch the user file.
/// </summary>
public sealed class CommandTemplateService : ICommandTemplateService
{
    public const string BuiltInFileName = "commands.json";
    public const string UserFileName = "commands.user.json";

    private const string FileVersion = "1.0";

    private readonly string _builtInPath;
    private readonly string _userPath;

    public CommandTemplateService(string templateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);
        _builtInPath = Path.Combine(templateDirectory, BuiltInFileName);
        _userPath = Path.Combine(templateDirectory, UserFileName);
    }

    /// <summary>The user override path (exposed for diagnostics / tests).</summary>
    public string UserFilePath => _userPath;

    public IReadOnlyList<CommandTemplateDto> List()
    {
        var builtIn = ReadFile(_builtInPath) ?? CommandParser.BuiltInCommands();
        var user = ReadFile(_userPath) ?? Array.Empty<CommandDefinition>();

        if (user.Count == 0)
        {
            return builtIn
                .Select(command => new CommandTemplateDto(command, CommandSource.BuiltIn))
                .ToList();
        }

        var userByName = new Dictionary<string, CommandDefinition>(StringComparer.Ordinal);
        foreach (var entry in user)
        {
            userByName[entry.Name] = entry;
        }

        var merged = new List<CommandTemplateDto>(builtIn.Count + user.Count);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in builtIn)
        {
            if (userByName.TryGetValue(entry.Name, out var overridden))
            {
                merged.Add(new CommandTemplateDto(overridden, CommandSource.User));
                consumed.Add(entry.Name);
            }
            else
            {
                merged.Add(new CommandTemplateDto(entry, CommandSource.BuiltIn));
            }
        }

        foreach (var entry in user)
        {
            if (!consumed.Contains(entry.Name))
            {
                merged.Add(new CommandTemplateDto(entry, CommandSource.User));
            }
        }

        return merged;
    }

    public void Add(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Upsert(command.Name, command);
    }

    public void Update(string name, CommandDefinition command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(command);
        Upsert(name, command);
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var entries = ReadUserEntries();
        entries.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
        WriteUserEntries(entries);
    }

    /// <summary>Reverts one command to the built-in set by dropping its user override.</summary>
    public void Reset(string name) => Delete(name);

    public void ResetAll()
    {
        try
        {
            if (File.Exists(_userPath))
            {
                File.Delete(_userPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[commands] reset all failed: {ex.Message}");
        }
    }

    private void Upsert(string name, CommandDefinition command)
    {
        var entries = ReadUserEntries();
        entries.RemoveAll(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
        entries.Add(command);
        WriteUserEntries(entries);
    }

    private List<CommandDefinition> ReadUserEntries()
        => ReadFile(_userPath)?.ToList() ?? new List<CommandDefinition>();

    private void WriteUserEntries(List<CommandDefinition> entries)
    {
        if (entries.Count == 0)
        {
            ResetAll();
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_userPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var normalized = entries.Select(NormalizeLora).ToList();
            var dto = new CommandsFileDto { Version = FileVersion, Commands = normalized };
            var json = JsonSerializer.Serialize(dto, CommandJsonContext.Default.CommandsFileDto);

            // Atomic-ish write (mirrors the settings precedent): write a sibling temp file,
            // then replace the target in one move so a crash cannot leave a half-written file.
            var temp = _userPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _userPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[commands] write user file failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Folds a legacy single <see cref="CommandDefinition.Lora"/> into
    /// <see cref="CommandDefinition.Loras"/> before writing, so the user file only ever carries
    /// the multi-slot form (T2: "read lora, write loras"). An entry that already uses only
    /// <c>loras</c> is returned unchanged.
    /// </summary>
    private static CommandDefinition NormalizeLora(CommandDefinition command)
    {
        if (command.Lora is null)
        {
            return command;
        }

        return new CommandDefinition
        {
            Name = command.Name,
            Params = command.Params,
            Tool = command.Tool,
            Template = command.Template,
            Variants = command.Variants,
            DefaultVariant = command.DefaultVariant,
            Variadic = command.Variadic,
            T2i = command.T2i,
            Description = command.Description,
            Handler = command.Handler,
            // Bridge §4.1-3: the quick-edit marker + label must survive the field-level rebuild;
            // the pre-existing FixedResolution silent drop is fixed alongside them.
            Quick = command.Quick,
            ShortcutLabel = command.ShortcutLabel,
            FixedResolution = command.FixedResolution,
            Loras = command.EffectiveLoras.Count > 0 ? command.EffectiveLoras.ToList() : null,
            Lora = null,
        };
    }

    private static IReadOnlyList<CommandDefinition>? ReadFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, CommandJsonContext.Default.CommandsFileDto);
            return dto?.Commands is { Count: > 0 } commands ? commands : null;
        }
        catch (Exception)
        {
            // A missing / malformed file is treated as empty, never fatal.
            return null;
        }
    }
}
