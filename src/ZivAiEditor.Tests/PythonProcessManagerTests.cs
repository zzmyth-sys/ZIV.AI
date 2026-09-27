using System.IO.Pipes;
using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Batch P1 / B16 (+ Z8 split): a failing start must not leave a Python process or a named pipe
/// behind. The start info is built (and validates the script, which can throw) before any
/// resource is created, and a failed <c>Process.Start()</c> disposes the pipe / process.
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

    [Fact]
    public async Task Failed_Start_Releases_The_Pipe_Name()
    {
        // A valid script (validator passes) but a non-existent PythonExe makes Process.Start()
        // throw AFTER the pipe is created. The B16 fix must dispose that pipe, so the pipe name
        // is free again — a leak would make this probe throw "all pipe instances are busy".
        var script = Path.GetTempFileName();
        var name = "zivai.infer.test." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new PythonBackendOptions
            {
                PipeName = name,
                PythonExe = @"C:\does\not\exist\python.exe",
                Script = script,
            };
            using var manager = new PythonProcessManager(options);

            await Assert.ThrowsAnyAsync<Exception>(() => manager.EnsureStartedAsync());
            Assert.Null(manager.ProcessId);

            using var probe = new NamedPipeServerStream(
                name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }
        finally
        {
            try
            {
                File.Delete(script);
            }
            catch
            {
            }
        }
    }
}

