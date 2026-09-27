using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Batch P1 / B16: a failing start must not leave a Python process behind. The start info is
/// built (and validates the script, which can throw) BEFORE the named pipe is created, so a
/// validation failure cannot leak the pipe / process. (Structural check: the pipe is created
/// only on the success path; its disposal is by construction.)
/// </summary>
public class PythonProcessManagerTests
{
    [Fact]
    public async Task EnsureStarted_Invalid_Script_Throws_And_Leaves_No_Process()
    {
        var options = new PythonBackendOptions
        {
            PipeName = "zivai.infer.test." + Guid.NewGuid().ToString("N"),
            PythonExe = @"C:\does\not\exist\python.exe",
            Script = @"C:\does\not\exist\main.py",
        };
        using var manager = new PythonProcessManager(options);

        await Assert.ThrowsAsync<ApplicationException>(() => manager.EnsureStartedAsync());

        Assert.Null(manager.ProcessId);
        Assert.False(manager.IsPipeConnected);
    }
}
