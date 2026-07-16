using System.Net.Sockets;

namespace PeerCast.Nova.Network.Transport;

internal static class TcpSocketConfigurator
{
    public static void Apply(TcpClient client, TcpTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        client.NoDelay = options.NoDelay;
        client.ReceiveBufferSize = options.ReceiveBufferSize;
        client.SendBufferSize = options.SendBufferSize;

        client.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.KeepAlive,
            options.KeepAlive);
    }
}
