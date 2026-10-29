using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>Numeric, pinned destination only. DNS resolution is separate and never follows tracker input.</summary>
internal sealed class PcpAnnounceProductionConnector : IPcpAnnounceConnector
{
    public async Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        if (!PublishingIpv4.IsPublicAddress(endpoint.Address.ToString()) || endpoint.Port is < 1 or > 65535)
            throw new ArgumentException("Only an explicit public IPv4 tracker is permitted.", nameof(endpoint));
        var pinned = new IPEndPoint(new IPAddress(endpoint.Address.GetAddressBytes()), endpoint.Port);
        var connection = await new TcpTransportConnector().ConnectAsync(pinned, cancellationToken).ConfigureAwait(false);
        return new PcpAnnounceLoopbackConnector.Connection(connection);
    }

    internal static async Task<IPEndPoint> ResolveAsync(PcpAnnounceEndpoint endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolver, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(endpoint.Uri.Host, out var literal) ? new[] { literal } :
            await resolver(endpoint.Uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var ipv4 = addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork).Distinct().ToArray();
        if (ipv4.Length == 0 || ipv4.Any(address => !PublishingIpv4.IsPublicAddress(address.ToString())))
            throw new InvalidDataException("Tracker DNS results contain no safe pinned IPv4 destination.");
        return new(new IPAddress(ipv4[0].GetAddressBytes()), endpoint.Port);
    }
}
