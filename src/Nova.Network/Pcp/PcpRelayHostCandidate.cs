using System.Net;

namespace PeerCast.Nova.Network.Pcp;

internal sealed record PcpRelayHostCandidate(
    PcpRelayHostEndpoint? GlobalEndpoint,
    PcpRelayHostEndpoint? LocalEndpoint)
{
    internal Guid? ChannelId { get; init; }
    internal Guid? PeerSessionId { get; init; }
    internal byte? Flags { get; init; }
    internal uint? RelayCount { get; init; }
    internal byte? Hops { get; init; }
    internal PcpRelayHostEndpoint? UpstreamEndpoint { get; init; }
    internal bool? Receiving => Flags is { } value ? (value & 0x10) != 0 : null;
    internal bool? RelayAvailable => Flags is { } value ? (value & 0x02) != 0 : null;
    internal bool? Firewalled => Flags is { } value ? (value & 0x08) != 0 : null;
}

internal sealed record PcpRelayHostEndpoint(
    IPAddress Address,
    int Port);
