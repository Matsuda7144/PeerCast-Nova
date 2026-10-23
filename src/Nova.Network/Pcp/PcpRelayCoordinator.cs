using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Owns one upstream channel session, its bounded relay fan-out, and one
/// downstream relay server. The caller owns the Core tracker and connector.
/// </summary>
/// <remarks>
/// When a recording source registry is injected, this coordinator owns only
/// its identity-bound registration token. The registry and shared fan-out
/// remain owned by their runtime composition; existing recording leases are
/// completed during owner shutdown.
/// </remarks>
public sealed class PcpRelayCoordinator : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly PcpRelayFanout _fanout;
    private readonly PcpRelayFanoutRecordingStreamSourceLeaseFactory
        _recordingSourceLeaseFactory;
    private readonly RecordingStreamSourceLeaseRegistry?
        _recordingSourceLeaseRegistry;
    private readonly RelayIngressConsumer _ingress;
    private readonly PcpChannelSession _upstreamSession;
    private readonly PcpDownstreamRelayServer _downstreamServer;
    private readonly ChannelStatusTracker _statusTracker;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly bool _stopWhenNoConsumers;
    private readonly HashSet<Guid> _consumerLeases = [];
    private readonly TaskCompletionSource<PcpRelayCoordinatorReadinessOutcome>
        _readinessSource = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstPacketAcceptedSource = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _drainTimeout;

    private Task<PcpRelayCoordinatorResult>? _runTask;
    private IDisposable? _recordingSourceRegistration;
    private PcpRelayCoordinatorState _state = PcpRelayCoordinatorState.Created;
    private bool _consumerLeaseAdmissionClosed;
    private int _stopRequested;
    private int _callerCanceled;
    private int _disposed;

    public PcpRelayCoordinator(
        TcpTransportConnector upstreamConnector,
        ChannelStatusTracker statusTracker,
        IPEndPoint downstreamEndPoint,
        PcpRelayCoordinatorOptions? options = null,
        RecordingStreamSourceLeaseRegistry? recordingSourceLeaseRegistry = null,
        bool stopWhenNoConsumers = false,
        NatPortMappingManager? portMapping = null,
        IPcpDownstreamRelayAdmission? admission = null)
    {
        ArgumentNullException.ThrowIfNull(upstreamConnector);
        ArgumentNullException.ThrowIfNull(statusTracker);
        ArgumentNullException.ThrowIfNull(downstreamEndPoint);
        if (admission is not null && admission.ChannelId != statusTracker.Snapshot.ChannelId)
            throw new ArgumentException("Admission must belong to this channel.", nameof(admission));

        var sourceOptions = options ?? new PcpRelayCoordinatorOptions();
        ArgumentNullException.ThrowIfNull(sourceOptions.UpstreamSession);
        ArgumentNullException.ThrowIfNull(sourceOptions.UpstreamSession.Stream);
        ArgumentNullException.ThrowIfNull(sourceOptions.DownstreamTransport);
        ArgumentNullException.ThrowIfNull(sourceOptions.Fanout);
        ArgumentNullException.ThrowIfNull(sourceOptions.DownstreamChild);

        var copiedOptions = sourceOptions with
        {
            UpstreamSession = sourceOptions.UpstreamSession with
            {
                Stream = sourceOptions.UpstreamSession.Stream with { },
            },
            DownstreamTransport = sourceOptions.DownstreamTransport with { },
            Fanout = sourceOptions.Fanout with { },
            DownstreamChild = sourceOptions.DownstreamChild with { },
        };
        copiedOptions.Validate();

        _drainTimeout = copiedOptions.DrainTimeout;
        _statusTracker = statusTracker;
        _stopWhenNoConsumers = stopWhenNoConsumers;
        _recordingSourceLeaseRegistry = recordingSourceLeaseRegistry;
        _fanout = new PcpRelayFanout(
            copiedOptions.Fanout,
            replayHeadForNewChildren: true);
        _recordingSourceLeaseFactory =
            new PcpRelayFanoutRecordingStreamSourceLeaseFactory(
                statusTracker.Snapshot.ChannelId,
                statusTracker.Snapshot.SessionId,
                _fanout,
                acquireOwnerLease: stopWhenNoConsumers
                    ? TryAcquireConsumerLease
                    : null);
        _ingress = new RelayIngressConsumer(_fanout, _firstPacketAcceptedSource);
        _upstreamSession = new PcpChannelSession(
            upstreamConnector,
            statusTracker,
            _ingress,
            copiedOptions.UpstreamSession);
        _downstreamServer = new PcpDownstreamRelayServer(
            downstreamEndPoint,
            _fanout,
            statusTracker.Snapshot.ChannelId,
            statusTracker.Snapshot.SessionId.Value,
            copiedOptions.DownstreamTransport,
            copiedOptions.DownstreamChild,
            portMapping,
            admission);
    }

    public PcpRelayCoordinatorState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets the number of application consumers that currently keep this
    /// owner alive when idle shutdown is enabled.
    /// </summary>
    public int ActiveConsumerCount
    {
        get
        {
            lock (_gate)
            {
                return _consumerLeases.Count;
            }
        }
    }

    public bool CanAcceptConsumers
    {
        get
        {
            lock (_gate)
            {
                return !_consumerLeaseAdmissionClosed &&
                    Volatile.Read(ref _disposed) == 0 &&
                    !_stopSource.IsCancellationRequested &&
                    _state is PcpRelayCoordinatorState.Created or
                        PcpRelayCoordinatorState.Running;
            }
        }
    }

    public Task<PcpRelayCoordinatorResult>? Completion
    {
        get
        {
            lock (_gate)
            {
                return _runTask;
            }
        }
    }

    public IPEndPoint LocalEndPoint => _downstreamServer.LocalEndPoint;

    public PcpDownstreamRelayServerSnapshot DownstreamSnapshot =>
        _downstreamServer.Snapshot;

    /// <summary>
    /// Non-owning identity for application admission registration. This
    /// coordinator retains exclusive fan-out disposal ownership.
    /// </summary>
    public PcpRelayFanout DownstreamFanout => _fanout;

    public PcpRelayIngressSnapshot IngressSnapshot => _ingress.Snapshot;

    /// <summary>
    /// Recording reader leases from this coordinator's existing upstream
    /// channel fan-out. The factory is available only while the owner runs.
    /// </summary>
    public IRecordingStreamSourceLeaseFactory RecordingStreamSourceLeaseFactory =>
        _recordingSourceLeaseFactory;

    internal RecordingStreamSourceLeaseRegistry? RecordingSourceLeaseRegistry =>
        _recordingSourceLeaseRegistry;

    /// <summary>
    /// Completes after the first upstream packet has been accepted by the
    /// bounded relay fan-out.
    /// </summary>
    public Task FirstPacketAccepted => _firstPacketAcceptedSource.Task;

    /// <summary>Existing upstream handshake observation, separate from listener bind and packet ingress.</summary>
    public Task<PcpChannelSessionNegotiationResult> UpstreamNegotiation => _upstreamSession.Negotiation;

    /// <summary>
    /// Attaches a playback packet consumer directly to this owner's bounded
    /// fan-out. The adapter restores this coordinator's channel/session identity
    /// and forwards callback-scoped payloads without opening another PCP link.
    /// </summary>
    public async ValueTask<PcpRelayConsumerRegistration?> AttachPlaybackConsumerAsync(
        IPcpChannelPacketConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ThrowIfDisposed();

        ConsumerLease? ownerLease = null;
        if (_stopWhenNoConsumers)
        {
            ownerLease = TryAcquireConsumerLease();
            if (ownerLease is null)
            {
                return null;
            }
        }

        PcpRelayChildRegistration? childRegistration = null;
        try
        {
            var identity = _statusTracker.Snapshot;
            var sink = new PlaybackPacketChildSink(
                identity.ChannelId,
                identity.SessionId,
                consumer);
            if (!_fanout.TryAddChild(sink, out childRegistration) ||
                childRegistration is null)
            {
                return null;
            }

            var registration = new PcpRelayConsumerRegistration(
                childRegistration,
                ownerLease);
            childRegistration = null;
            ownerLease = null;
            return registration;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            if (childRegistration is not null)
            {
                await childRegistration.DisposeAsync().ConfigureAwait(false);
            }

            if (ownerLease is not null)
            {
                await ownerLease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Waits until the downstream listener has bound successfully or the
    /// coordinator reaches a fixed failure/stopped outcome before readiness.
    /// Waiting cancellation does not stop the coordinator.
    /// </summary>
    public Task<PcpRelayCoordinatorReadinessOutcome> WaitForReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _readinessSource.Task.WaitAsync(cancellationToken);
    }

    public Task<PcpRelayCoordinatorResult> RunAsync(
        string upstreamHost,
        int upstreamPort,
        long initialStreamPosition = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamHost);

        if (upstreamPort is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(upstreamPort),
                upstreamPort,
                "The upstream TCP port is outside the valid range.");
        }

        if (initialStreamPosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialStreamPosition),
                initialStreamPosition,
                "The initial stream position cannot be negative.");
        }

        lock (_gate)
        {
            if (_runTask is not null)
            {
                throw new InvalidOperationException(
                    "A relay coordinator can run only once.");
            }

            if (_stopSource.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "A stopped relay coordinator cannot run.");
            }

            _state = PcpRelayCoordinatorState.Running;
            _runTask = RunCoreAsync(
                upstreamHost,
                upstreamPort,
                initialStreamPosition,
                cancellationToken);
            return _runTask;
        }
    }

    public void RequestStop()
    {
        ThrowIfDisposed();
        CloseConsumerLeaseAdmission();
        RemoveRecordingSourceRegistration();
        Interlocked.Exchange(ref _stopRequested, 1);
        _readinessSource.TrySetResult(
            PcpRelayCoordinatorReadinessOutcome.Stopped);
        SetStoppingState();
        CancelBestEffort(_stopSource);
        RequestOwnedStopBestEffort();
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        RequestStop();

        Task<PcpRelayCoordinatorResult>? runTask;

        lock (_gate)
        {
            runTask = _runTask;
        }

        if (runTask is null)
        {
            await StopUnstartedOwnedAsync(
                cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _state = PcpRelayCoordinatorState.Stopped;
            }

            return;
        }

        await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RemoveRecordingSourceRegistration();
        CloseConsumerLeaseAdmission();
        Interlocked.Exchange(ref _stopRequested, 1);
        _readinessSource.TrySetResult(
            PcpRelayCoordinatorReadinessOutcome.Stopped);
        SetStoppingState();
        CancelBestEffort(_stopSource);
        RequestOwnedStopBestEffort();

        Task<PcpRelayCoordinatorResult>? runTask;

        lock (_gate)
        {
            runTask = _runTask;
        }

        try
        {
            if (runTask is null)
            {
                await StopUnstartedOwnedAsync(
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await runTask.ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await _upstreamSession.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await _downstreamServer.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await _fanout.DisposeAsync().ConfigureAwait(false);
                    _stopSource.Dispose();

                    lock (_gate)
                    {
                        _state = PcpRelayCoordinatorState.Stopped;
                    }
                }
            }
        }
    }

    private async Task<PcpRelayCoordinatorResult> RunCoreAsync(
        string upstreamHost,
        int upstreamPort,
        long initialStreamPosition,
        CancellationToken callerCancellationToken)
    {
        using var cancellationRegistration =
            callerCancellationToken.Register(CancelFromCaller);

        Task<PcpChannelSessionResult>? upstreamTask = null;
        Task<PcpDownstreamRelayServerResult>? downstreamTask = null;
        PcpChannelSessionResult? upstreamResult = null;
        PcpDownstreamRelayServerResult? downstreamResult = null;
        var upstreamObserved = false;
        var downstreamObserved = false;
        var upstreamFailed = false;
        var downstreamFailed = false;
        var downstreamCompletedFirst = false;
        var internalFailure = false;
        var shutdownCompleted = true;
        var drainOutcome = PcpRelayDrainOutcome.NotRequired;

        try
        {
            if (!_stopSource.IsCancellationRequested)
            {
                try
                {
                    downstreamTask = _downstreamServer.RunAsync(
                        CancellationToken.None);
                }
                catch (Exception)
                {
                    downstreamFailed = true;
                    _readinessSource.TrySetResult(
                        PcpRelayCoordinatorReadinessOutcome.Failed);
                }
            }

            if (downstreamTask is not null &&
                !_stopSource.IsCancellationRequested)
            {
                try
                {
                    upstreamTask = _upstreamSession.RunAsync(
                        upstreamHost,
                        upstreamPort,
                        initialStreamPosition,
                        CancellationToken.None);

                    if (!_stopSource.IsCancellationRequested)
                    {
                        StartRecordingSourcePublication();
                        _readinessSource.TrySetResult(
                            PcpRelayCoordinatorReadinessOutcome.Ready);
                    }
                }
                catch (Exception)
                {
                    upstreamFailed = true;
                }
            }

            if (upstreamTask is not null && downstreamTask is not null)
            {
                var first = await Task.WhenAny(
                    upstreamTask,
                    downstreamTask).ConfigureAwait(false);

                if (ReferenceEquals(first, upstreamTask))
                {
                    RemoveRecordingSourceRegistration();
                    var observation = await ObserveUpstreamAsync(
                        upstreamTask).ConfigureAwait(false);
                    upstreamResult = observation.Result;
                    upstreamFailed = observation.Failed;
                    upstreamObserved = true;

                    if (!_stopSource.IsCancellationRequested)
                    {
                        try
                        {
                            drainOutcome = await _fanout.WaitForIdleAsync(
                                _drainTimeout,
                                _stopSource.Token).ConfigureAwait(false)
                                    ? PcpRelayDrainOutcome.Completed
                                    : PcpRelayDrainOutcome.TimedOut;
                        }
                        catch (OperationCanceledException)
                            when (_stopSource.IsCancellationRequested)
                        {
                            drainOutcome = PcpRelayDrainOutcome.Canceled;
                        }
                        catch (Exception)
                        {
                            internalFailure = true;
                        }
                    }

                    RequestDownstreamStopBestEffort();
                }
                else
                {
                    RemoveRecordingSourceRegistration();
                    downstreamCompletedFirst = true;
                    var observation = await ObserveDownstreamAsync(
                        downstreamTask).ConfigureAwait(false);
                    downstreamResult = observation.Result;
                    downstreamFailed = observation.Failed;
                    downstreamObserved = true;
                    RequestUpstreamStopBestEffort();
                }
            }
        }
        catch (Exception)
        {
            internalFailure = true;
        }
        finally
        {
            CloseConsumerLeaseAdmission();
            RemoveRecordingSourceRegistration();
            _readinessSource.TrySetResult(
                _stopSource.IsCancellationRequested
                    ? PcpRelayCoordinatorReadinessOutcome.Stopped
                    : PcpRelayCoordinatorReadinessOutcome.Failed);
            RequestOwnedStopBestEffort();

            if (upstreamTask is null)
            {
                try
                {
                    await _upstreamSession.StopAsync(
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    shutdownCompleted = false;
                }
            }
            else if (!upstreamObserved)
            {
                var observation = await ObserveUpstreamAsync(
                    upstreamTask).ConfigureAwait(false);
                upstreamResult = observation.Result;
                upstreamFailed |= observation.Failed;
            }

            if (downstreamTask is null)
            {
                try
                {
                    await _downstreamServer.StopAsync(
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    shutdownCompleted = false;
                }
            }
            else if (!downstreamObserved)
            {
                var observation = await ObserveDownstreamAsync(
                    downstreamTask).ConfigureAwait(false);
                downstreamResult = observation.Result;
                downstreamFailed |= observation.Failed;
            }

            var recordingDrainCompleted =
                drainOutcome == PcpRelayDrainOutcome.Completed;

            if (!recordingDrainCompleted)
            {
                try
                {
                    recordingDrainCompleted = await _fanout.WaitForIdleAsync(
                        _drainTimeout,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    recordingDrainCompleted = false;
                }
            }

            var recordingTerminalFault =
                !recordingDrainCompleted ||
                upstreamFailed ||
                downstreamFailed ||
                (downstreamCompletedFirst &&
                    Volatile.Read(ref _stopRequested) == 0 &&
                    Volatile.Read(ref _callerCanceled) == 0) ||
                internalFailure ||
                _statusTracker.Snapshot.Fault is not null
                    ? new IOException(
                        recordingDrainCompleted
                            ? "The channel owner ended with a terminal fault."
                            : "The channel owner stopped before recording packets drained.")
                    : null;

            try
            {
                await _recordingSourceLeaseFactory.CompleteAsync(
                    recordingTerminalFault).ConfigureAwait(false);
            }
            catch (Exception)
            {
                shutdownCompleted = false;
            }

            try
            {
                await _fanout.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                shutdownCompleted = false;
            }

            if (downstreamResult?.Snapshot.CleanupIncompleteCount > 0)
            {
                shutdownCompleted = false;
            }

            lock (_gate)
            {
                _state = PcpRelayCoordinatorState.Stopped;
            }
        }

        var endReason = DetermineEndReason(
            upstreamResult,
            downstreamResult,
            upstreamFailed,
            downstreamFailed,
            downstreamCompletedFirst,
            internalFailure);

        return new PcpRelayCoordinatorResult(
            endReason,
            drainOutcome,
            _ingress.Snapshot,
            upstreamResult,
            downstreamResult,
            shutdownCompleted);
    }

    private PcpRelayCoordinatorEndReason DetermineEndReason(
        PcpChannelSessionResult? upstreamResult,
        PcpDownstreamRelayServerResult? downstreamResult,
        bool upstreamFailed,
        bool downstreamFailed,
        bool downstreamCompletedFirst,
        bool internalFailure)
    {
        if (Volatile.Read(ref _callerCanceled) != 0)
        {
            return PcpRelayCoordinatorEndReason.CallerCanceled;
        }

        if (Volatile.Read(ref _stopRequested) != 0)
        {
            return PcpRelayCoordinatorEndReason.Stopped;
        }

        if (internalFailure)
        {
            return PcpRelayCoordinatorEndReason.InternalFailure;
        }

        if (downstreamFailed ||
            downstreamCompletedFirst ||
            downstreamResult?.EndReason ==
                PcpDownstreamRelayServerEndReason.AcceptFailed ||
            downstreamResult?.Snapshot.InternalFailureCount > 0)
        {
            return PcpRelayCoordinatorEndReason.DownstreamFailed;
        }

        if (upstreamFailed || upstreamResult is null)
        {
            return PcpRelayCoordinatorEndReason.UpstreamFailed;
        }

        return PcpRelayCoordinatorEndReason.UpstreamEnded;
    }

    private void CancelFromCaller()
    {
        CloseConsumerLeaseAdmission();
        RemoveRecordingSourceRegistration();
        Interlocked.Exchange(ref _callerCanceled, 1);
        _readinessSource.TrySetResult(
            PcpRelayCoordinatorReadinessOutcome.Stopped);
        SetStoppingState();
        CancelBestEffort(_stopSource);
        RequestOwnedStopBestEffort();
    }

    private void StartRecordingSourcePublication()
    {
        lock (_gate)
        {
            _recordingSourceLeaseFactory.StartAccepting();

            if (_recordingSourceLeaseRegistry is null)
            {
                return;
            }

            try
            {
                _recordingSourceRegistration =
                    _recordingSourceLeaseRegistry.Register(
                        _statusTracker.Snapshot.ChannelId,
                        _recordingSourceLeaseFactory);
            }
            catch (InvalidOperationException)
            {
                // Recording is optional; registry capacity must not stop the
                // existing PCP relay owner.
                _recordingSourceLeaseFactory.StopAccepting();
            }
        }
    }

    private void RemoveRecordingSourceRegistration()
    {
        lock (_gate)
        {
            _recordingSourceLeaseFactory.StopAccepting();
            var registration = _recordingSourceRegistration;
            _recordingSourceRegistration = null;
            registration?.Dispose();
        }
    }

    private void SetStoppingState()
    {
        lock (_gate)
        {
            if (_state == PcpRelayCoordinatorState.Running)
            {
                _state = PcpRelayCoordinatorState.Stopping;
            }
        }
    }

    private void RequestOwnedStopBestEffort()
    {
        RequestUpstreamStopBestEffort();
        RequestDownstreamStopBestEffort();
    }

    private void RequestUpstreamStopBestEffort()
    {
        try
        {
            _upstreamSession.RequestStop();
        }
        catch (ObjectDisposedException)
        {
            // Coordinator disposal already owns the completed shutdown.
        }
    }

    private void RequestDownstreamStopBestEffort()
    {
        try
        {
            _downstreamServer.RequestStop();
        }
        catch (ObjectDisposedException)
        {
            // Coordinator disposal already owns the completed shutdown.
        }
    }

    private async Task StopUnstartedOwnedAsync(
        CancellationToken cancellationToken)
    {
        await _upstreamSession.StopAsync(
            cancellationToken).ConfigureAwait(false);
        await _downstreamServer.StopAsync(
            cancellationToken).ConfigureAwait(false);
        await _fanout.DisposeAsync().AsTask().WaitAsync(
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UpstreamObservation> ObserveUpstreamAsync(
        Task<PcpChannelSessionResult> task)
    {
        try
        {
            return new UpstreamObservation(
                await task.ConfigureAwait(false),
                Failed: false);
        }
        catch (Exception)
        {
            return new UpstreamObservation(Result: null, Failed: true);
        }
    }

    private static async Task<DownstreamObservation> ObserveDownstreamAsync(
        Task<PcpDownstreamRelayServerResult> task)
    {
        try
        {
            return new DownstreamObservation(
                await task.ConfigureAwait(false),
                Failed: false);
        }
        catch (Exception)
        {
            return new DownstreamObservation(Result: null, Failed: true);
        }
    }

    private static void CancelBestEffort(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // A cancellation callback cannot change coordinator ownership.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }

    private sealed record UpstreamObservation(
        PcpChannelSessionResult? Result,
        bool Failed);

    private sealed record DownstreamObservation(
        PcpDownstreamRelayServerResult? Result,
        bool Failed);

    private sealed class RelayIngressConsumer : IPcpChannelPacketConsumer
    {
        private readonly object _gate = new();
        private readonly IPcpChannelPacketConsumer _inner;
        private readonly TaskCompletionSource _firstPacketAccepted;
        private long _acceptedPacketCount;
        private long _acceptedPayloadByteCount;

        public RelayIngressConsumer(
            IPcpChannelPacketConsumer inner,
            TaskCompletionSource firstPacketAccepted)
        {
            _inner = inner;
            _firstPacketAccepted = firstPacketAccepted;
        }

        public PcpRelayIngressSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    return new PcpRelayIngressSnapshot(
                        _acceptedPacketCount,
                        _acceptedPayloadByteCount);
                }
            }
        }

        public async ValueTask ConsumeAsync(
            PcpChannelPacket packet,
            CancellationToken cancellationToken)
        {
            await _inner.ConsumeAsync(
                packet,
                cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _acceptedPacketCount = checked(
                    _acceptedPacketCount + 1);
                _acceptedPayloadByteCount = checked(
                    _acceptedPayloadByteCount + packet.Payload.Length);
            }

            _firstPacketAccepted.TrySetResult();
        }
    }

    private ConsumerLease? TryAcquireConsumerLease()
    {
        lock (_gate)
        {
            if (_consumerLeaseAdmissionClosed ||
                Volatile.Read(ref _disposed) != 0 ||
                _stopSource.IsCancellationRequested ||
                _state is not (PcpRelayCoordinatorState.Created or
                    PcpRelayCoordinatorState.Running))
            {
                return null;
            }

            var leaseId = Guid.NewGuid();
            _consumerLeases.Add(leaseId);
            return new ConsumerLease(this, leaseId);
        }
    }

    /// <summary>
    /// Acquires a temporary application owner lease when this coordinator is
    /// configured to stop after its consumers detach. Returns <see langword="null"/>
    /// when the owner is stopping or does not use consumer-counted lifetime.
    /// </summary>
    public IAsyncDisposable? TryAcquireApplicationConsumerLease() =>
        _stopWhenNoConsumers
            ? TryAcquireConsumerLease()
            : null;

    private async ValueTask ReleaseConsumerLeaseAsync(Guid leaseId)
    {
        var stopWhenIdle = false;
        lock (_gate)
        {
            if (!_consumerLeases.Remove(leaseId))
            {
                return;
            }

            if (_stopWhenNoConsumers && _consumerLeases.Count == 0 &&
                !_consumerLeaseAdmissionClosed)
            {
                _consumerLeaseAdmissionClosed = true;
                stopWhenIdle = true;
            }
        }

        if (stopWhenIdle)
        {
            try
            {
                RequestStop();
            }
            catch (ObjectDisposedException)
            {
                // Runtime disposal already owns the final shutdown path.
            }

            Task<PcpRelayCoordinatorResult>? runTask;
            lock (_gate)
            {
                runTask = _runTask;
            }

            if (runTask is not null)
            {
                await runTask.ConfigureAwait(false);
            }
            else if (Volatile.Read(ref _disposed) == 0)
            {
                await StopAsync().ConfigureAwait(false);
            }
        }
    }

    private void CloseConsumerLeaseAdmission()
    {
        lock (_gate)
        {
            _consumerLeaseAdmissionClosed = true;
        }
    }

    private sealed class ConsumerLease(
        PcpRelayCoordinator owner,
        Guid leaseId) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private Task? _disposeTask;

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                _disposeTask ??= owner.ReleaseConsumerLeaseAsync(leaseId).AsTask();
                return new ValueTask(_disposeTask);
            }
        }
    }

    private sealed class PlaybackPacketChildSink(
        ChannelId channelId,
        ChannelSessionId sessionId,
        IPcpChannelPacketConsumer consumer) : IPcpRelayChildSink
    {
        public ValueTask SendAsync(
            PcpRelayPacket packet,
            CancellationToken cancellationToken) =>
            consumer.ConsumeAsync(
                new PcpChannelPacket(
                    channelId,
                    sessionId,
                    packet.Sequence,
                    packet.Type,
                    packet.WirePosition,
                    packet.StreamPosition,
                    packet.Payload,
                    packet.Discontinuity),
                cancellationToken);
    }
}
