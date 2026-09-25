using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.Agent.Execution;

/// <summary>
/// Origin of a merged command entry. Deliberately <b>not</b> on the contract
/// <see cref="CommandDefinition"/> (T2 decision): the source is a store-layer concern.
/// </summary>
public enum CommandSource
{
    BuiltIn,
    User,
}

/// <summary>One merged command plus where it came from (service-layer view; T5 surfaces it).</summary>
public sealed record CommandTemplateDto(CommandDefinition Definition, CommandSource Source);

/// <summary>
/// Template store over the built-in <c>commands.json</c> and the user override
/// <c>commands.user.json</c> (T2). Reads return a by-<c>name</c> merge (user overrides a
/// built-in of the same name; new names append). Writes only ever touch the user file, never
/// the built-in data ship, so a re-publish cannot clobber user commands.
/// </summary>
public interface ICommandTemplateService
{
    /// <summary>The merged command list (built-in order, then user-only additions).</summary>
    IReadOnlyList<CommandTemplateDto> List();

    /// <summary>Adds or replaces a user entry by <see cref="CommandDefinition.Name"/>.</summary>
    void Add(CommandDefinition command);

    /// <summary>Replaces the user entry named <paramref name="name"/> with <paramref name="command"/>.</summary>
    void Update(string name, CommandDefinition command);

    /// <summary>Removes the user entry named <paramref name="name"/> (built-in, if any, shows through).</summary>
    void Delete(string name);

    /// <summary>Removes the user override named <paramref name="name"/> (= revert to the built-in).</summary>
    void Reset(string name);

    /// <summary>Removes the whole user override file (= revert every command to the built-in set).</summary>
    void ResetAll();
}
