using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.Network.Transport;

/// <summary>
/// Owns one connected TCP client and provides cancellable byte-oriented I/O.
/// Protocol parsing and media buffering are intentionally outside this type.
/// </summary>
public sealed class TcpTransportConnection : IAsyncDisposable
{
    private static long _nextConnectionId;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Stream _observedStream;
    internal PcpTransportCounter Traffic { get; }
    private readonly Action? _onDisposed;
    private readonly TaskCompletionSource _cleanupCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    internal TcpTransportConnection(TcpClient client, Action? onDisposed = null, PcpTransportStatistics? statistics = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _stream = client.GetStream();
        _onDisposed = onDisposed;

        LocalEndPoint = client.Client.LocalEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("The TCP connection has no local endpoint.");

        RemoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("The TCP connection has no remote endpoint.");

        ConnectionId = Interlocked.Increment(ref _nextConnectionId);
        Traffic = (statistics ?? PcpTransportStatistics.Shared).CreateCounter(ConnectionId);
        _observedStream = new TransportObservationStream(_stream, Traffic);
    }

    public long ConnectionId { get; }

    public IPEndPoint LocalEndPoint { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Observes actual transport cleanup, including the owner's disposal callback.
    /// IsDisposed only marks the start of disposal. Duplicate disposal remains a
    /// no-op; observers use this task rather than assuming that no-op proves cleanup.
    /// </summary>
    public Task CleanupCompletion => _cleanupCompletion.Task;

    internal IDisposable ObservePcpSession() => Traffic.Establish();

    internal Socket TransportSocket => _stream.Socket;

    internal Stream TransportStream
    {
        get
        {
            ThrowIfDisposed();
            return _observedStream;
        }
    }

    public ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _observedStream.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _observedStream.WriteAsync(buffer, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            try
            {
                _client.Client.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
                // The peer may already have closed or reset the connection.
            }
            catch (ObjectDisposedException)
            {
                // Disposal is idempotent.
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Traffic.Close();
            var transportClosed = false;
            try
            {
                // Also close the client when stream disposal faults.
                _client.Dispose();
                transportClosed = true;
            }
            finally
            {
                try { _onDisposed?.Invoke(); }
                finally
                {
                    if (transportClosed) _cleanupCompletion.TrySetResult();
                    else _cleanupCompletion.TrySetException(
                        new IOException("TCP transport cleanup could not be confirmed."));
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
}
