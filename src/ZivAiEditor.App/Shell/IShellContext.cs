using System;
using ZivAiEditor.UI;

namespace ZivAiEditor.App;

/// <summary>
/// The narrow shell surface the bridge needs (bridge §6): settings + template paths, plus the
/// single-instance launch event so the headless runner can answer a forwarded quick request with
/// <c>busy</c>. Implemented by <see cref="ShellService"/>; the only point that touches app
/// composition.
/// </summary>
internal interface IShellContext
{
    /// <summary>Reads the program-directory <c>settings.ini</c> (Z14).</summary>
    BackendSettings LoadSettings();

    /// <summary>The merged command-template directory (<c>commands.json</c>).</summary>
    string TemplateDirectory { get; }

    /// <summary>Raised for each request handed over by a later process (single-instance pipe).</summary>
    event Action<LaunchOptions>? LaunchRequested;
}
