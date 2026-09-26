using System;
using System.Collections.Generic;
using ZivAiEditor.App.Flows;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;
using ZivAiEditor.UI.Chat;

namespace ZivAiEditor.Tests;

/// <summary>
/// Test harness for the session view model (module-boundary migration step 6): builds the view
/// model, the App's <see cref="FlowRunner"/> and attaches them (two-phase wiring), so tests can
/// keep exercising the same public surface (<c>SubmitAsync</c> / <c>RerunNodeAsync</c> /
/// <c>CancelCurrent</c>) as before.
/// </summary>
internal static class FlowRunnerHarness
{
    public static SessionViewModel Create(
        IEditSession session,
        IEditSessionWriter writer,
        ICommandParser parser,
        IExecutor executor,
        IImagingService? imaging = null,
        Action<string, IReadOnlyCollection<string>, bool>? nodeArtifactsCleaner = null)
    {
        var vm = new SessionViewModel(session, writer, imaging);
        var flow = new FlowRunner(vm, session, writer, parser, executor, null, null, nodeArtifactsCleaner, imaging);
        vm.AttachFlowRunner(flow);
        return vm;
    }
}
