using System.Net;

namespace PeerCast.Nova.Network.Pcp;

internal sealed record PcpRelayHostCandidate(
    PcpRelayHostEndpoint? GlobalEndpoint,
    PcpRelayHostEndpoint? LocalEndpoint);

internal sealed record PcpRelayHostEndpoint(
    IPAddress Address,
    int Port);
