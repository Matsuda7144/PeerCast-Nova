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
    private readonly Action? _onDisposed;
    private int _disposed;

    internal TcpTransportConnection(TcpClient client, Action? onDisposed = null)
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
    }

    public long ConnectionId { get; }

    public IPEndPoint LocalEndPoint { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal NetworkStream TransportStream
    {
        get
        {
            ThrowIfDisposed();
            return _stream;
        }
    }

    public ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _stream.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _stream.WriteAsync(buffer, cancellationToken);
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
            _client.Dispose();
        }
        finally
        {
            _onDisposed?.Invoke();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
}
