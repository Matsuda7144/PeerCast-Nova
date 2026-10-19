using System.Threading.Channels;

namespace PeerCast.Nova.App;

/// <summary>Shared History/root document durability; revisions are process-local composite checkpoints.</summary>
internal sealed record GenesisRecordingHistoryDurabilitySnapshot(
    GenesisRecordingHistoryLoadStatus LoadStatus, long LastPersistedRevision, long LatestRevision,
    GenesisRecordingHistoryWriteStatus LastWriteStatus, GenesisRecordingHistoryStoreFailure Failure,
    bool AutomaticPersistenceEnabled)
{
    public bool Pending => LatestRevision > LastPersistedRevision;
    public bool IsDegraded => !AutomaticPersistenceEnabled || LastWriteStatus is
        GenesisRecordingHistoryWriteStatus.Failed or GenesisRecordingHistoryWriteStatus.Cancelled;
}

/// <summary>One coalescing whole-snapshot writer. Terminal callbacks only TryWrite a wake.</summary>
internal sealed class GenesisRecordingHistoryPersistenceCoordinator : IAsyncDisposable
{
    private readonly GenesisRecordingHistoryReadModel _history;
    private readonly GenesisRecordingLibraryKnownRootRegistry _roots;
    private readonly GenesisRecordingHistoryStore _store;
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite, AllowSynchronousContinuations = false });
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private readonly Task _worker;
    private readonly GenesisRecordingHistoryLoadStatus _loadStatus;
    private string? _fingerprint;
    private long _persistedRevision;
    private long _attemptedRevision;
    private GenesisRecordingHistoryWriteStatus _lastWrite;
    private GenesisRecordingHistoryStoreFailure _failure;
    private bool _enabled;
    private Task? _shutdownTask;

    internal GenesisRecordingHistoryPersistenceCoordinator(GenesisRecordingHistoryReadModel history, GenesisRecordingHistoryStore store,
        GenesisRecordingLibraryKnownRootRegistry? roots = null)
    {
        _history = history;
        _roots = roots ?? new();
        _store = store;
        var loaded = store.Load(); // Before composition exposes RecordingRuntime.
        _loadStatus = loaded.Status;
        _fingerprint = loaded.Fingerprint;
        _enabled = loaded.CanWrite;
        _failure = loaded.Failure;
        if (loaded.Snapshot is { } snapshot) history.Hydrate(snapshot);
        if (loaded.KnownRoots is { } knownRoots) _roots.Hydrate(knownRoots);
        _persistedRevision = _attemptedRevision = GetLatestRevision();
        _history.Changed += Signal;
        _roots.Changed += Signal;
        _worker = Task.Run(WorkAsync);
    }

    internal GenesisRecordingHistoryDurabilitySnapshot GetSnapshot()
    {
        var latest = GetLatestRevision();
        lock (_gate) return new(_loadStatus, _persistedRevision, latest, _lastWrite, _failure, _enabled);
    }

    private void Signal() => _wake.Writer.TryWrite(true);

    private long GetLatestRevision() => _history.GetSnapshot().Revision + _roots.GetSnapshot().Revision;

    internal Task WorkerCompletion => _worker;

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var _ in _wake.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
                await FlushAsync(token: _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    // Normal attempts never retry the same failed revision. Later semantic
    // changes retry the latest snapshot; shutdown permits one final retry.
    internal async Task FlushAsync(bool finalAttempt = false, CancellationToken token = default)
    {
        await _writer.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // One serialized writer captures both independent monotonic models.
            // A change after either capture posts a wake and makes the composite
            // revision newer, so a subsequent write preserves the latest pair.
            var snapshot = new GenesisRecordingMetadataSnapshot(_history.GetSnapshot(), _roots.GetSnapshot());
            string? expected;
            lock (_gate)
            {
                if (!_enabled || snapshot.Revision <= _persistedRevision ||
                    (!finalAttempt && snapshot.Revision <= _attemptedRevision)) return;
                _attemptedRevision = snapshot.Revision;
                expected = _fingerprint;
            }
            var result = await _store.SaveAsync(snapshot, expected, token).ConfigureAwait(false);
            lock (_gate)
            {
                _lastWrite = result.Status;
                _failure = result.Failure;
                if (result.Status == GenesisRecordingHistoryWriteStatus.Saved)
                {
                    _fingerprint = result.Fingerprint;
                    _persistedRevision = snapshot.Revision;
                }
                if (result.Status == GenesisRecordingHistoryWriteStatus.Conflict) _enabled = false;
            }
            // Revisions arriving while serialization/I/O ran are coalesced into
            // a later full snapshot, without storing per-event facts in a queue.
            if (GetLatestRevision() > snapshot.Revision) Signal();
        }
        finally { _writer.Release(); }
    }

    internal Task ShutdownAsync(TimeSpan? timeout = null)
    {
        lock (_gate) return _shutdownTask ??= ShutdownCoreAsync(timeout ?? TimeSpan.FromSeconds(10));
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    private async Task ShutdownCoreAsync(TimeSpan timeout)
    {
        Task finalFlush = Task.CompletedTask;
        _history.Changed -= Signal;
        _roots.Changed -= Signal;
        _wake.Writer.TryComplete();
        _shutdown.CancelAfter(timeout);
        try
        {
            await _worker.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            // Keep synchronous serialization/fingerprint/OS I/O off the
            // shutdown caller too, so cancellation can bound its waiting.
            finalFlush = Task.Run(() => FlushAsync(finalAttempt: true, token: _shutdown.Token));
            await finalFlush.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate) { _lastWrite = GenesisRecordingHistoryWriteStatus.Cancelled; _failure = GenesisRecordingHistoryStoreFailure.Cancelled; }
        }
        finally { _ = ReleaseResourcesAsync(finalFlush); }
        // Cancellation bounds caller waiting. Synchronous OS flush cannot be
        // forcibly interrupted; the worker retains its ownership until unwind.
    }

    private async Task ReleaseResourcesAsync(Task finalFlush)
    {
        try { await Task.WhenAll(_worker, finalFlush).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { _writer.Dispose(); _shutdown.Dispose(); }
    }
}
