using System.Text.Json.Serialization;

namespace ZivAiEditor.Contracts.Execution;

/// <summary>
/// Backend path a command routes to (T2: declared on <see cref="CommandDefinition.Handler"/>).
/// The actual routing (splitting the parse / dispatch) lands in T3. Serialized by name in
/// <c>commands.json</c> / <c>commands.user.json</c>; an absent <c>handler</c> falls back to
/// <see cref="Edit"/> so legacy files keep working.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommandHandler>))]
public enum CommandHandler
{
    Edit,
    T2I,
    Outpaint,
    Tag,
}
