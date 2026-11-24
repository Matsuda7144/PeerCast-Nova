using System.Net;

namespace PeerCast.Nova.Protocol.Pcp;

public static class PcpHeloCodec
{
    public const uint LegacyServantVersion = 1218;

    public static PcpAtom CreateHelo(
        string agent,
        Guid sessionId,
        ushort? openPort = null,
        ushort? pingPort = null,
        uint servantVersion = LegacyServantVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);

        if (openPort.HasValue && pingPort.HasValue)
        {
            throw new ArgumentException(
                "A HELO cannot advertise both an open port and a ping port.");
        }

        var children = new List<PcpAtom>
        {
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloAgent,
                PcpWireValueCodec.EncodeNullTerminatedUtf8(agent)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloSessionId,
                PcpWireValueCodec.EncodeGuid(sessionId)),
        };

        if (openPort.HasValue)
        {
            children.Add(
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloPort,
                    PcpWireValueCodec.EncodeUInt16(openPort.Value)));
        }
        else if (pingPort.HasValue)
        {
            children.Add(
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloPing,
                    PcpWireValueCodec.EncodeUInt16(pingPort.Value)));
        }

        children.Add(
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(servantVersion)));

        return PcpAtom.Parent(
            PcpKnownAtomNames.Helo,
            children);
    }

    public static PcpAtom CreateOleh(
        string agent,
        Guid sessionId,
        uint servantVersion = LegacyServantVersion,
        IPAddress? remoteIp = null,
        ushort? openPort = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);

        var children = new List<PcpAtom>
        {
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloAgent,
                PcpWireValueCodec.EncodeNullTerminatedUtf8(agent)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloSessionId,
                PcpWireValueCodec.EncodeGuid(sessionId)),
            PcpAtom.Leaf(
                PcpKnownAtomNames.HeloVersion,
                PcpWireValueCodec.EncodeUInt32(servantVersion)),
        };

        if (remoteIp is not null)
        {
            var ipv4 = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
            if (ipv4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                children.Add(
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.HeloRemoteIp,
                        PcpWireValueCodec.EncodeLegacyIpv4(ipv4)));
            }
        }

        if (openPort is > 0)
        {
            children.Add(
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloPort,
                    PcpWireValueCodec.EncodeUInt16(openPort.Value)));
        }

        return PcpAtom.Parent(
            PcpKnownAtomNames.Oleh,
            children);
    }

    public static PcpHeloInfo ParseHelo(PcpAtom atom)
    {
        ArgumentNullException.ThrowIfNull(atom);

        if (!atom.IsParent || atom.Name != PcpKnownAtomNames.Helo)
        {
            throw new InvalidDataException("Expected a PCP HELO parent atom.");
        }

        var sessionAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloSessionId,
            required: true);
        var agentAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloAgent,
            required: false);
        var versionAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloVersion,
            required: false);
        var portAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloPort,
            required: false);
        var pingAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloPing,
            required: false);

        if (portAtom is not null && pingAtom is not null)
        {
            throw new InvalidDataException(
                "HELO cannot advertise both a port and a ping port.");
        }

        return new PcpHeloInfo(
            PcpWireValueCodec.DecodeGuid(sessionAtom!.Content.Span),
            agentAtom is null
                ? null
                : PcpWireValueCodec.DecodeNullTerminatedUtf8(agentAtom.Content.Span),
            versionAtom is null
                ? null
                : PcpWireValueCodec.DecodeUInt32(versionAtom.Content.Span),
            portAtom is null
                ? null
                : PcpWireValueCodec.DecodeUInt16(portAtom.Content.Span),
            pingAtom is null
                ? null
                : PcpWireValueCodec.DecodeUInt16(pingAtom.Content.Span));
    }

    public static PcpOlehInfo ParseOleh(PcpAtom atom)
    {
        ArgumentNullException.ThrowIfNull(atom);

        if (!atom.IsParent || atom.Name != PcpKnownAtomNames.Oleh)
        {
            throw new InvalidDataException("Expected a PCP OLEH parent atom.");
        }

        var sessionAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloSessionId,
            required: true);
        var agentAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloAgent,
            required: false);
        var versionAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloVersion,
            required: false);
        var portAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloPort,
            required: false);
        var remoteIpAtom = FindUniqueLeaf(
            atom,
            PcpKnownAtomNames.HeloRemoteIp,
            required: false);

        return new PcpOlehInfo(
            PcpWireValueCodec.DecodeGuid(sessionAtom!.Content.Span),
            agentAtom is null
                ? null
                : PcpWireValueCodec.DecodeNullTerminatedUtf8(agentAtom.Content.Span),
            versionAtom is null
                ? null
                : PcpWireValueCodec.DecodeUInt32(versionAtom.Content.Span),
            portAtom is null
                ? null
                : PcpWireValueCodec.DecodeUInt16(portAtom.Content.Span),
            remoteIpAtom is null
                ? null
                : PcpWireValueCodec.DecodeLegacyIpv4(remoteIpAtom.Content.Span));
    }

    private static PcpAtom? FindUniqueLeaf(
        PcpAtom parent,
        PcpAtomName name,
        bool required)
    {
        PcpAtom? result = null;

        foreach (var child in parent.Children)
        {
            if (child.Name != name)
            {
                continue;
            }

            if (result is not null)
            {
                throw new InvalidDataException(
                    $"PCP atom '{parent.Name}' contains duplicate '{name}' fields.");
            }

            if (child.IsParent)
            {
                throw new InvalidDataException(
                    $"PCP field '{name}' must be a leaf atom.");
            }

            result = child;
        }

        if (required && result is null)
        {
            throw new InvalidDataException(
                $"PCP atom '{parent.Name}' has no '{name}' field.");
        }

        return result;
    }
}

public sealed record PcpHeloInfo(
    Guid SessionId,
    string? Agent,
    uint? ServantVersion,
    ushort? OpenPort,
    ushort? PingPort);

public sealed record PcpOlehInfo(
    Guid SessionId,
    string? Agent,
    uint? ServantVersion,
    ushort? RemotePort,
    IPAddress? RemoteAddress);
