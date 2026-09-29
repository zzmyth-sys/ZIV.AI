using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.Agent.Execution;

/// <summary>
/// Runtime hot-reload half of <see cref="CommandParser"/> (Z-030 复议). <see cref="Reload"/>
/// replaces the held command set <b>in place</b> so every holder of the same parser instance
/// (Executor / FlowRunner / the UI) observes the new commands without any re-dispatch; the
/// instance reference never changes. This is a concrete-type capability — deliberately not on
/// <c>ICommandParser</c> — called by the App layer from <c>AppContext.ReloadCommands()</c>.
/// </summary>
public sealed partial class CommandParser
{
    /// <summary>
    /// Replaces the command set with <paramref name="commands"/> in place. The parser instance
    /// identity is unchanged, so callers that captured it keep working. Thread-safety is by
    /// convention: the App calls this on the UI thread while no parse is mid-flight.
    /// </summary>
    public void Reload(IReadOnlyList<CommandDefinition> commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }
}
