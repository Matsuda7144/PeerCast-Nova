using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Owns one bounded TCP listener and all accepted downstream PCP child
/// sessions. The upstream session and relay fan-out remain caller-owned and
/// must outlive this server.
/// </summary>
public sealed partial class PcpDownstreamRelayServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly TcpTransportListener _listener;
    private readonly NatPortMappingManager? _portMapping;
    private IDisposable? _publicMappingOwner;
    private readonly PcpRelayFanout _fanout;
    private readonly Func<IPAddress?, bool>? _acceptRemoteAddress;
    private readonly PcpWanIngressPolicy? _wanIngress;
    private readonly ChannelId _channelId;
    private readonly Guid _localSessionId;
    private readonly PcpDownstreamChildOptions _childOptions;
    private readonly IPcpDownstreamRelayAdmission? _admission;
    private readonly PcpDownstreamTrafficMeter _traffic = new();
    private long _pacingLimitedCount;
    private PcpDownstreamTrafficMeter TrafficScope => (_admission as IPcpDownstreamRelayTraffic)?.Traffic ?? _traffic;
    private readonly ReadOnlyMemory<byte> _initialMetadata;
    private readonly Func<ReadOnlyMemory<byte>>? _initialMetadataProvider;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly CancellationTokenSource _acceptStop = new();
    private readonly PcpRelayChildAdmission _childAdmission = new();
    private readonly TaskCompletionSource<bool> _acceptClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _closeAdmissionTask;
    private Task<PcpRelayQuiescenceResult>? _drainTask;
    private Task? _disposal;
    private CancellationToken _callerCancellation;
    private int _forcedStopRequested;
    private readonly HashSet<Task<bool>> _pendingAdmissionCleanup = [];
    private long _admissionCleanupFailedCount;
    private long _admissionCloseFailedCount;
    private readonly HashSet<SessionEntry> _sessions = [];

    private Task<PcpDownstreamRelayServerResult>? _runTask;
    private IPEndPoint? _boundEndPoint;
    private PcpDownstreamRelayServerState _state =
        PcpDownstreamRelayServerState.Created;
    private PcpDownstreamRelayServerEndReason? _endReason;
    private long _acceptedConnectionCount;
    private int _activeConnectionCount;
    private long _completedConnectionCount;
    private long _acceptedHandshakeCount;
    private long _relayFullCount;
    private long _protocolRejectedCount;
    private long _handshakeTimedOutCount;
    private long _quitReceivedCount;
    private long _connectionEndedCount;
    private long _fanoutStoppedCount;
    private long _slowConsumerCount;
    private long _writeTimedOutCount;
    private long _writeFailedCount;
    private long _stoppedCount;
    private long _cleanupIncompleteCount;
    private long _deliveredPacketCount;
    private long _deliveredByteCount;
    private long _discardedPacketCount;
    private long _discardedByteCount;
    private long _internalFailureCount;
    private int _disposed;

    private long _admissionClosedCount;
    internal Func<TcpTransportConnection, IPcpDownstreamPacketWriter?>? PacketWriterFactory { get; init; }
    internal PcpRelayQuiescenceObserver? QuiescenceObserver { get; init; }
    internal PcpPeerDirectory Peers { get; }

    internal async Task WaitForRecoveryCleanupAsync(TimeSpan timeout, TimeProvider clock, CancellationToken token)
    {
        Task[] sessions;
        lock (_gate) sessions = _sessions.Select(e => e.Task ?? Task.CompletedTask).ToArray();
        await Task.WhenAll(sessions).WaitAsync(timeout, clock, token).ConfigureAwait(false);
        await WaitForAdmissionCleanupAsync(token).WaitAsync(timeout, clock, token).ConfigureAwait(false);
        var snapshot = Snapshot;
        if (snapshot.CleanupIncompleteCount != 0 || snapshot.InternalFailureCount != 0 ||
            snapshot.AdmissionCleanupFailedCount != 0)
            throw new IOException("Downstream transport/admission cleanup could not be confirmed.");
    }

    internal (bool CanAccept, int RelayCount) ReadRuntimeCapacity()
    {
        lock (_gate) return (_state == PcpDownstreamRelayServerState.Running && !_childAdmission.IsClosed &&
            !_callerCancellation.IsCancellationRequested && !_stopSource.IsCancellationRequested &&
            _activeConnectionCount < _listener.MaxConcurrentConnections && _pendingAdmissionCleanup.Count == 0 &&
            _admissionCleanupFailedCount == 0 && _admissionCloseFailedCount == 0,
            _sessions.Count(entry => entry.Session?.IsEstablishedRelay == true));
    }
    public PcpDownstreamRelayServer(
        IPEndPoint localEndPoint,
        PcpRelayFanout fanout,
        ChannelId channelId,
        Guid localSessionId,
        TcpTransportOptions? transportOptions = null,
        PcpDownstreamChildOptions? childOptions = null,
        NatPortMappingManager? portMapping = null,
        IPcpDownstreamRelayAdmission? admission = null,
        ChannelMetadataSnapshot? initialMetadata = null,
        Func<IPAddress?, bool>? acceptRemoteAddress = null,
        PcpWanIngressOptions? wanOptions = null,
        Func<ReadOnlyMemory<byte>>? initialMetadataProvider = null)
    {
        ArgumentNullException.ThrowIfNull(localEndPoint);
        ArgumentNullException.ThrowIfNull(fanout);
        ArgumentNullException.ThrowIfNull(channelId);
        if (admission is not null && admission.ChannelId != channelId)
            throw new ArgumentException("Admission must belong to this channel.", nameof(admission));

        if (localSessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The local session identifier cannot be empty.",
                nameof(localSessionId));
        }

        var copiedTransportOptions = transportOptions is null
            ? new TcpTransportOptions()
            : transportOptions with { };
        copiedTransportOptions.Validate();

        _childOptions = childOptions is null
            ? new PcpDownstreamChildOptions()
            : childOptions with { };
        _childOptions.Validate();
        _initialMetadata = initialMetadata is null ? default : PcpInitialChannelMetadata.Encode(channelId.Value, initialMetadata);
        _initialMetadataProvider = initialMetadataProvider;

        _listener = new TcpTransportListener(
            localEndPoint,
            copiedTransportOptions);
        _portMapping = portMapping;
        _fanout = fanout;
        _channelId = channelId;
        _localSessionId = localSessionId;
        _admission = admission;
        Peers = new PcpPeerDirectory(channelId.Value, localSessionId);
        _acceptRemoteAddress = acceptRemoteAddress;
        if (wanOptions is not null)
        {
            wanOptions.Validate();
            if (admission is null || portMapping is not null) throw new ArgumentException("WAN requires explicit admission without NAT mapping.", nameof(admission));
            _wanIngress = new(wanOptions);
        }
    }

    public IPEndPoint LocalEndPoint
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref _disposed) != 0,
                    this);

                return _boundEndPoint ??
                    throw new InvalidOperationException(
                        "The downstream relay server has not started.");
            }
        }
    }

    public PcpDownstreamRelayServerSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshotLocked();
            }
        }
    }

    public Task<PcpDownstreamRelayServerResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);

            if (_runTask is not null)
            {
                throw new InvalidOperationException(
                    "A downstream relay server can run only once.");
            }

            if (_stopSource.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "A stopped downstream relay server cannot run.");
            }

            _listener.Start();
            ObserveBoundChannelPath();
            _boundEndPoint = _listener.LocalEndPoint;
            Peers.SetLocalEndpoint(new(_boundEndPoint.Address, _boundEndPoint.Port));
            if (_portMapping is not null &&
                _boundEndPoint.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(_boundEndPoint.Address))
            {
                _publicMappingOwner = _portMapping.AcquirePublicTcpListener(_boundEndPoint);
            }

            _state = PcpDownstreamRelayServerState.Running;
            _callerCancellation = cancellationToken;
            _runTask = RunCoreAsync(cancellationToken);
            return _runTask;
        }
    }

    /// <summary>Close TCP and child registration admission without cancelling existing sessions.</summary>
    public Task StopAcceptingAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_runTask is null) throw new InvalidOperationException("The relay server has not started.");
            _childAdmission.Close();
            Peers.Dispose();
            _wanIngress?.Close();
            return _closeAdmissionTask ??= Task.Run(WaitForAdmissionClosedAsync);
        }
    }

    private async Task WaitForAdmissionClosedAsync()
    {
        try
        {
            await _acceptStop.CancelAsync().ConfigureAwait(false);
            try
            {
                if (QuiescenceObserver is not null)
                    await QuiescenceObserver.ObserveAsync(PcpRelayQuiescencePoint.BeforeListenerStop).ConfigureAwait(false);
            }
            finally { await _listener.StopAsync().ConfigureAwait(false); }
            if (!await _acceptClosed.Task.ConfigureAwait(false))
                throw new IOException("Relay admission closure failed.");
        }
        catch
        {
            lock (_gate) { _admissionCloseFailedCount = 1; }
            throw;
        }
    }

    /// <summary>
    /// Caller must first prove upstream consumption has ended. Close admission,
    /// drain only this server's children, then clean owned sessions. The first
    /// request owns one overall deadline/token; duplicate calls share its result.
    /// Timeout/cancellation never force-cancel a still-pending send.
    /// </summary>
    public Task<PcpRelayQuiescenceResult> DrainAfterUpstreamEndAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_runTask is null) throw new InvalidOperationException("The relay server has not started.");
            if (_drainTask is not null) return _drainTask;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(new PcpRelayQuiescenceResult(PcpRelayQuiescenceOutcome.Cancelled, false, false, Snapshot));
            var deadline = new CancellationTokenSource(timeout);
            _drainTask = DrainCoreAsync(deadline, cancellationToken);
            return _drainTask;
        }
    }

    private async Task<PcpRelayQuiescenceResult> DrainCoreAsync(
        CancellationTokenSource deadline, CancellationToken cancellationToken)
    {
        // Defer work so the cached task is published before completion/reentry.
        await Task.Yield();
        using (deadline)
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken))
        {
            var drained = false;
            var cleaning = false;
            try
            {
                await StopAcceptingAsync().WaitAsync(wait.Token).ConfigureAwait(false);
                if (ForcedStopRequested) return ForcedStopResult();
                var scope = _childAdmission.CaptureClosed();
                using var sends = CancellationTokenSource.CreateLinkedTokenSource(wait.Token, _stopSource.Token, _callerCancellation);
                if (!await PcpRelayFanout.WaitForIdleAsync(scope.Children, TimeSpan.FromMinutes(1), sends.Token).ConfigureAwait(false))
                    return Result(PcpRelayQuiescenceOutcome.TimedOut, false, false);
                var failed = scope.DeliveryFailed || scope.Children.Any(child => PcpRelayChildAdmission.DeliveryFailed(child.Snapshot));
                if (ForcedStopRequested) return ForcedStopResult();
                wait.Token.ThrowIfCancellationRequested();
                if (QuiescenceObserver is not null)
                    await QuiescenceObserver.ObserveAsync(PcpRelayQuiescencePoint.AfterInitialFailureEvaluation).AsTask().WaitAsync(wait.Token).ConfigureAwait(false);
                drained = !failed;
                // Only after all known pending/in-flight sends have settled.
                cleaning = true;
                await _stopSource.CancelAsync().WaitAsync(wait.Token).ConfigureAwait(false);
                var final = await _runTask!.WaitAsync(wait.Token).ConfigureAwait(false);
                await WaitForAdmissionCleanupAsync(wait.Token).ConfigureAwait(false);
                // Fixed registrations and aggregate pruned failures are both re-evaluated after cleanup.
                failed |= _childAdmission.CaptureClosed().DeliveryFailed ||
                    scope.Children.Any(child => PcpRelayChildAdmission.DeliveryFailed(child.Snapshot));
                wait.Token.ThrowIfCancellationRequested();
                var snapshot = Snapshot;
                var clean = snapshot.ActiveConnectionCount == 0 && snapshot.CleanupIncompleteCount == 0 && snapshot.InternalFailureCount == 0 && snapshot.AdmissionCleanupPendingCount == 0 && snapshot.AdmissionCleanupFailedCount == 0;
                failed |= snapshot.DiscardedPacketCount != 0 || snapshot.WriteFailedCount != 0 || snapshot.WriteTimedOutCount != 0 || snapshot.SlowConsumerCount != 0 || snapshot.FanoutStoppedCount != 0;
                drained &= !failed;
                if (ForcedStopRequested) return ForcedStopResult();
                if (final.EndReason == PcpDownstreamRelayServerEndReason.AcceptFailed || snapshot.AdmissionCloseFailedCount != 0)
                    return new(clean ? PcpRelayQuiescenceOutcome.Failed : PcpRelayQuiescenceOutcome.CleanupFailed, false, clean, snapshot);
                return new(ClassifyQuiescence(clean, ForcedStopRequested, failed), drained, clean, snapshot);
            }
            catch (OperationCanceledException)
            {
                if (ForcedStopRequested) return ForcedStopResult();
                var snapshot = Snapshot;
                return Result(snapshot.AdmissionCleanupFailedCount != 0 || snapshot.CleanupIncompleteCount != 0 || snapshot.InternalFailureCount != 0
                    ? PcpRelayQuiescenceOutcome.CleanupFailed : cancellationToken.IsCancellationRequested
                    ? PcpRelayQuiescenceOutcome.Cancelled : PcpRelayQuiescenceOutcome.TimedOut, drained, false);
            }
            catch (ObjectDisposedException) when (ForcedStopRequested)
            {
                return ForcedStopResult();
            }
            catch (Exception)
            {
                return Result(cleaning ? PcpRelayQuiescenceOutcome.CleanupFailed : PcpRelayQuiescenceOutcome.Failed, drained, false);
            }
        }
    }

    private PcpRelayQuiescenceResult Result(PcpRelayQuiescenceOutcome outcome, bool drained, bool cleaned) =>
        new(outcome, drained, cleaned, Snapshot);

    private bool ForcedStopRequested => Volatile.Read(ref _forcedStopRequested) != 0 || _callerCancellation.IsCancellationRequested;

    private PcpRelayQuiescenceResult ForcedStopResult()
    {
        var snapshot = Snapshot;
        var cleanupFailed = snapshot.CleanupIncompleteCount != 0 || snapshot.InternalFailureCount != 0 || snapshot.AdmissionCleanupFailedCount != 0;
        return new(cleanupFailed ? PcpRelayQuiescenceOutcome.CleanupFailed : PcpRelayQuiescenceOutcome.Cancelled,
            false, false, snapshot);
    }

    internal static PcpRelayQuiescenceOutcome ClassifyQuiescence(bool cleanupCompleted, bool cancelled, bool deliveryFailed) =>
        !cleanupCompleted ? PcpRelayQuiescenceOutcome.CleanupFailed : cancelled ? PcpRelayQuiescenceOutcome.Cancelled :
        deliveryFailed ? PcpRelayQuiescenceOutcome.DeliveryFailed : PcpRelayQuiescenceOutcome.Drained;

    internal void TrackAdmissionCleanup(Task<bool> cleanup)
    {
        lock (_gate)
        {
            PruneAdmissionCleanupLocked();
            _pendingAdmissionCleanup.Add(cleanup);
            PruneAdmissionCleanupLocked();
        }
        // Observe late faults and prune successful histories even if no subsequent
        // connection or status read occurs after a timed-out drain.
        _ = cleanup.ContinueWith(_ =>
        {
            lock (_gate) { PruneAdmissionCleanupLocked(); }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void PruneAdmissionCleanupLocked()
    {
        foreach (var task in _pendingAdmissionCleanup.Where(task => task.IsCompleted).ToArray())
        {
            if (!task.IsCompletedSuccessfully || !task.Result)
            {
                _ = task.Exception;
                _admissionCleanupFailedCount = checked(_admissionCleanupFailedCount + 1);
            }
            _pendingAdmissionCleanup.Remove(task);
        }
    }

    private async Task WaitForAdmissionCleanupAsync(CancellationToken cancellationToken)
    {
        Task<bool>[] pending;
        lock (_gate)
        {
            PruneAdmissionCleanupLocked();
            pending = [.. _pendingAdmissionCleanup];
        }
        foreach (var task in pending)
        {
            try { await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { /* The aggregate failure evidence owns classification. */ }
        }
        lock (_gate) { PruneAdmissionCleanupLocked(); }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        _wanIngress?.Close();
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        Task<PcpDownstreamRelayServerResult>? runTask;

        lock (_gate)
        {
            runTask = _runTask;

            if (runTask is null)
            {
                _state = PcpDownstreamRelayServerState.Stopped;
                _endReason = PcpDownstreamRelayServerEndReason.Stopped;
            }
        }

        Interlocked.Exchange(ref _forcedStopRequested, 1);
        CancelBestEffort(_stopSource);

        if (runTask is null)
        {
            return;
        }

        await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests server shutdown without waiting for the accept loop or child
    /// sessions. The caller can later await <see cref="StopAsync"/> or the task
    /// returned by <see cref="RunAsync"/>.
    /// </summary>
    public void RequestStop()
    {
        _wanIngress?.Close();
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        Interlocked.Exchange(ref _forcedStopRequested, 1);
        Peers.Dispose();
        CancelBestEffort(_stopSource);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _wanIngress?.Close();
            Interlocked.Exchange(ref _disposed, 1);
            Peers.Dispose();
            Interlocked.Exchange(ref _forcedStopRequested, 1);
            _disposal = Task.Run(DisposeCoreAsync);
            return new(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task<PcpDownstreamRelayServerResult>? runTask;

        CancelBestEffort(_stopSource);

        lock (_gate)
        {
            runTask = _runTask;

            if (runTask is null)
            {
                _state = PcpDownstreamRelayServerState.Stopped;
                _endReason = PcpDownstreamRelayServerEndReason.Stopped;
            }
        }

        try
        {
            if (runTask is not null)
            {
                await runTask.ConfigureAwait(false);
            }
        }
        finally
        {
            Task? closure;
            lock (_gate) { closure = _closeAdmissionTask; }
            if (closure is not null)
            {
                try { await closure.ConfigureAwait(false); }
                catch { /* Closure failure remains in the fixed snapshot counter. */ }
            }
            await _listener.DisposeAsync().ConfigureAwait(false);
            _stopSource.Dispose();
            _acceptStop.Dispose();
        }
    }

    private async Task<PcpDownstreamRelayServerResult> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        using var operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopSource.Token);
        var operationToken = operationSource.Token;
        using var accepts = CancellationTokenSource.CreateLinkedTokenSource(operationToken, _acceptStop.Token);
        var endReason = PcpDownstreamRelayServerEndReason.Stopped;

        try
        {
            while (true)
            {
                if (QuiescenceObserver is not null)
                    await QuiescenceObserver.ObserveAsync(PcpRelayQuiescencePoint.BeforeAccept).ConfigureAwait(false);
                var connection = await _listener.AcceptAsync(accepts.Token).ConfigureAwait(false);
                if (!StartSession(connection, operationToken))
                    await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (_acceptStop.IsCancellationRequested && !operationSource.IsCancellationRequested)
        {
            endReason = PcpDownstreamRelayServerEndReason.Quiesced;
        }
        catch (OperationCanceledException)
            when (operationSource.IsCancellationRequested)
        {
            endReason = cancellationToken.IsCancellationRequested &&
                !_stopSource.IsCancellationRequested
                    ? PcpDownstreamRelayServerEndReason.CallerCanceled
                    : PcpDownstreamRelayServerEndReason.Stopped;
        }
        catch (SocketException)
        {
            endReason = PcpDownstreamRelayServerEndReason.AcceptFailed;
        }
        catch (IOException)
        {
            endReason = PcpDownstreamRelayServerEndReason.AcceptFailed;
        }
        catch (InvalidOperationException)
            when (_acceptStop.IsCancellationRequested && !operationSource.IsCancellationRequested)
        {
            endReason = PcpDownstreamRelayServerEndReason.Quiesced;
        }
        catch (InvalidOperationException)
        {
            endReason = PcpDownstreamRelayServerEndReason.AcceptFailed;
        }
        finally
        {
            _wanIngress?.Close();
            Peers.Dispose();
            var closed = false;
            try
            {
                await _listener.StopAsync().ConfigureAwait(false);
                Interlocked.Exchange(ref _publicMappingOwner, null)?.Dispose();
                closed = true;
            }
            catch
            {
                endReason = PcpDownstreamRelayServerEndReason.AcceptFailed;
                lock (_gate) { _admissionCloseFailedCount = 1; }
            }
            finally { _acceptClosed.TrySetResult(closed && endReason != PcpDownstreamRelayServerEndReason.AcceptFailed); }
            if (closed && endReason == PcpDownstreamRelayServerEndReason.Quiesced && !operationSource.IsCancellationRequested)
            {
                lock (_gate) { _state = PcpDownstreamRelayServerState.Quiescent; }
                try { await Task.Delay(Timeout.InfiniteTimeSpan, operationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (operationSource.IsCancellationRequested) { }
            }
            lock (_gate)
            {
                _state = PcpDownstreamRelayServerState.Stopping;
            }

            CancelBestEffort(operationSource);
            await _listener.StopAsync().ConfigureAwait(false);
            Interlocked.Exchange(ref _publicMappingOwner, null)?.Dispose();
            await WaitForSessionsAsync().ConfigureAwait(false);

            lock (_gate)
            {
                _state = PcpDownstreamRelayServerState.Stopped;
                _endReason = endReason;
            }
        }

        var snapshot = Snapshot;
        return new PcpDownstreamRelayServerResult(endReason, snapshot);
    }

    private bool StartSession(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        if (_wanIngress is not null && cancellationToken.IsCancellationRequested) return false;
        // Gate before any HTTP/PCP read, reply or metadata disclosure.
        if (_acceptRemoteAddress is not null)
        {
            try { if (!_acceptRemoteAddress(connection.RemoteEndPoint?.Address)) return false; }
            catch { return false; }
        }
        var lease = _wanIngress?.TryAccept(connection.RemoteEndPoint?.Address);
        if (_wanIngress is not null && lease is null) return false;
        var entry = new SessionEntry(connection) { WanLease = lease };

        lock (_gate)
        {
            if (_childAdmission.IsClosed)
            {
                if (lease is not null) TrackAdmissionCleanup(ReleaseWanAfterCleanupAsync(lease, connection.CleanupCompletion));
                return false;
            }
            _sessions.Add(entry);
            _acceptedConnectionCount = checked(
                _acceptedConnectionCount + 1);
            _activeConnectionCount = checked(
                _activeConnectionCount + 1);
            // Never run external policy callbacks while holding the server lock.
            entry.Task = Task.Run(() => RunSessionAsync(entry, cancellationToken), CancellationToken.None);
            return true;
        }
    }

    private async Task RunSessionAsync(
        SessionEntry entry,
        CancellationToken cancellationToken)
    {
        PcpDownstreamChildResult? result = null;
        var internalFailure = false;
        PcpDownstreamChildSession? session = null;

        try
        {
            session = new PcpDownstreamChildSession(
                entry.Connection,
                _fanout,
                _channelId,
                _localSessionId,
                _childOptions,
                packetWriter: PacketWriterFactory?.Invoke(entry.Connection),
                admission: _admission,
                childAdmission: _childAdmission,
                quiescenceObserver: QuiescenceObserver,
                initialMetadata: ResolveInitialMetadata(),
                wanIngress: entry.WanLease,
                peers: Peers,
                traffic: TrafficScope);
            lock (_gate) entry.Session = session;
            result = await session.RunAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            internalFailure = true;

            try
            {
                await entry.Connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The fixed internal-failure counter is the terminal signal.
            }
        }
        finally
        {
            try
            {
                if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            }
            catch { internalFailure = true; }
            if (session is not null) TrackAdmissionCleanup(session.AdmissionCleanupCompletion);
            if (entry.WanLease is { } lease) TrackAdmissionCleanup(ReleaseWanAfterCleanupAsync(lease, entry.Connection.CleanupCompletion,
                session?.AdmissionCleanupCompletion));
            CompleteSessionEntry(entry, result, internalFailure);
        }
    }

    private async Task WaitForSessionsAsync()
    {
        Task[] tasks;

        lock (_gate)
        {
            tasks = _sessions
                .Select(entry => entry.Task ?? Task.CompletedTask)
                .ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<bool> ReleaseWanAfterCleanupAsync(PcpWanIngressPolicy.Lease lease, Task transportCleanup, Task<bool>? workerCleanup = null)
    {
        try
        {
            await transportCleanup.ConfigureAwait(false);
            if (workerCleanup is not null && !await workerCleanup.ConfigureAwait(false)) return false;
            lease.Dispose(); return true;
        }
        catch { return false; }
    }

    private void RecordResultLocked(PcpDownstreamChildResult result)
    {
        if (result.HandshakeAccepted)
        {
            _acceptedHandshakeCount = checked(
                _acceptedHandshakeCount + 1);
        }

        if (!result.CleanupCompleted)
        {
            _cleanupIncompleteCount = checked(
                _cleanupIncompleteCount + 1);
        }

        _deliveredPacketCount = checked(
            _deliveredPacketCount + result.DeliveredPacketCount);
        _deliveredByteCount = checked(
            _deliveredByteCount + result.DeliveredByteCount);
        _discardedPacketCount = checked(
            _discardedPacketCount + result.DiscardedPacketCount);
        _discardedByteCount = checked(
            _discardedByteCount + result.DiscardedByteCount);

        switch (result.EndReason)
        {
            case PcpDownstreamChildEndReason.AdmissionClosed:
                _admissionClosedCount = checked(_admissionClosedCount + 1);
                break;
            case PcpDownstreamChildEndReason.Stopped:
                _stoppedCount = checked(_stoppedCount + 1);
                break;
            case PcpDownstreamChildEndReason.RelayFull:
                _relayFullCount = checked(_relayFullCount + 1);
                break;
            case PcpDownstreamChildEndReason.ProtocolRejected:
                _protocolRejectedCount = checked(
                    _protocolRejectedCount + 1);
                break;
            case PcpDownstreamChildEndReason.HandshakeTimedOut:
                _handshakeTimedOutCount = checked(
                    _handshakeTimedOutCount + 1);
                break;
            case PcpDownstreamChildEndReason.QuitReceived:
                _quitReceivedCount = checked(_quitReceivedCount + 1);
                break;
            case PcpDownstreamChildEndReason.ConnectionEnded:
                _connectionEndedCount = checked(
                    _connectionEndedCount + 1);
                break;
            case PcpDownstreamChildEndReason.FanoutStopped:
                _fanoutStoppedCount = checked(_fanoutStoppedCount + 1);
                break;
            case PcpDownstreamChildEndReason.SlowConsumer:
                _slowConsumerCount = checked(_slowConsumerCount + 1);
                break;
            case PcpDownstreamChildEndReason.WriteTimedOut:
                _writeTimedOutCount = checked(_writeTimedOutCount + 1);
                break;
            case PcpDownstreamChildEndReason.WriteFailed:
                _writeFailedCount = checked(_writeFailedCount + 1);
                break;
            case PcpDownstreamChildEndReason.PingCompleted:
                break;
            case PcpDownstreamChildEndReason.PacingLimited:
                _pacingLimitedCount = checked(_pacingLimitedCount + 1);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown downstream child end reason.");
        }
    }

    private PcpDownstreamRelayServerSnapshot CreateSnapshotLocked()
    {
        PruneAdmissionCleanupLocked();
        return new PcpDownstreamRelayServerSnapshot(
            _state,
            _endReason,
            _acceptedConnectionCount,
            _activeConnectionCount,
            _completedConnectionCount,
            _acceptedHandshakeCount,
            _relayFullCount,
            _protocolRejectedCount,
            _handshakeTimedOutCount,
            _quitReceivedCount,
            _connectionEndedCount,
            _fanoutStoppedCount,
            _slowConsumerCount,
            _writeTimedOutCount,
            _writeFailedCount,
            _stoppedCount,
            _cleanupIncompleteCount,
            _deliveredPacketCount,
            _deliveredByteCount,
            _discardedPacketCount,
            _discardedByteCount,
            _internalFailureCount)
        {
            AdmissionClosedCount = _admissionClosedCount,
            AdmissionCloseFailedCount = _admissionCloseFailedCount,
            AdmissionCleanupPendingCount = _pendingAdmissionCleanup.Count,
            AdmissionCleanupFailedCount = _admissionCleanupFailedCount,
            WanIngress = _wanIngress?.Snapshot,
            Traffic = TrafficScope.Snapshot,
            PacingLimitedCount = _pacingLimitedCount,
        };
    }

    private static void CancelBestEffort(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // A cancellation callback cannot change server ownership.
        }
        catch (ObjectDisposedException) { /* Concurrent completed Dispose owns cleanup. */ }
    }

    private sealed class SessionEntry
    {
        public SessionEntry(TcpTransportConnection connection)
        {
            Connection = connection;
        }

        public TcpTransportConnection Connection { get; }
        public Guid ChildInstanceId { get; } = Guid.NewGuid();
        public PendingDisconnect? Disconnect { get; set; }
        public bool CleanupConfirmed { get; set; }

        public PcpDownstreamChildSession? Session;
        public Task? Task { get; set; }
        public PcpWanIngressPolicy.Lease? WanLease { get; init; }
        public bool Shared { get; init; }
    }
}
