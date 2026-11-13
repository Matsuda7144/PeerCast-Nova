using System.Net;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

internal static class PcpRelayHostCandidateParser
{
    internal const int MaximumCandidates = 8;
    internal const int MaximumEndpointsPerCandidate = 2;
    internal const int MaximumFields = 32;
    internal const int MaximumHostBytes = 1024;

    private static readonly PcpAtomName Ip = PcpAtomName.FromAscii("ip");

    internal static PcpRelayHostCandidate? TryParse(PcpAtom atom)
    {
        ArgumentNullException.ThrowIfNull(atom);

        if (!atom.IsParent || atom.Name != PcpKnownAtomNames.Host)
        {
            return null;
        }

        if (atom.Children.Count > MaximumFields ||
            atom.Children.Sum(c => 8L + c.Content.Length) + 8 > MaximumHostBytes)
            return null;

        Guid? channelId = null;
        Guid? peerId = null;
        byte? flags = null;
        uint? relayCount = null;
        IPAddress? upstreamAddress = null;
        int? upstreamPort = null;
        var seen = new HashSet<string>();
        foreach (var field in atom.Children)
        {
            var name = field.Name.ToString();
            if (name is not ("cid" or "id" or "flg1" or "numr" or "upip" or "uppt"))
                continue;
            if (field.IsParent || !seen.Add(name)) return null;
            var length = field.Content.Length;
            if (name is "cid" or "id")
            {
                if (length != 16) return null;
                var id = PcpWireValueCodec.DecodeGuid(field.Content.Span);
                if (id == Guid.Empty) return null;
                if (name == "cid") channelId = id; else peerId = id;
            }
            else if (name == "flg1")
            {
                if (length != 1) return null;
                flags = field.Content.Span[0];
            }
            else if (name == "numr")
            {
                if (length != 4) return null;
                relayCount = PcpWireValueCodec.DecodeUInt32(field.Content.Span);
            }
            else if (name == "upip")
            {
                if (!TryDecodeAddress(field.Content.Span, out upstreamAddress)) return null;
            }
            else
            {
                if (!TryDecodePort(field.Content.Span, out var port)) return null;
                upstreamPort = port;
            }
        }

        var addressSlots = new IPAddress?[MaximumEndpointsPerCandidate];
        var portSlots = new int?[MaximumEndpointsPerCandidate];
        var addressOccurrenceCount = 0;
        var portOccurrenceCount = 0;

        foreach (var child in atom.Children)
        {
            if (child.Name == Ip)
            {
                // A known field name reserves its ordinal even when the node
                // is nested or otherwise malformed, so later fields cannot
                // shift into the malformed pair.
                var ordinal = addressOccurrenceCount;
                addressOccurrenceCount = checked(addressOccurrenceCount + 1);
                if (ordinal < MaximumEndpointsPerCandidate &&
                    !child.IsParent &&
                    TryDecodeAddress(child.Content.Span, out var address))
                {
                    addressSlots[ordinal] = address;
                }

                continue;
            }

            if (child.Name == PcpKnownAtomNames.HeloPort)
            {
                var ordinal = portOccurrenceCount;
                portOccurrenceCount = checked(portOccurrenceCount + 1);
                if (ordinal < MaximumEndpointsPerCandidate &&
                    !child.IsParent &&
                    TryDecodePort(child.Content.Span, out var port))
                {
                    portSlots[ordinal] = port;
                }
            }
        }

        PcpRelayHostEndpoint? globalEndpoint = null;
        PcpRelayHostEndpoint? localEndpoint = null;

        if (addressSlots[0] is { } globalAddress &&
            portSlots[0] is { } globalPort)
        {
            globalEndpoint = new(globalAddress, globalPort);
        }

        if (addressSlots[1] is { } localAddress &&
            portSlots[1] is { } localPort)
        {
            localEndpoint = new(localAddress, localPort);
        }

        if (globalEndpoint is null && localEndpoint is null)
        {
            return null;
        }

        return new(globalEndpoint, localEndpoint)
        {
            ChannelId = channelId,
            PeerSessionId = peerId,
            Flags = flags,
            RelayCount = relayCount,
            UpstreamEndpoint = upstreamAddress is not null && upstreamPort is { } upstreamPortValue
                ? new(upstreamAddress, upstreamPortValue) : null,
        };
    }

    private static bool TryDecodeAddress(
        ReadOnlySpan<byte> content,
        out IPAddress address)
    {
        address = null!;

        if (content.Length != 4)
        {
            return false;
        }

        try
        {
            address = PcpWireValueCodec.DecodeLegacyIpv4(content);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryDecodePort(
        ReadOnlySpan<byte> content,
        out int port)
    {
        port = 0;

        if (content.Length != sizeof(ushort))
        {
            return false;
        }

        try
        {
            var value = PcpWireValueCodec.DecodeUInt16(content);
            if (value == 0)
            {
                return false;
            }

            port = value;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
