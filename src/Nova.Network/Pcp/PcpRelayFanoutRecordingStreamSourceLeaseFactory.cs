using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Creates recording readers as bounded children of an existing PCP relay
/// fan-out. It never creates or owns a network connection.
/// </summary>
public sealed partial class PcpRelayFanoutRecordingStreamSourceLeaseFactory :
    IRecordingStreamSourceLeaseFactory
{
    private readonly object _gate = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _sessionId;
    private readonly PcpRelayFanout _fanout;
    private readonly PcpRecordingPacketStreamBridgeOptions _options;
    private readonly Func<IAsyncDisposable?>? _acquireOwnerLease;
    private readonly HashSet<RecordingLease> _leases = [];

    private bool _accepting;
    private bool _completed;
    private Task? _completionTask;

    public PcpRelayFanoutRecordingStreamSourceLeaseFactory(
        ChannelId channelId,
        ChannelSessionId sessionId,
        PcpRelayFanout fanout,
        PcpRecordingPacketStreamBridgeOptions? options = null,
        Func<IAsyncDisposable?>? acquireOwnerLease = null)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(fanout);

        var effectiveOptions = options is null
            ? new PcpRecordingPacketStreamBridgeOptions()
            : options with { };
        effectiveOptions.Validate();

        _channelId = channelId;
        _sessionId = sessionId;
        _fanout = fanout;
        _options = effectiveOptions;
        _acquireOwnerLease = acquireOwnerLease;
    }

    internal PcpRecordingSourceCleanupObserver? CleanupObserver { get; init; }

    public ChannelId ChannelId => _channelId;

    public int ActiveLeaseCount
    {
        get
        {
            lock (_gate)
            {
                return _leases.Count(lease => !lease.IsDisposed);
            }
        }
    }

    /// <summary>
    /// Opens acquisition after the channel owner has started its packet pump.
    /// </summary>
    public void StartAccepting()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _accepting = true;
        }
    }

    /// <summary>
    /// Closes acquisition without terminating readers that were already
    /// returned to recording sessions. The owner completes those readers
    /// after its fan-out drain has finished.
    /// </summary>
    internal void StopAccepting()
    {
        lock (_gate)
        {
            _accepting = false;
        }
    }

    public async ValueTask<IRecordingStreamSourceLease?> AcquireAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Candidate.ChannelId != _channelId)
            throw new ArgumentException("The recording request channel does not match this fan-out.", nameof(request));
        lock (_gate)
        {
            if (!_accepting || _completed) return null;
            if (_pendingAcquisitions++ == 0)
                _acquisitionsSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await AcquireCoreAsync(request, cancellationToken).ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                if (--_pendingAcquisitions == 0) _acquisitionsSettled!.TrySetResult();
            }
        }
    }

    private async ValueTask<IRecordingStreamSourceLease?> AcquireCoreAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Candidate.ChannelId != _channelId)
        {
            throw new ArgumentException(
                "The recording request channel does not match this fan-out.",
                nameof(request));
        }

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_accepting || _completed)
            {
                return null;
            }
        }

        IAsyncDisposable? ownerLease;
        try { ownerLease = _acquireOwnerLease?.Invoke(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            RecordAcquisitionFailure();
            throw;
        }
        if (_acquireOwnerLease is not null && ownerLease is null)
        {
            return null;
        }

        var lease = new RecordingLease(
            this,
            _channelId,
            _sessionId,
            _options,
            ownerLease);
        var accepted = false;

        try
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_accepting && !_completed)
                {
                    try
                    {
                        if (_fanout.TryAddChild(lease, out var registration))
                        {
                            lease.Attach(registration);
                            _leases.Add(lease);
                            _acquiredLeases = checked(_acquiredLeases + 1);
                            lease.ObserveCompletion();
                            accepted = true;
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // The owner may stop between acquisition and child reservation.
                    }
                }
            }
        }
        catch (Exception acquisitionFailure)
        {
            if (acquisitionFailure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                RecordAcquisitionFailure();
            try { await DisposeRejectedLeaseAsync(lease).ConfigureAwait(false); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Recording acquisition and rejected cleanup failed.", acquisitionFailure, cleanupFailure);
            }
            throw;
        }

        if (!accepted)
        {
            await DisposeRejectedLeaseAsync(lease).ConfigureAwait(false);
            return null;
        }

        return lease;
    }

    private void RecordAcquisitionFailure()
    {
        lock (_gate) { _acquisitionFailures = checked(_acquisitionFailures + 1); }
    }

    private async Task DisposeRejectedLeaseAsync(RecordingLease lease)
    {
        lock (_gate) { _rejectedCleanupPending++; }
        try { await lease.DisposeAsync().ConfigureAwait(false); }
        catch
        {
            // Independent of the acquisition token, including cleanup OCE.
            lock (_gate) { _rejectedCleanupFailures = checked(_rejectedCleanupFailures + 1); }
            throw;
        }
        finally { lock (_gate) { _rejectedCleanupPending--; } }
    }

    /// <summary>
    /// Closes future acquisitions and completes existing readers. A null
    /// terminal fault produces EOF; a fault is propagated as an exception.
    /// </summary>
    public ValueTask CompleteAsync(Exception? terminalFault = null)
    {
        RecordingLease[] leases;
        TaskCompletionSource completionSource;

        lock (_gate)
        {
            _accepting = false;
            _completed = true;

            if (_completionTask is not null)
            {
                return new ValueTask(_completionTask);
            }

            completionSource = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _completionTask = completionSource.Task;
            _ownerFaultRequested = terminalFault is not null;
            ObserveLateFailure(_completionTask);
            leases = [.. _leases];
        }

        _ = CompleteLeasesAsync(
            leases,
            terminalFault,
            completionSource);
        return new ValueTask(completionSource.Task);
    }

    private static async Task CompleteLeasesAsync(
        RecordingLease[] leases,
        Exception? terminalFault,
        TaskCompletionSource completionSource)
    {
        try
        {
            foreach (var lease in leases)
            {
                await lease.CompleteFromOwnerAsync(
                    terminalFault).ConfigureAwait(false);
            }

            completionSource.TrySetResult();
        }
        catch (Exception exception)
        {
            completionSource.TrySetException(exception);
        }
    }

    private void Remove(RecordingLease lease)
    {
        lock (_gate)
        {
            if (_leases.Remove(lease))
            {
                _ = lease.CleanupCompletion.Exception;
                _retiredEvidence = _retiredEvidence.Add(lease.Evidence) with
                { RetiredLeaseCount = checked(_retiredEvidence.RetiredLeaseCount + 1) };
            }
        }
    }

    private sealed class RecordingLease :
        IRecordingStreamSourceLease,
        IPcpRelayChildSink,
        IPcpRelayChildCompletionObserver
    {
        private readonly object _gate = new();
        private readonly PcpRelayFanoutRecordingStreamSourceLeaseFactory _owner;
        private readonly ChannelId _channelId;
        private readonly ChannelSessionId _sessionId;
        private readonly PcpRecordingPacketStreamBridge _bridge;
        private readonly IAsyncDisposable? _ownerLease;

        private PcpRelayChildRegistration? _registration;
        private Stream? _readStream;
        private Task? _terminalTask;
        private Task? _disposeTask;
        private readonly TaskCompletionSource _cleanupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _sourceFault;
        private bool _deliveryFailed;
        private bool _eofNotified;
        private bool _faultNotified;
        private bool _aborted;
        private bool _registrationCleanupFailed;
        private bool _bridgeCleanupFailed;
        private bool _ownerCleanupFailed;
        private int _workerTokenObserved;
        private long? _expectedSourceSequence;
        private long _recordingSequence;
        private int _terminalRequested;
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public Task CleanupCompletion => _cleanupCompletion.Task;
        public PcpRelayChildRegistration? Registration => _registration;

        ValueTask IPcpRelayChildCompletionObserver.ObserveCompletionAsync() =>
            _owner.CleanupObserver?.AfterRegistrationCompletion is { } observe ? new(observe()) : ValueTask.CompletedTask;

        public PcpRecordingSourceEvidence Evidence
        {
            get
            {
                lock (_gate)
                {
                    var child = _registration?.Snapshot;
                    var ended = _registration?.Completion.IsCompleted == true;
                    var registrationFailed = _registration?.Completion.IsFaulted == true ||
                        _registration?.Completion.IsCanceled == true;
                    var clean = _cleanupCompletion.Task.IsCompletedSuccessfully &&
                        _registration?.Completion.IsCompletedSuccessfully == true && child?.CleanupFailed != true;
                    return new()
                    {
                        ActiveLeaseCount = IsDisposed ? 0 : 1,
                        CleanupPendingLeaseCount = _cleanupCompletion.Task.IsCompleted ? 0 : 1,
                        EofNotificationCount = _eofNotified ? 1 : 0,
                        FaultNotificationCount = _faultNotified ? 1 : 0,
                        SourceFaultLeaseCount = _sourceFault ? 1 : 0,
                        AbortedLeaseCount = _aborted ? 1 : 0,
                        DeliveryFailedLeaseCount = _deliveryFailed || child?.DiscardedPacketCount > 0 ||
                            child?.EndReason is PcpRelayChildEndReason.SinkFailed or PcpRelayChildEndReason.SlowConsumer ? 1 : 0,
                        ChildStoppedCount = ended ? 1 : 0,
                        CleanupCompletedLeaseCount = clean ? 1 : 0,
                        CleanupFailedLeaseCount = _cleanupCompletion.Task.IsFaulted ||
                            _cleanupCompletion.Task.IsCanceled || registrationFailed || child?.CleanupFailed == true ? 1 : 0,
                        RegistrationCleanupFailedLeaseCount = _registrationCleanupFailed || registrationFailed || child?.CleanupFailed == true ? 1 : 0,
                        BridgeCleanupFailedLeaseCount = _bridgeCleanupFailed ? 1 : 0,
                        OwnerCleanupFailedLeaseCount = _ownerCleanupFailed ? 1 : 0,
                        PendingPacketCount = child?.PendingPacketCount ?? 0,
                        DeliveredPacketCount = child?.DeliveredPacketCount ?? 0,
                        DeliveredByteCount = child?.DeliveredByteCount ?? 0,
                        DiscardedPacketCount = child?.DiscardedPacketCount ?? 0,
                        DiscardedByteCount = child?.DiscardedByteCount ?? 0,
                    };
                }
            }
        }

        public async Task StopChildAsync(CancellationToken cancellationToken)
        {
            if (_registration is not { } registration) return;
            _ = await registration.StopAsync(cancellationToken).ConfigureAwait(false);
            await registration.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public RecordingLease(
            PcpRelayFanoutRecordingStreamSourceLeaseFactory owner,
            ChannelId channelId,
            ChannelSessionId sessionId,
            PcpRecordingPacketStreamBridgeOptions options,
            IAsyncDisposable? ownerLease)
        {
            _owner = owner;
            _channelId = channelId;
            _sessionId = sessionId;
            _ownerLease = ownerLease;
            _bridge = new PcpRecordingPacketStreamBridge(
                channelId,
                options);
        }

        public Stream ReadStream
        {
            get
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(
                        Volatile.Read(ref _disposed) != 0,
                        this);
                    return _readStream ??= _bridge.OpenReadStream();
                }
            }
        }

        public async ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(packet);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (Interlocked.Exchange(ref _workerTokenObserved, 1) == 0)
                    _owner.CleanupObserver?.WorkerTokenObserved?.Invoke(cancellationToken);
                if (Volatile.Read(ref _disposed) != 0)
                {
                    ObjectDisposedException.ThrowIf(true, this);
                }

                if (Volatile.Read(ref _terminalRequested) != 0)
                {
                    throw new IOException(
                        "The recording source owner has completed.");
                }

                if (!packet.IsRetainedHeadReplay &&
                    _expectedSourceSequence is { } expected &&
                    packet.Sequence != expected)
                {
                    throw new InvalidDataException(
                        $"Expected PCP packet sequence {expected} but observed {packet.Sequence}.");
                }

                var recordingPacket = new PcpChannelPacket(
                    _channelId,
                    _sessionId,
                    _recordingSequence,
                    packet.Type,
                    packet.WirePosition,
                    packet.StreamPosition,
                    packet.Payload,
                    packet.Discontinuity);

                await _bridge.ConsumeAsync(
                    recordingPacket,
                    cancellationToken).ConfigureAwait(false);

                if (!packet.IsRetainedHeadReplay)
                {
                    _expectedSourceSequence = checked(packet.Sequence + 1);
                }

                _recordingSequence = checked(_recordingSequence + 1);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lock (_gate) { _deliveryFailed = true; }
                await CompleteStreamAsync(
                    exception,
                    stopChild: false).ConfigureAwait(false);
                throw;
            }
        }

        public void Attach(PcpRelayChildRegistration registration)
        {
            _registration = registration;
        }

        public void ObserveCompletion()
        {
            ObserveLateFailure(ObserveCompletionAsync(
                _registration ?? throw new InvalidOperationException(
                    "The fan-out registration has not been attached.")));
        }

        public Task CompleteFromOwnerAsync(Exception? terminalFault)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.CompletedTask;
            }

            if (terminalFault is null &&
                _registration?.Snapshot.PendingPacketCount > 0)
            {
                terminalFault = new IOException(
                    "The channel owner stopped before recording packets drained.");
            }

            return CompleteStreamAsync(
                terminalFault,
                stopChild: terminalFault is not null);
        }

        public ValueTask DisposeAsync()
        {
            TaskCompletionSource completionSource;

            lock (_gate)
            {
                if (_disposeTask is not null)
                {
                    return new ValueTask(_disposeTask);
                }

                Volatile.Write(ref _disposed, 1);
                _aborted |= _terminalTask is null;
                completionSource = _cleanupCompletion;
                _disposeTask = completionSource.Task;
            }

            _ = completionSource.Task.ContinueWith(_ => _owner.Remove(this),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = DisposeCoreAsync(completionSource);
            return new ValueTask(completionSource.Task);
        }

        private async Task ObserveCompletionAsync(
            PcpRelayChildRegistration registration)
        {
            try
            {
                await registration.Completion.ConfigureAwait(false);

                if (Volatile.Read(ref _disposed) == 0 &&
                    Volatile.Read(ref _terminalRequested) == 0)
                {
                    lock (_gate) { _deliveryFailed = true; }
                    await CompleteStreamAsync(
                        new IOException(
                            $"The recording fan-out child stopped unexpectedly ({registration.Snapshot.EndReason})."),
                        stopChild: false).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    await CompleteStreamAsync(
                        new IOException(
                            "The recording fan-out child failed.",
                            exception),
                        stopChild: false).ConfigureAwait(false);
                }
            }
        }

        private Task CompleteStreamAsync(
            Exception? terminalFault,
            bool stopChild)
        {
            TaskCompletionSource completionSource;

            lock (_gate)
            {
                if (_terminalTask is not null)
                {
                    return _terminalTask;
                }

                Volatile.Write(ref _terminalRequested, 1);
                _sourceFault = terminalFault is not null;
                completionSource = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _terminalTask = completionSource.Task;
            }

            _ = CompleteStreamCoreAsync(
                terminalFault,
                stopChild,
                completionSource);
            return completionSource.Task;
        }

        private async Task CompleteStreamCoreAsync(
            Exception? terminalFault,
            bool stopChild,
            TaskCompletionSource completionSource)
        {
            try
            {
                if (stopChild && _registration is { } registration)
                {
                    _ = await registration.StopAsync().ConfigureAwait(false);
                    await registration.Completion.ConfigureAwait(false);
                }

                await _bridge.CompleteAsync(
                    terminalFault).ConfigureAwait(false);
                lock (_gate)
                {
                    _eofNotified = terminalFault is null;
                    _faultNotified = terminalFault is not null;
                }
                completionSource.TrySetResult();
            }
            catch (ObjectDisposedException)
                when (Volatile.Read(ref _disposed) != 0)
            {
                lock (_gate) { _aborted = true; }
                completionSource.TrySetResult();
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        }

        private async Task DisposeCoreAsync(
            TaskCompletionSource completionSource)
        {
            List<Exception> failures = [];
            try
            {
                if (_registration is { } registration)
                {
                    try { _ = await registration.StopAsync().ConfigureAwait(false); }
                    catch (Exception exception)
                    {
                        lock (_gate) { _registrationCleanupFailed = true; }
                        failures.Add(exception);
                    }
                    // A failed stop request is not proof of actual termination.
                    await registration.Completion.ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                lock (_gate) { _registrationCleanupFailed = true; }
                if (!failures.Contains(exception)) failures.Add(exception);
            }

            Task? terminal;
            lock (_gate) { terminal = _terminalTask; }
            try { if (terminal is not null) await terminal.ConfigureAwait(false); }
            catch (Exception exception) { failures.Add(exception); }

            // These cleanup stages remain reachable even if registration or
            // terminal completion faulted. Each failure is retained independently.
            try
            {
                await _bridge.DisposeAsync().ConfigureAwait(false);
                if (_owner.CleanupObserver?.AfterBridgeCleanup is { } observe)
                    await observe().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lock (_gate) { _bridgeCleanupFailed = true; }
                failures.Add(exception);
            }

            if (_ownerLease is not null)
            {
                try { await _ownerLease.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception)
                {
                    lock (_gate) { _ownerCleanupFailed = true; }
                    failures.Add(exception);
                }
            }

            if (failures.Count == 0) completionSource.TrySetResult();
            else completionSource.TrySetException(failures.Count == 1 ? failures[0] :
                new AggregateException("The recording source failed to clean up.", failures));
        }
    }
}

// Internal deterministic observation boundaries; production supplies no observer.
// Fault injection occurs only after the corresponding actual operation ended.
internal sealed class PcpRecordingSourceCleanupObserver
{
    public Func<Task>? AfterRegistrationCompletion { get; init; }
    public Func<Task>? AfterBridgeCleanup { get; init; }
    public Action<CancellationToken>? WorkerTokenObserved { get; init; }
}
