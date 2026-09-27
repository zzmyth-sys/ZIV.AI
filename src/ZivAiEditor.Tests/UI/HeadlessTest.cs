using System;
using Avalonia.Headless;

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// Minimal headless UI-test runner (Z-008 groundwork). The xUnit integration package
/// (Avalonia.Headless.XUnit 12.1.1) targets xUnit v3 while this repo uses xUnit v2, so
/// the base <see cref="HeadlessUnitTestSession"/> is driven directly: the test body is
/// dispatched onto the Avalonia UI thread and the call blocks until it completes.
/// </summary>
internal static class HeadlessTest
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessTest).Assembly);

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }
}
