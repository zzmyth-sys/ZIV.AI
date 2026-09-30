using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Serializes the test classes that set / read the process-wide <c>ZIV_AI_SETTINGS_PATH</c>
/// environment variable. <c>SettingsLoader.ResolvePath</c> (and everything built on it:
/// <c>AppContext.BuildBackendEnvironment</c>, <c>SettingsWindow</c>) reads that variable, so a
/// class that sets it must not run in parallel with any class that resolves the settings path.
/// Mark such classes with <c>[Collection(SettingsEnvCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SettingsEnvCollection
{
    public const string Name = "SettingsEnv";
}
