using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Playback;

/// <summary>
/// Serves one generation of complete H.264/AAC fMP4 segments to one loopback
/// client at a time. Start a new host for a new initialization generation.
/// </summary>
public sealed class PlaybackFmp4HttpStreamHost : IAsyncDisposable
{
    public const int MaximumInitializationBytes = 64 * 1024;
    public const int MaximumMediaSegmentBytes = 1024 * 1024 + 64 * 1024;

    private const int MaximumHttpHeaderBytes = 8 * 1024;
    private const int MaximumHttpChunkBytes = 16 * 1024;
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(10);
    private static readonly byte[] SuccessHeader =
        "HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nTransfer-Encoding: chunked\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"u8.ToArray();
    private static readonly byte[] ChunkSuffix = "\r\n"u8.ToArray();
    private static readonly byte[] LastChunk = "0\r\n\r\n"u8.ToArray();

    private readonly object _gate = new();
    private readonly ChannelSessionId _sessionId;
    private readonly TcpTransportListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<bool> _firstClient = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _acceptLoop;
    private Task<bool>? _completionTask;
    private Task? _disposeTask;
    private PlaybackFmp4LiveInitializationFragmentDefaultsProjection? _initializationDefaults;
    private byte[]? _initialization;
    private uint? _lastSequence;
    private PlaybackFmp4HttpClientBuffer? _client;

