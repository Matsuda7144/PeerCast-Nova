using System.Collections.Immutable;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

internal static class PcpReferralPlanner
{
    internal static ImmutableArray<PcpRelayHostCandidate> Select(
        PcpPeerDirectory directory, PcpRelayPeerIdentity requester)
    {
        var seen = new HashSet<PcpRelayHostEndpoint>();
        return directory.Snapshot(requester)
            // Station identifies a HOST by its SID. Never fabricate one.
            .Where(e => e.Host.PeerSessionId is { } sid && sid != Guid.Empty)
            .Where(e => e.Host.Firewalled != true)
            .OrderByDescending(e => e.Host.Receiving == true)
            .ThenByDescending(e => e.Host.RelayAvailable == true)
            .ThenByDescending(e => e.HasSucceeded)
            .ThenByDescending(e => e.ObservedAt)
            .ThenBy(e => e.Host.Hops ?? byte.MaxValue)
            .Where(e => seen.Add(e.Host.GlobalEndpoint!))
            .Take(PcpRelayHostCandidateParser.MaximumCandidates)
            .Select(e => e.Host).ToImmutableArray();
    }

    internal static PcpAtom CreateHost(PcpRelayHostCandidate host)
    {
        if (host.ChannelId is not { } channel || channel == Guid.Empty ||
            host.PeerSessionId is not { } sid || sid == Guid.Empty ||
            host.GlobalEndpoint is not { } endpoint ||
            !PcpRelayRedirectPolicy.Evaluate(new(endpoint, PcpRelayRedirectEndpointRole.Global), 0).IsEligible)
            throw new ArgumentException("A referral needs a channel, peer SID and eligible global IPv4 endpoint.", nameof(host));
        var fields = new List<PcpAtom>
        {
            PcpAtom.Leaf(PcpAtomName.FromAscii("id"), PcpWireValueCodec.EncodeGuid(sid)),
            PcpAtom.Leaf(PcpAtomName.FromAscii("cid"), PcpWireValueCodec.EncodeGuid(channel)),
            PcpAtom.Leaf(PcpAtomName.FromAscii("ip"), PcpWireValueCodec.EncodeLegacyIpv4(endpoint.Address)),
            PcpAtom.Leaf(PcpKnownAtomNames.HeloPort, PcpWireValueCodec.EncodeUInt16((ushort)endpoint.Port)),
        };
        // Phase B advertises only global IPv4; never forward a private LAN endpoint.
        if (host.Flags is { } flags)
            fields.Add(PcpAtom.Leaf(PcpAtomName.FromAscii("flg1"), new[] { flags }));
        if (host.RelayCount is { } relays)
            fields.Add(PcpAtom.Leaf(PcpAtomName.FromAscii("numr"), PcpWireValueCodec.EncodeUInt32(relays)));
        return PcpAtom.Parent(PcpKnownAtomNames.Host, fields);
    }
}
