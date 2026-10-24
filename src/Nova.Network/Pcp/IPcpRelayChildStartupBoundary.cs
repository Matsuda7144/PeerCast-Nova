using System.Diagnostics.CodeAnalysis;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Optional initial-send barrier before live queue delivery. Cancellation requests
/// must not complete the barrier until the actual initial sends have ended.
/// </summary>
internal interface IPcpRelayChildStartupBoundary
{
    Task StartupCompletion { get; }
    Task StartupCancellationCompletion { get; }
    void RequestStartupCancellation();
}

internal enum PcpHttpFlvStartupPoint { BeforeHeaderWrite, BeforeBootstrapWrite, BeforeCancellation }

internal sealed class PcpHttpFlvStartupObserver(Func<PcpHttpFlvStartupPoint, ValueTask> observe)
{
    public ValueTask ObserveAsync(PcpHttpFlvStartupPoint point) => observe(point);
}

/// <summary>
/// Owns the startup CTS until initial writes, the worker and real cancellation
/// processing have ended. Requesting cancellation never settles a send barrier.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The owned CTS is asynchronously disposed by DisposeAfterWorkerAsync only after writes, worker and cancellation have terminated.")]
internal sealed class PcpHttpFlvStartupLifetime(PcpHttpFlvStartupObserver? observer = null)
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private readonly TaskCompletionSource _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _cancellation;
    private Task? _cleanup;
    private int _writes;
    private bool _activate;
    private bool _ended;
    private bool _writeFailed;
    private bool _cancelRequested;
    private bool _cancelClosed;
    private int _disposed;
    private int _cancellationFailed;

    public Task StartupCompletion => _startup.Task;
    public Task CancellationCompletion { get { lock (_gate) { return _cancellation ?? Task.CompletedTask; } } }
    internal bool CancellationFailed => Volatile.Read(ref _cancellationFailed) != 0;
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    internal CancellationToken Token { get { lock (_gate) { return _source.Token; } } }

    public async Task WriteAsync(Func<CancellationToken, Task> write, PcpHttpFlvStartupPoint point)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_ended || _activate || IsDisposed) throw new InvalidOperationException("HTTP startup admission is closed.");
            token = _source.Token;
            _writes++;
        }
        try
        {
            if (observer is not null) await observer.ObserveAsync(point).ConfigureAwait(false);
            await write(token).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate) { _writeFailed = true; }
            throw;
        }
        finally
        {
            lock (_gate) { _writes--; SettleStartupLocked(); }
        }
    }

    public void Activate() { lock (_gate) { _activate = true; SettleStartupLocked(); } }
    public void EndStartup() { lock (_gate) { _ended = true; SettleStartupLocked(); } }

    private void SettleStartupLocked()
    {
        if (_writes != 0 || (!_activate && !_ended)) return;
        if (_cancelRequested) _startup.TrySetCanceled();
        else if (_activate && !_writeFailed) _startup.TrySetResult();
        else _startup.TrySetException(new IOException("HTTP initial send did not complete."));
    }

    public void RequestCancellation()
    {
        lock (_gate)
        {
            if (_cancelClosed) return;
            _cancelRequested = true;
            // Never run CTS callbacks while holding an owner/fanout lock.
            if (_cancellation is null)
            {
                _cancellation = Task.Run(CancelCoreAsync);
                _ = _cancellation.ContinueWith(task => { _ = task.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private async Task CancelCoreAsync()
    {
        try
        {
            if (observer is not null)
                await observer.ObserveAsync(PcpHttpFlvStartupPoint.BeforeCancellation).ConfigureAwait(false);
            await _source.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Exchange(ref _cancellationFailed, 1);
            throw;
        }
    }

    public Task DisposeAfterWorkerAsync(Task workerCompletion)
    {
        lock (_gate) { return _cleanup ??= Task.Run(() => DisposeCoreAsync(workerCompletion)); }
    }

    private async Task DisposeCoreAsync(Task workerCompletion)
    {
        // A faulted/cancelled worker or startup task is terminal, but not delivery
        // success. Their failures are classified by the worker/fanout owner.
        try { await workerCompletion.ConfigureAwait(false); } catch { }
        try { await _startup.Task.ConfigureAwait(false); } catch { }
        Task cancellation;
        lock (_gate)
        {
            _cancelClosed = true;
            cancellation = _cancellation ?? Task.CompletedTask;
        }
        try { await cancellation.ConfigureAwait(false); }
        finally
        {
            _source.Dispose();
            Volatile.Write(ref _disposed, 1);
        }
    }
}
