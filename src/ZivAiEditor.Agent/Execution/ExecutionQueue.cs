namespace ZivAiEditor.Agent.Execution;

/// <summary>
/// Single serial queue for GPU-bound work (Z18: the 4080 16GB must not run
/// concurrent inferences). A single-slot <see cref="SemaphoreSlim"/> guarantees
/// at most one task runs at a time; waiters are served in the order they arrive.
/// Priority scheduling is intentionally deferred to a later step.
/// </summary>
public sealed class ExecutionQueue : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Runs <paramref name="work"/> once the single slot is free, then releases
    /// it. The work receives the (linked) token so it can be canceled while
    /// queued or running.
    /// </summary>
    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await work(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
