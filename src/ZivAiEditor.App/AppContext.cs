using System;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.App;

/// <summary>
/// Composition root for the App layer (Z1: no static hub). Wires the Python
/// process manager and IPC client from <c>settings.ini</c> and exposes the
/// inference client as the frozen <see cref="IInferenceClient"/> contract.
/// </summary>
internal sealed class AppContext : IDisposable
{
    private bool _disposed;

    private AppContext(PythonProcessManager backend, IpcInferenceClient client)
    {
        Backend = backend;
        Client = client;
    }

    public PythonProcessManager Backend { get; }

    public IInferenceClient Client { get; }

    public static AppContext Create()
    {
        var settings = SettingsLoader.Load();
        var options = new PythonBackendOptions
        {
            PipeName = settings.PipeName,
            PythonExe = settings.PythonExe,
            Script = settings.Script,
            AutoRestartEnabled = true,
        };

        var backend = new PythonProcessManager(options);
        var client = new IpcInferenceClient(backend, ownsProcess: true);
        return new AppContext(backend, client);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Client.Dispose();
        Backend.Dispose();
    }
}