    public PlaybackFmp4HttpStreamHost(ChannelId channelId, ChannelSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        _sessionId = sessionId;
        _listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0), new TcpTransportOptions
        {
            ListenBacklog = 1,
            MaxConcurrentConnections = 1,
        });
        _listener.Start();
        StreamUri = new Uri($"http://127.0.0.1:{_listener.LocalEndPoint.Port}/fmp4/{channelId}");
        _acceptLoop = AcceptClientsAsync(_stop.Token);
    }

    public Uri StreamUri { get; }

    /// <summary>Waits for the first valid GET to subscribe, before sending its response.</summary>
    public Task<bool> WaitForClientAsync(CancellationToken cancellationToken = default) =>
        _firstClient.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Publishes an initialization or media segment. The caller can reuse its
    /// buffer after this method returns. Capacity failure ends only the client.
    /// </summary>
    public bool TryPublishSegment(ChannelSessionId sessionId, ReadOnlySpan<byte> segment)
    {
        lock (_gate)
        {
            if (sessionId != _sessionId || _completionTask is not null || _disposeTask is not null)
            {
                return false;
            }

            if (_initializationDefaults is null)
            {
                if (PlaybackFmp4LiveInitializationFragmentDefaultsProjector.Project(
                        segment, MaximumInitializationBytes, out var defaults) !=
                    PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult.Projected ||
                    defaults is null)
                {
                    return false;
                }

                _initialization = segment.ToArray();
                _initializationDefaults = defaults;
                return true;
            }

            if (PlaybackFmp4LiveEncodedSampleBatchAdapter.ProjectMediaSegment(
                    segment, MaximumMediaSegmentBytes, _initializationDefaults, out var projection) !=
                PlaybackEncodedSampleBatchCreationResult.Created ||
                projection is null ||
                projection.SequenceNumber == 0 ||
                (_lastSequence.HasValue && (ulong)_lastSequence.Value + 1 != projection.SequenceNumber))
            {
                return false;
            }

            _lastSequence = projection.SequenceNumber;
            if (_client is { } client &&
                (client.Started || CanStartAt(projection.ResolvedRuns, _initializationDefaults.VideoTrackId)) &&
                !client.TryEnqueue(client.Started ? null : _initialization, segment))
            {
                client.Abort();
            }

            return true;
        }
    }

    /// <summary>Drains the active response. Returns true if the final HTTP chunk was written.</summary>
    public Task<bool> CompleteAsync()
    {
        lock (_gate)
        {
            if (_completionTask is not null)
            {
                return _completionTask;
            }

            _initialization = null;
            _initializationDefaults = null;
            _firstClient.TrySetResult(false);
            _client?.Complete();
            _completionTask = _client?.Finished.Task ?? Task.FromResult(false);
            return _completionTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        _initialization = null;
        _initializationDefaults = null;
        _firstClient.TrySetResult(false);
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await _acceptLoop.ConfigureAwait(false);
        }
        finally
        {
            await _listener.DisposeAsync().ConfigureAwait(false);
            _stop.Dispose();
        }
    }

    private static bool CanStartAt(PlaybackFmp4LiveMediaFragmentResolvedRunProjection runs, uint videoTrackId) =>
        PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector.Project(
            runs.FragmentBinding.FragmentIdentity,
            videoTrackId,
            runs.VideoTrack?.ResolvedRun,
            out var eligibility) == PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult.Projected &&
        eligibility is { IsEligible: true };

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var connection = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await ServeClientAsync(connection, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or SocketException or OperationCanceledException)
                {
                    // Disconnects and I/O deadlines end the current HTTP response.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ServeClientAsync(TcpTransportConnection connection, CancellationToken cancellationToken)
    {
        using var headerDeadline = CreateDeadline(cancellationToken);
        var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
            connection, MaximumHttpHeaderBytes, headerDeadline.Token).ConfigureAwait(false);
        if (request.Method != "GET")
        {
            await WriteErrorAsync(connection, "405 Method Not Allowed\r\nAllow: GET", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.RequestTarget != StreamUri.AbsolutePath)
        {
            await WriteErrorAsync(connection, "404 Not Found", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.HttpVersion != HttpVersion.Version11 ||
            !request.Headers.TryGetValue("Host", out var host) ||
            !string.Equals(host, StreamUri.Authority, StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorAsync(connection, "400 Bad Request", cancellationToken).ConfigureAwait(false);
            return;
        }

        using var client = new PlaybackFmp4HttpClientBuffer(cancellationToken);
        if (!TrySubscribe(client))
        {
            await WriteErrorAsync(connection, "503 Service Unavailable", cancellationToken).ConfigureAwait(false);
            return;
        }

        var disconnect = WatchClientAsync(connection, client);
        var completed = false;
        try
        {
            if (!await WaitForBootstrapAsync(client).ConfigureAwait(false))
            {
                await WriteErrorAsync(connection, "503 Service Unavailable", client.Token).ConfigureAwait(false);
                return;
            }

            await WriteAsync(connection, SuccessHeader, client.Token).ConfigureAwait(false);
            await foreach (var fragment in client.Reader.ReadAllAsync(client.Token).ConfigureAwait(false))
            {
                if (fragment.Initialization is { } initialization)
                {
                    await WriteSegmentAsync(connection, initialization, client.Token).ConfigureAwait(false);
                }

                await WriteSegmentAsync(connection, fragment.Media, client.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    client.AcknowledgeWrite(fragment);
                }
            }

            await WriteAsync(connection, LastChunk, client.Token).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            client.Abort();
            await disconnect.ConfigureAwait(false);
            lock (_gate)
            {
                _client = null;
                client.Finished.TrySetResult(completed);
            }
        }
    }

    private bool TrySubscribe(PlaybackFmp4HttpClientBuffer client)
    {
        lock (_gate)
        {
            if (_completionTask is not null || _disposeTask is not null)
            {
                return false;
            }

            _client = client;
            _firstClient.TrySetResult(true);
            return true;
        }
    }

    private static async Task<bool> WaitForBootstrapAsync(PlaybackFmp4HttpClientBuffer client)
    {
        using var deadline = CreateDeadline(client.Token);
        try
        {
            return await client.Reader.WaitToReadAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!client.Token.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task WatchClientAsync(TcpTransportConnection connection, PlaybackFmp4HttpClientBuffer client)
    {
        try
        {
            // The response owns the connection after GET. EOF or extra input
            // ends the subscription, even if the publisher is idle.
            await connection.ReadAsync(new byte[1], client.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
        }
        finally
        {
            client.Abort();
        }
    }

    private static async Task WriteSegmentAsync(
        TcpTransportConnection connection, ReadOnlyMemory<byte> segment, CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        while (!segment.IsEmpty)
        {
            var length = Math.Min(segment.Length, MaximumHttpChunkBytes);
            var prefix = Encoding.ASCII.GetBytes(length.ToString("X", CultureInfo.InvariantCulture) + "\r\n");
            await connection.WriteAsync(prefix, deadline.Token).ConfigureAwait(false);
            await connection.WriteAsync(segment[..length], deadline.Token).ConfigureAwait(false);
            await connection.WriteAsync(ChunkSuffix, deadline.Token).ConfigureAwait(false);
            segment = segment[length..];
        }
    }

    private static Task WriteErrorAsync(TcpTransportConnection connection, string status, CancellationToken cancellationToken) =>
        WriteAsync(connection, Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"), cancellationToken);

    private static async Task WriteAsync(
        TcpTransportConnection connection, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        await connection.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
    }

    private static CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(IoTimeout);
        return deadline;
    }
}
