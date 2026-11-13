using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Owns one shared bounded TCP listener, reads one downstream HTTP relay request,
/// resolves its channel through the current route registry, and delegates an
/// already-routed connection to a borrowed handler. The listener owns connection
/// and PipeReader cleanup; route leases remain held until transport cleanup.
/// </summary>
public sealed class PcpSharedDownstreamRelayListener : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly TcpTransportListener _listener;
    private readonly PcpDownstreamRelayRouteRegistry _registry;
    private readonly IPcpDownstreamRoutedConnectionHandler _handler;
    private readonly PcpDownstreamChildOptions _childOptions;
    private readonly PcpWanIngressPolicy? _wanIngress;
    private readonly Guid _pingSessionId;
    internal PcpWanIngressSnapshot? WanIngress => _wanIngress?.Snapshot;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly HashSet<SessionEntry> _sessions = [];

    private Task<PcpSharedDownstreamRelayListenerResult>? _runTask;
    private IPEndPoint? _boundEndPoint;
    private PcpSharedDownstreamRelayListenerState _state =
        PcpSharedDownstreamRelayListenerState.Created;
    private PcpSharedDownstreamRelayListenerEndReason? _endReason;
    private long _acceptedConnectionCount;
    private int _activeConnectionCount;
    private long _completedConnectionCount;
    private long _routedConnectionCount;
    private long _badRequestCount;
    private long _notFoundCount;
    private long _handshakeTimedOutCount;
    private long _handlerFailureCount;
    private long _internalFailureCount;
    private long _cleanupFailureCount;
    private int _disposed;

    /// <summary>Reuses current Coordinators; construction performs no bind or I/O.</summary>
    public PcpSharedDownstreamRelayListener(IPEndPoint localEndPoint,
        Func<ChannelId, PcpRelayFanout, PcpRelayCoordinator?> resolveOwner,
        TcpTransportOptions? transportOptions = null, PcpWanIngressOptions? wanOptions = null)
        : this(localEndPoint, new PcpDownstreamRelayRouteRegistry(), CreateOwnerHandler(resolveOwner),
            transportOptions, wanOptions: wanOptions ?? new())
    {
    }

    private static PcpDownstreamChildSessionRoutedConnectionHandler CreateOwnerHandler(
        Func<ChannelId, PcpRelayFanout, PcpRelayCoordinator?> resolveOwner)
    {
        ArgumentNullException.ThrowIfNull(resolveOwner);
        return new(Guid.NewGuid(), resolveOwner: (channel, fanout) =>
        {
            var owner = resolveOwner(channel, fanout);
            return owner is not null && ReferenceEquals(owner.DownstreamFanout, fanout) ? owner.DownstreamServer : null;
        });
    }

    /// <summary>Registers an exact existing fanout. Disposal closes routing immediately and joins its drain.</summary>
    public IAsyncDisposable Register(ChannelId channelId, PcpRelayFanout fanout)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_stopSource.IsCancellationRequested) throw new InvalidOperationException("Public routing is closed.");
            return _registry.Register(channelId, fanout);
        }
    }

    public int RouteCount => _registry.Count;
    public long CleanupFailureCount => Snapshot.CleanupFailureCount;
    internal PcpDownstreamRelayRouteRegistry RouteRegistry => _registry;

    /// <summary>Binds once, then runs the existing bounded accept loop until Stop or cancellation.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default) => RunAsync(cancellationToken);

    internal PcpSharedDownstreamRelayListener(
        IPEndPoint localEndPoint,
        PcpDownstreamRelayRouteRegistry registry,
        IPcpDownstreamRoutedConnectionHandler handler,
        TcpTransportOptions? transportOptions = null,
        PcpDownstreamChildOptions? childOptions = null,
        PcpWanIngressOptions? wanOptions = null,
        Guid? pingSessionId = null)
    {
        ArgumentNullException.ThrowIfNull(localEndPoint);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(handler);

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
        _registry = registry;
        _handler = handler;
        if (wanOptions is not null) { wanOptions.Validate(); _wanIngress = new(wanOptions); }
        _pingSessionId = pingSessionId ?? Guid.NewGuid();
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
                        "The shared downstream relay listener has not started.");
            }
        }
    }

    internal PcpSharedDownstreamRelayListenerSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CreateSnapshotLocked();
            }
        }
    }

    internal Task<PcpSharedDownstreamRelayListenerResult> RunAsync(
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
                    "A shared downstream relay listener can run only once.");
            }

            if (_stopSource.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "A stopped shared downstream relay listener cannot run.");
            }

            _listener.Start();
            _boundEndPoint = _listener.LocalEndPoint;
            _state = PcpSharedDownstreamRelayListenerState.Running;
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

        Task<PcpSharedDownstreamRelayListenerResult>? runTask;

        lock (_gate)
        {
            runTask = _runTask;

            if (runTask is null)
            {
                _state = PcpSharedDownstreamRelayListenerState.Stopped;
                _endReason = PcpSharedDownstreamRelayListenerEndReason.Stopped;
            }
        }

        _wanIngress?.Close();
        CancelBestEffort(_stopSource);

        if (runTask is not null)
        {
            await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void RequestStop()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        _wanIngress?.Close();
        CancelBestEffort(_stopSource);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Task<PcpSharedDownstreamRelayListenerResult>? runTask;
        _wanIngress?.Close();
        CancelBestEffort(_stopSource);

        lock (_gate)
        {
            runTask = _runTask;

            if (runTask is null)
            {
                _state = PcpSharedDownstreamRelayListenerState.Stopped;
                _endReason = PcpSharedDownstreamRelayListenerEndReason.Stopped;
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

    private async Task<PcpSharedDownstreamRelayListenerResult> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        using var operationSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopSource.Token);
        var operationToken = operationSource.Token;
        var endReason = PcpSharedDownstreamRelayListenerEndReason.Stopped;

        try
        {
            while (true)
            {
                var connection = await _listener.AcceptAsync(
                    operationToken).ConfigureAwait(false);
                if (!StartSession(connection, operationToken))
                    await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (operationSource.IsCancellationRequested)
        {
            endReason = cancellationToken.IsCancellationRequested &&
                !_stopSource.IsCancellationRequested
                    ? PcpSharedDownstreamRelayListenerEndReason.CallerCanceled
                    : PcpSharedDownstreamRelayListenerEndReason.Stopped;
        }
        catch (Exception exception) when (exception is
            SocketException or
            IOException or
            InvalidOperationException)
        {
            endReason = PcpSharedDownstreamRelayListenerEndReason.AcceptFailed;
        }
        finally
        {
            _wanIngress?.Close();
            lock (_gate)
            {
                _state = PcpSharedDownstreamRelayListenerState.Stopping;
            }

            CancelBestEffort(operationSource);
            await _listener.StopAsync().ConfigureAwait(false);
            await WaitForSessionsAsync().ConfigureAwait(false);

            lock (_gate)
            {
                _state = PcpSharedDownstreamRelayListenerState.Stopped;
                _endReason = endReason;
            }
        }

        return new PcpSharedDownstreamRelayListenerResult(
            endReason,
            Snapshot);
    }

    private bool StartSession(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || _stopSource.IsCancellationRequested) return false;
        var ingress = _wanIngress?.TryAccept(connection.RemoteEndPoint.Address);
        if (_wanIngress is not null && ingress is null) return false;
        var entry = new SessionEntry(connection) { WanLease = ingress };

        lock (_gate)
        {
            _sessions.Add(entry);
            _acceptedConnectionCount = checked(
                _acceptedConnectionCount + 1);
            _activeConnectionCount = checked(
                _activeConnectionCount + 1);
            entry.Task = Task.Run(() => RunSessionAsync(entry, cancellationToken), CancellationToken.None);
            return true;
        }
    }

    private async Task RunSessionAsync(
        SessionEntry entry,
        CancellationToken cancellationToken)
    {
        PipeReader? reader = null;
        PcpDownstreamRelayIngressResolution? routing = null;
        var routed = false;
        var badRequest = false;
        var notFound = false;
        var handshakeTimedOut = false;
        var handlerFailed = false;
        var internalFailure = false;
        var cleanupFailed = false;

        try
        {
            reader = PipeReader.Create(
                entry.Connection.TransportStream,
                new StreamPipeReaderOptions(
                    bufferSize: _childOptions.PipeBufferSize,
                    minimumReadSize: _childOptions.PipeMinimumReadSize,
                    leaveOpen: true));

            using var timeoutSource = new CancellationTokenSource(
                entry.WanLease?.Remaining(_childOptions.HandshakeTimeout) ?? _childOptions.HandshakeTimeout,
                entry.WanLease?.Clock ?? TimeProvider.System);
            using var ingressSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);

            try
            {
                if (entry.WanLease is { } lease)
                {
                    using var identify = new CancellationTokenSource(lease.Remaining(lease.IdentificationTimeout), lease.Clock);
                    using var identification = CancellationTokenSource.CreateLinkedTokenSource(ingressSource.Token, identify.Token);
                    if (await PcpWanProtocolIngress.IsDirectPcpAsync(reader, identification.Token).ConfigureAwait(false))
                    {
                        await using var atoms = new PcpAtomPipeMessageReader(reader,
                            new PcpProtocolLimits { MaxContentLength = 4096, MaxChildCount = 32, MaxNestingDepth = 4 }, 4096);
                        await PcpWanProtocolIngress.RunPingAsync(atoms, _pingSessionId, lease,
                            (bytes, token) => entry.Connection.WriteAsync(bytes, token), cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }
                routing = await PcpDownstreamRelayIngressRouter.ReadAndResolveAsync(
                    reader,
                    _registry,
                    entry.WanLease is null ? _childOptions.MaxHttpHeaderBytes : Math.Min(4096, _childOptions.MaxHttpHeaderBytes),
                    _childOptions.PcpVersion,
                    ingressSource.Token).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                badRequest = true;
                await TryWriteRejectionAsync(
                    entry.Connection,
                    400,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
                when (timeoutSource.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
            {
                handshakeTimedOut = true;
                return;
            }

            if (!routing.IsRouted)
            {
                if (routing.StatusCode == 404)
                {
                    notFound = true;
                }
                else
                {
                    badRequest = true;
                }

                await TryWriteRejectionAsync(
                    entry.Connection,
                    routing.StatusCode,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var request = routing.Request ??
                throw new InvalidOperationException(
                    "A routed downstream ingress has no request.");
            var fanout = routing.Fanout ??
                throw new InvalidOperationException(
                    "A routed downstream ingress has no fan-out.");
            routed = true;

            try
            {
                _ = entry.WanLease is { } ingress
                    ? await _handler.RunWanAsync(entry.Connection, reader, request, fanout, ingress, cancellationToken).ConfigureAwait(false)
                    : await _handler.RunAsync(
                    entry.Connection,
                    reader,
                    request,
                    fanout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                handlerFailed = true;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) { handshakeTimedOut = true; }
        catch (Exception exception) when (exception is InvalidDataException or PcpProtocolException)
        { badRequest = true; }
        catch (Exception exception) when (exception is
            IOException or
            SocketException or
            InvalidOperationException or
            ObjectDisposedException)
        {
            if (_wanIngress is not null) badRequest = true;
            else internalFailure = true;
        }
        finally
        {
            if (reader is not null)
            {
                try
                {
                    await reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is
                    IOException or
                    ObjectDisposedException)
                {
                    internalFailure = true; cleanupFailed = true;
                }
            }

            try
            {
                await entry.Connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is
                IOException or
                SocketException or
                ObjectDisposedException)
            {
                internalFailure = true; cleanupFailed = true;
            }

            // Keep route/policy leases until actual transport cleanup.
            if (entry.Connection.CleanupCompletion.IsCompletedSuccessfully)
            {
                routing?.Dispose();
                entry.WanLease?.Dispose();
            }
            else { internalFailure = true; cleanupFailed = true; }

            lock (_gate)
            {
                _sessions.Remove(entry);
                _activeConnectionCount--;
                _completedConnectionCount = checked(
                    _completedConnectionCount + 1);
                if (cleanupFailed) _cleanupFailureCount++;

                if (routed)
                {
                    _routedConnectionCount = checked(
                        _routedConnectionCount + 1);
                }

                if (badRequest)
                {
                    _badRequestCount = checked(_badRequestCount + 1);
                }

                if (notFound)
                {
                    _notFoundCount = checked(_notFoundCount + 1);
                }

                if (handshakeTimedOut)
                {
                    _handshakeTimedOutCount = checked(
                        _handshakeTimedOutCount + 1);
                }

                if (handlerFailed)
                {
                    _handlerFailureCount = checked(
                        _handlerFailureCount + 1);
                }

                if (internalFailure)
                {
                    _internalFailureCount = checked(
                        _internalFailureCount + 1);
                }
            }
        }
    }

    private async ValueTask TryWriteRejectionAsync(
        TcpTransportConnection connection,
        int statusCode,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(
            _childOptions.WriteTimeout);
        using var writeSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            await connection.WriteAsync(
                PcpDownstreamHttpResponse.CreateRejection(
                    statusCode,
                    _childOptions.UserAgent),
                writeSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            OperationCanceledException or
            IOException or
            SocketException or
            ObjectDisposedException)
        {
            // Rejection is diagnostic and remains best effort and bounded.
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

    private PcpSharedDownstreamRelayListenerSnapshot CreateSnapshotLocked()
    {
        return new PcpSharedDownstreamRelayListenerSnapshot(
            _state,
            _endReason,
            _acceptedConnectionCount,
            _activeConnectionCount,
            _completedConnectionCount,
            _routedConnectionCount,
            _badRequestCount,
            _notFoundCount,
            _handshakeTimedOutCount,
            _handlerFailureCount,
            _internalFailureCount, _cleanupFailureCount);
    }

    private static void CancelBestEffort(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // A cancellation callback cannot change listener ownership.
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
        public PcpWanIngressPolicy.Lease? WanLease { get; init; }
    }
}
