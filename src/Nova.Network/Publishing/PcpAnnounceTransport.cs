using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Publishing;

internal interface IPcpAnnounceConnection : IAsyncDisposable
{
    Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    Task<PcpAtom> ReadAsync(CancellationToken cancellationToken);
    Task CloseAsync();
    void Abort();
}
internal interface IPcpAnnounceConnector
{
    Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken);
}

/// <summary>Foundation-only transport. It cannot resolve DNS or connect beyond IPv4 localhost.</summary>
internal sealed class PcpAnnounceLoopbackConnector : IPcpAnnounceConnector
{
    public async Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || !IPAddress.IsLoopback(endpoint.Address))
            throw new InvalidOperationException("External announce transport is disabled.");
        var connection = await new TcpTransportConnector().ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return new Connection(connection);
    }

    private sealed class Connection : IPcpAnnounceConnection
    {
        private readonly TcpTransportConnection _transport;
        private readonly PipeReader _pipe;
        private readonly PcpAtomPipeMessageReader _reader;
        private readonly object _gate = new();
        private Task? _close;
        private Task? _abort;
        private Task<PcpAtom>? _read;
        internal Connection(TcpTransportConnection transport)
        {
            _transport = transport;
            _pipe = PipeReader.Create(transport.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            _reader = new PcpAtomPipeMessageReader(_pipe, PcpAnnounceOptions.Limits, PcpAnnounceOptions.MaximumControlBytes);
        }
        public Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => _transport.WriteAsync(bytes, cancellationToken).AsTask();
        public Task<PcpAtom> ReadAsync(CancellationToken cancellationToken) => _read = _reader.ReadNextAsync(cancellationToken).AsTask();
        public void Abort()
        {
            _pipe.CancelPendingRead();
            // The actual transport closure and callback are observed by CloseAsync.
            lock (_gate) _abort ??= _transport.DisposeAsync().AsTask();
        }
        public Task CloseAsync() { lock (_gate) return _close ??= CloseCoreAsync(); }
        public ValueTask DisposeAsync() => new(CloseAsync());
        private async Task CloseCoreAsync()
        {
            Abort();
            await _abort!.ConfigureAwait(false);
            await _transport.CleanupCompletion.ConfigureAwait(false);
            if (_read is { } read)
            {
                try { await read.ConfigureAwait(false); }
                catch (Exception) { /* Transport closure completes the pending read. */ }
            }
            await _reader.DisposeAsync().ConfigureAwait(false);
            await _pipe.CompleteAsync().ConfigureAwait(false);
        }
    }
}
