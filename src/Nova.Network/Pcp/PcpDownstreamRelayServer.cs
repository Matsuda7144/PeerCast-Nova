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
public sealed class PcpDownstreamRelayServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly TcpTransportListener _listener;
    private readonly NatPortMappingManager? _portMapping;
    private IDisposable? _publicMappingOwner;
    private readonly PcpRelayFanout _fanout;
    private readonly ChannelId _channelId;
    private readonly Guid _localSessionId;
    private readonly PcpDownstreamChildOptions _childOptions;
    private readonly IPcpDownstreamRelayAdmission? _admission;
    private readonly CancellationTokenSource _stopSource = new();
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

    public PcpDownstreamRelayServer(
        IPEndPoint localEndPoint,
        PcpRelayFanout fanout,
        ChannelId channelId,
        Guid localSessionId,
        TcpTransportOptions? transportOptions = null,
        PcpDownstreamChildOptions? childOptions = null,
        NatPortMappingManager? portMapping = null,
        IPcpDownstreamRelayAdmission? admission = null)
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

        _listener = new TcpTransportListener(
            localEndPoint,
            copiedTransportOptions);
        _portMapping = portMapping;
        _fanout = fanout;
        _channelId = channelId;
        _localSessionId = localSessionId;
        _admission = admission;
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
            _boundEndPoint = _listener.LocalEndPoint;
            if (_portMapping is not null &&
                _boundEndPoint.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(_boundEndPoint.Address))
            {
                _publicMappingOwner = _portMapping.AcquirePublicTcpListener(_boundEndPoint);
            }

            _state = PcpDownstreamRelayServerState.Running;
            _runTask = RunCoreAsync(cancellationToken);
            return _runTask;
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
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
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        CancelBestEffort(_stopSource);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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
            await _listener.DisposeAsync().ConfigureAwait(false);
            _stopSource.Dispose();
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
        var endReason = PcpDownstreamRelayServerEndReason.Stopped;

        try
        {
            while (true)
            {
                var connection = await _listener.AcceptAsync(
                    operationToken).ConfigureAwait(false);
                StartSession(connection, operationToken);
            }
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
        {
            endReason = PcpDownstreamRelayServerEndReason.AcceptFailed;
        }
        finally
        {
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

    private void StartSession(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        var entry = new SessionEntry(connection);

        lock (_gate)
        {
            _sessions.Add(entry);
            _acceptedConnectionCount = checked(
                _acceptedConnectionCount + 1);
            _activeConnectionCount = checked(
                _activeConnectionCount + 1);
            entry.Task = RunSessionAsync(entry, cancellationToken);
        }
    }

    private async Task RunSessionAsync(
        SessionEntry entry,
        CancellationToken cancellationToken)
    {
        PcpDownstreamChildResult? result = null;
        var internalFailure = false;

        try
        {
            await using var session = new PcpDownstreamChildSession(
                entry.Connection,
                _fanout,
                _channelId,
                _localSessionId,
                _childOptions,
                packetWriter: null,
                admission: _admission);
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
            lock (_gate)
            {
                _sessions.Remove(entry);
                _activeConnectionCount--;
                _completedConnectionCount = checked(
                    _completedConnectionCount + 1);

                if (internalFailure)
                {
                    _internalFailureCount = checked(
                        _internalFailureCount + 1);
                }
                else if (result is not null)
                {
                    RecordResultLocked(result);
                }
            }
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
            default:
                throw new InvalidOperationException(
                    "Unknown downstream child end reason.");
        }
    }

    private PcpDownstreamRelayServerSnapshot CreateSnapshotLocked()
    {
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
            _internalFailureCount);
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
    }

    private sealed class SessionEntry
    {
        public SessionEntry(TcpTransportConnection connection)
        {
            Connection = connection;
        }

        public TcpTransportConnection Connection { get; }

        public Task? Task { get; set; }
    }
}
