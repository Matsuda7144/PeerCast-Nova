using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Owns a loopback-only HTTP listener and creates independent raw-FLV packet
/// consumers for PCP channel sessions. No Playback or Windows output component
/// is used by this candidate path.
/// </summary>
public sealed class PcpHttpFlvPlaybackHost :
    IAsyncDisposable
{
    private const int MaximumSourceSessions = 8;
    private const string StreamPrefix = "/stream/";
    private const string SuccessResponse =
        "HTTP/1.1 200 OK\r\n" +
        "Server: PeerCastNova\r\n" +
        "Content-Type: video/x-flv\r\n" +
        "Cache-Control: no-store\r\n" +
        "Connection: close\r\n\r\n";

    private readonly object _gate = new();
    private readonly PcpHttpFlvPlaybackOptions _options;
    private readonly TcpTransportListener _listener;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly SemaphoreSlim _streamSlots;
    private readonly Dictionary<Guid, PcpHttpFlvPlaybackSession> _sources = [];
    private readonly ConcurrentDictionary<long, TcpTransportConnection> _connections = [];
    private readonly ConcurrentDictionary<long, Task> _clients = [];
    private readonly Action<string>? _streamUrlReady;
    private IPEndPoint? _localEndPoint;
    private Task? _acceptTask;
    private Task? _disposeTask;
    private long _nextClientId;
    private bool _started;
    private int _disposed;

    public PcpHttpFlvPlaybackHost(
        PcpHttpFlvPlaybackOptions? options = null,
        Action<string>? streamUrlReady = null)
    {
        _options = options is null
            ? new PcpHttpFlvPlaybackOptions()
            : options with
            {
                Bootstrap = options.Bootstrap with { },
                Fanout = options.Fanout with { },
                Transport = options.Transport with { },
            };
        _options.Validate();
        _streamUrlReady = streamUrlReady;
        _streamSlots = new SemaphoreSlim(
            _options.MaximumConcurrentStreamClients,
            _options.MaximumConcurrentStreamClients);
        _listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, _options.Port),
            _options.Transport);
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
                return _localEndPoint ?? throw new InvalidOperationException(
                    "The HTTP-FLV listener has not started.");
            }
        }
    }

    public int ActiveConnectionCount => _connections.Count;

    public int ActiveSourceCount
    {
        get
        {
            lock (_gate)
            {
                return _sources.Count;
            }
        }
    }

    public int ActiveStreamClientCount
    {
        get
        {
            lock (_gate)
            {
                return _sources.Values.Sum(source => source.ActiveClientCount);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            if (_started)
            {
                throw new InvalidOperationException(
                    "The HTTP-FLV host can be started only once.");
            }

            _listener.Start();
            _localEndPoint = _listener.LocalEndPoint;
            _started = true;
            _acceptTask = AcceptLoopAsync(_stopSource.Token);
        }
    }

    public string StreamUrlFor(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        var endpoint = LocalEndPoint;
        return $"http://127.0.0.1:{endpoint.Port}{StreamPrefix}{channelId}";
    }

    public IOwnedPcpChannelPacketConsumer Create(
        ChannelId channelId,
        ChannelSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);

        PcpHttpFlvPlaybackSession source;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            if (!_started)
            {
                throw new InvalidOperationException(
                    "The HTTP-FLV host must be started before creating a PCP consumer.");
            }

            if (_sources.Count >= MaximumSourceSessions ||
                _sources.ContainsKey(channelId.Value))
            {
                throw new InvalidOperationException(
                    "An HTTP-FLV source for this channel is already active or the bounded source limit is full.");
            }

            source = new PcpHttpFlvPlaybackSession(
                channelId,
                sessionId,
                _options,
                RemoveSource,
                () => _streamUrlReady?.Invoke(StreamUrlFor(channelId)));
            _sources.Add(channelId.Value, source);
        }

        return source;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new(_disposeTask);
            }

            Interlocked.Exchange(ref _disposed, 1);
            _disposeTask = DisposeCoreAsync();
            return new(_disposeTask);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TcpTransportConnection connection;
            try
            {
                connection = await _listener.AcceptAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!_streamSlots.Wait(0, CancellationToken.None))
            {
                try
                {
                    // Consume the bounded request before closing so the TCP
                    // peer can receive the finite rejection response cleanly.
                    _ = await ReadRequestHeaderAsync(connection, cancellationToken)
                        .ConfigureAwait(false);
                    await WriteErrorAsync(connection, 503, "Service Unavailable", cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // A rejected client cannot affect the listener or a live stream.
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }

                continue;
            }

            var clientId = Interlocked.Increment(ref _nextClientId);
            _connections[clientId] = connection;
            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Task.Run(
                async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    await RunClientLifecycleAsync(
                        clientId,
                        connection,
                        cancellationToken).ConfigureAwait(false);
                },
                CancellationToken.None);
            _clients[clientId] = task;
            start.TrySetResult(true);
        }
    }

    internal PcpHttpFlvStartupObserver? StartupObserver { get; init; }

    private async Task RunClientAsync(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        PcpRelayChildRegistration? registration = null;
        PcpHttpFlvClientSink? sink = null;
        CancellationTokenSource? disconnectSource = null;
        try
        {
            var request = await ReadRequestHeaderAsync(
                connection,
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(request.Method, "GET", StringComparison.Ordinal))
            {
                await WriteErrorAsync(connection, 405, "Method Not Allowed", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (!TryResolveSource(request.RequestTarget, out var source))
            {
                await WriteErrorAsync(connection, 404, "Not Found", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (!await source.WaitForHeaderAsync(
                    _options.BootstrapHeaderTimeout,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                await WriteErrorAsync(connection, 503, "Source Not Ready", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            sink = new PcpHttpFlvClientSink(
                connection,
                _options.WriteTimeout, StartupObserver);
            if (!source.TryAddClient(sink, out registration, out var bootstrap) ||
                registration is null ||
                bootstrap is null)
            {
                await WriteErrorAsync(connection, 503, "Stream Client Limit Reached", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await sink.WriteResponseHeaderAsync(
                Encoding.ASCII.GetBytes(SuccessResponse),
                cancellationToken).ConfigureAwait(false);
            await sink.WriteBootstrapAsync(bootstrap, cancellationToken)
                .ConfigureAwait(false);
            sink.Activate();

            disconnectSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var disconnectTask = MonitorClientDisconnectAsync(
                connection,
                disconnectSource.Token);
            var completedTask = await Task.WhenAny(
                registration.Completion,
                disconnectTask).ConfigureAwait(false);

            if (completedTask == disconnectTask)
            {
                await disconnectTask.ConfigureAwait(false);
                await registration.StopAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                disconnectSource.Cancel();
                await IgnoreMonitorStopAsync(disconnectTask).ConfigureAwait(false);
                await registration.Completion.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Listener stop and upstream/client cancellation are normal exits.
        }
        catch (InvalidDataException)
        {
            try
            {
                await WriteErrorAsync(connection, 400, "Bad Request", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // The peer may have disconnected while its request was parsed.
            }
        }
        catch (EndOfStreamException)
        {
            // A client that closes before completing its request is discarded.
        }
        catch (IOException)
        {
            // Client disconnects are isolated to the current stream.
        }
        catch (ObjectDisposedException)
        {
            // Concurrent source or server cleanup owns the connection.
        }
        finally
        {
            // Bootstrap/header writes have really ended before the child barrier settles.
            sink?.EndStartup();
            disconnectSource?.Cancel();
            disconnectSource?.Dispose();
            try
            {
                if (registration is not null)
                    await registration.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                // Closing the transport may unblock an in-flight send. The
                // client lifecycle retains CTS ownership after a bounded stop.
                try { await connection.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    if (sink is not null)
                        await sink.DisposeAfterWorkerAsync(registration?.Completion ?? Task.CompletedTask).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task WriteErrorAsync(
        TcpTransportConnection connection,
        int statusCode,
        string reason,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            "Server: PeerCastNova\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n");
        await WriteWithTimeoutAsync(
            connection,
            response,
            _options.WriteTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PcpHttpRequestHeader> ReadRequestHeaderAsync(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        using var headerTimeout = new CancellationTokenSource(
            _options.RequestHeaderTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            headerTimeout.Token);
        var reader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            return await PcpHttpHeaderBlockReader.ReadRequestAsync(
                reader,
                _options.MaximumHttpHeaderBytes,
                linkedSource.Token).ConfigureAwait(false);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteWithTimeoutAsync(
        TcpTransportConnection connection,
        ReadOnlyMemory<byte> bytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        await connection.WriteAsync(bytes, linkedSource.Token).ConfigureAwait(false);
    }

    private async Task RunClientLifecycleAsync(
        long clientId,
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunClientAsync(connection, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // A client failure is local to the connection.
        }
        finally
        {
            _connections.TryRemove(clientId, out _);
            _clients.TryRemove(clientId, out _);
            _streamSlots.Release();
        }
    }

    private static async Task MonitorClientDisconnectAsync(
        TcpTransportConnection connection,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        try
        {
            while (true)
            {
                var count = await connection.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    return;
                }

                // A GET stream has no request body; unexpected post-header data
                // terminates only this child stream.
                return;
            }
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task IgnoreMonitorStopAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal when upstream ends before the HTTP client disconnects.
        }
        catch (IOException)
        {
            // Connection cleanup also releases a pending monitor read.
        }
        catch (ObjectDisposedException)
        {
            // Connection cleanup also releases a pending monitor read.
        }
    }

    private bool TryResolveSource(
        string requestTarget,
        out PcpHttpFlvPlaybackSession source)
    {
        source = null!;
        if (!requestTarget.StartsWith(StreamPrefix, StringComparison.Ordinal) ||
            requestTarget.Contains('?') ||
            requestTarget.Contains('#'))
        {
            return false;
        }

        var idText = requestTarget.AsSpan(StreamPrefix.Length);
        if (idText.Length != 32 ||
            !Guid.TryParseExact(idText, "N", out var id))
        {
            return false;
        }

        lock (_gate)
        {
            return _sources.TryGetValue(id, out source!);
        }
    }

    private void RemoveSource(PcpHttpFlvPlaybackSession source)
    {
        lock (_gate)
        {
            if (_sources.TryGetValue(source.ChannelIdValue, out var current) &&
                ReferenceEquals(current, source))
            {
                _sources.Remove(source.ChannelIdValue);
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            _stopSource.Cancel();
            await _listener.StopAsync().ConfigureAwait(false);

            if (_acceptTask is not null)
            {
                try
                {
                    await _acceptTask.WaitAsync(_options.CleanupTimeout)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException(
                        "The HTTP-FLV accept loop did not stop within its cleanup bound.");
                }
            }

            PcpHttpFlvPlaybackSession[] sources;
            lock (_gate)
            {
                sources = [.. _sources.Values];
            }

            foreach (var source in sources)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }

            foreach (var connection in _connections.Values)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            var clients = _clients.Values.ToArray();
            if (clients.Length > 0)
            {
                try
                {
                    await Task.WhenAll(clients)
                        .WaitAsync(_options.CleanupTimeout)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException(
                        "HTTP-FLV clients did not stop within their cleanup bound.");
                }
            }
        }
        finally
        {
            await _listener.DisposeAsync().ConfigureAwait(false);
        }

        _streamSlots.Dispose();
        _stopSource.Dispose();
    }
}

internal sealed class PcpHttpFlvClientSink :
    IPcpRelayChildSink, IPcpRelayChildStartupBoundary
{
    private readonly TcpTransportConnection _connection;
    private readonly TimeSpan _writeTimeout;
    private readonly PcpHttpFlvStartupLifetime _startup;

    public Task StartupCompletion => _startup.StartupCompletion;
    public Task StartupCancellationCompletion => _startup.CancellationCompletion;
    public void RequestStartupCancellation() => _startup.RequestCancellation();
    public void EndStartup() => _startup.EndStartup();
    public Task DisposeAfterWorkerAsync(Task workerCompletion) => _startup.DisposeAfterWorkerAsync(workerCompletion);

    public PcpHttpFlvClientSink(
        TcpTransportConnection connection,
        TimeSpan writeTimeout,
        PcpHttpFlvStartupObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
        _writeTimeout = writeTimeout;
        _startup = new PcpHttpFlvStartupLifetime(observer);
    }

    public async ValueTask SendAsync(
        PcpRelayPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        await _startup.StartupCompletion.WaitAsync(cancellationToken).ConfigureAwait(false);
        await WriteAsync(packet.Payload, cancellationToken).ConfigureAwait(false);
    }

    public Task WriteResponseHeaderAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken) =>
        _startup.WriteAsync(token => WriteAsync(bytes, cancellationToken, token), PcpHttpFlvStartupPoint.BeforeHeaderWrite);

    public Task WriteBootstrapAsync(
        PcpFlvBootstrapPayloadSnapshot bootstrap,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        return _startup.WriteAsync(async token =>
        {
            await WriteAsync(bootstrap.Header, cancellationToken, token).ConfigureAwait(false);
            await WriteAsync(bootstrap.Metadata, cancellationToken, token).ConfigureAwait(false);
            await WriteAsync(bootstrap.VideoConfiguration, cancellationToken, token).ConfigureAwait(false);
            await WriteAsync(bootstrap.AudioConfiguration, cancellationToken, token).ConfigureAwait(false);
        }, PcpHttpFlvStartupPoint.BeforeBootstrapWrite);
    }

    public void Activate() => _startup.Activate();

    private async Task WriteAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken,
        CancellationToken startupToken = default)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        using var timeoutSource = new CancellationTokenSource(_writeTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token,
            startupToken);
        try
        {
            await _connection.WriteAsync(bytes, linkedSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}
