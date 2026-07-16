using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Internal byte-write boundary for deterministic timeout validation. The
/// buffer remains valid only until the returned operation completes.
/// </summary>
internal interface IPcpDownstreamPacketWriter
{
    ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken);
}

internal sealed class TcpDownstreamPacketWriter :
    IPcpDownstreamPacketWriter
{
    private readonly TcpTransportConnection _connection;

    public TcpDownstreamPacketWriter(TcpTransportConnection connection)
    {
        _connection = connection;
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        return _connection.WriteAsync(buffer, cancellationToken);
    }
}
