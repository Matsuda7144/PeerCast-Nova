using System.Net;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

internal interface IPcpChannelTransportConnector
{
    ValueTask<TcpTransportConnection> ConnectOriginalAsync(
        string host,
        int port,
        CancellationToken cancellationToken);

    ValueTask<TcpTransportConnection> ConnectRedirectAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken);
}

internal sealed class TcpTransportConnectorAdapter
    : IPcpChannelTransportConnector
{
    private readonly TcpTransportConnector _connector;

    internal TcpTransportConnectorAdapter(TcpTransportConnector connector)
    {
        ArgumentNullException.ThrowIfNull(connector);
        _connector = connector;
    }

    public ValueTask<TcpTransportConnection> ConnectOriginalAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        return _connector.ConnectAsync(host, port, cancellationToken);
    }

    public ValueTask<TcpTransportConnection> ConnectRedirectAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        return _connector.ConnectAsync(endpoint, cancellationToken);
    }
}
