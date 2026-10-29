using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>PCP v1 root announcements. These control documents contain no media packets.</summary>
public static class PcpAnnounceCodec
{
    public static readonly PcpAtomName Broadcast = PcpAtomName.FromAscii("bcst");
    public static readonly PcpAtomName BroadcastId = PcpAtomName.FromAscii("bcid");
    public static readonly PcpAtomName Disable = PcpAtomName.FromAscii("dis");

    public static PcpAtom CreateHelo(string agent, Guid senderSessionId, Guid broadcastId, ushort port)
    {
        ValidateIds(senderSessionId, broadcastId);
        if (port == 0) throw new ArgumentOutOfRangeException(nameof(port));
        var helo = PcpHeloCodec.CreateHelo(agent, senderSessionId, openPort: port);
        return PcpAtom.Parent(helo.Name, helo.Children.Concat(new[]
        {
            PcpAtom.Leaf(BroadcastId, PcpWireValueCodec.EncodeGuid(broadcastId)),
        }).ToArray());
    }

    /// <summary>OLEH is handshake evidence only; returned endpoint observations never grant publication capability.</summary>
    public static PcpOlehInfo ParseOleh(PcpAtom atom)
    {
        var info = PcpHeloCodec.ParseOleh(atom);
        if (info.SessionId == Guid.Empty) throw new InvalidDataException("Tracker SID is empty.");
        PcpAtom? disable = null;
        foreach (var child in atom.Children.Where(c => c.Name == Disable))
        {
            if (disable is not null || child.IsParent) throw new InvalidDataException("Invalid tracker disable field.");
            disable = child;
        }
        if (disable is not null && PcpWireValueCodec.DecodeUInt32(disable.Content.Span) != 0)
            throw new PcpAnnounceBannedException();
        return info;
    }

    public static PcpAtom CreateBroadcast(
        Guid channelId, Guid senderSessionId, Guid broadcastId,
        PcpAtom channelInfo, PcpAtom? channelTrack, IPAddress publicAddress, ushort publicPort,
        uint listeners, uint relays, uint uptimeSeconds, bool receiving,
        bool relayAvailable, bool directAvailable)
    {
        ValidateIds(senderSessionId, broadcastId);
        if (channelId == Guid.Empty) throw new ArgumentException("ChannelId is required.", nameof(channelId));
        ArgumentNullException.ThrowIfNull(publicAddress);
        if (publicAddress.AddressFamily != AddressFamily.InterNetwork || publicAddress.Equals(IPAddress.Any) ||
            publicAddress.GetAddressBytes()[0] is 0 or >= 224 || publicPort == 0)
            throw new ArgumentException("An explicit IPv4 endpoint is required.", nameof(publicAddress));
        ValidateSection(channelInfo, PcpKnownAtomNames.ChannelInfo);
        if (channelTrack is not null) ValidateSection(channelTrack, PcpKnownAtomNames.ChannelTrack);
        var channel = new List<PcpAtom>
        {
            GuidAtom("id", channelId), GuidAtom("bcid", broadcastId), channelInfo,
        };
        if (channelTrack is not null) channel.Add(channelTrack);
        // Tracker is the originating broadcast host. Relay/direct bits describe real capacity, not intent.
        var flags = (byte)(0x01 | (receiving ? 0x10 : 0) |
            (relayAvailable ? 0x02 : 0) | (directAvailable ? 0x04 : 0));
        var host = PcpAtom.Parent(PcpKnownAtomNames.Host,
            GuidAtom("id", senderSessionId), GuidAtom("cid", channelId),
            PcpAtom.Leaf(PcpAtomName.FromAscii("ip"), PcpWireValueCodec.EncodeLegacyIpv4(publicAddress)),
            PcpAtom.Leaf(PcpAtomName.FromAscii("port"), PcpWireValueCodec.EncodeUInt16(publicPort)),
            UIntAtom("numl", listeners), UIntAtom("numr", relays), UIntAtom("uptm", uptimeSeconds),
            UIntAtom("ver", PcpHeloCodec.LegacyServantVersion), ByteAtom("flg1", flags));
        return PcpAtom.Parent(Broadcast,
            ByteAtom("ttl", 1), ByteAtom("hops", 0), GuidAtom("from", senderSessionId),
            UIntAtom("vers", PcpHeloCodec.LegacyServantVersion), GuidAtom("cid", channelId),
            ByteAtom("grp", 1), PcpAtom.Parent(PcpKnownAtomNames.Channel, channel), host);
    }

    public static PcpAtom CreateQuit() => UIntAtom("quit", 1000);

    private static void ValidateIds(Guid sid, Guid bcid)
    {
        if (sid == Guid.Empty || bcid == Guid.Empty || sid == bcid)
            throw new ArgumentException("SID and BCID must be distinct nonempty identities.");
    }

    private static void ValidateSection(PcpAtom atom, PcpAtomName name)
    {
        ArgumentNullException.ThrowIfNull(atom);
        if (!atom.IsParent || atom.Name != name || atom.Children.Any(c => c.IsParent))
            throw new ArgumentException("Expected a flat metadata section.", nameof(atom));
        var allowed = name == PcpKnownAtomNames.ChannelInfo
            ? new[] { "type", "styp", "name", "gnre", "desc", "cmnt", "url", "sext", "bitr" }
            : new[] { "titl", "gnre", "albm", "crea", "url" };
        var seen = new HashSet<PcpAtomName>();
        foreach (var child in atom.Children)
        {
            if (!allowed.Any(value => child.Name == PcpAtomName.FromAscii(value)) || !seen.Add(child.Name))
                throw new ArgumentException("Unknown or duplicate public metadata field.", nameof(atom));
            if (child.Name == PcpKnownAtomNames.Bitrate)
            {
                if (PcpWireValueCodec.DecodeUInt32(child.Content.Span) == 0) throw new ArgumentException("Bitrate must be positive.", nameof(atom));
            }
            else if (child.Content.Length > 4096 || PcpWireValueCodec.DecodeNullTerminatedUtf8(child.Content.Span)
                .EnumerateRunes().Any(r => Rune.IsControl(r) || r.Value is 0x2028 or 0x2029))
                throw new ArgumentException("Invalid public metadata text.", nameof(atom));
        }
    }

    private static PcpAtom GuidAtom(string name, Guid value) => PcpAtom.Leaf(PcpAtomName.FromAscii(name), PcpWireValueCodec.EncodeGuid(value));
    private static PcpAtom UIntAtom(string name, uint value) => PcpAtom.Leaf(PcpAtomName.FromAscii(name), PcpWireValueCodec.EncodeUInt32(value));
    private static PcpAtom ByteAtom(string name, byte value) => PcpAtom.Leaf(PcpAtomName.FromAscii(name), new[] { value });
}

public sealed class PcpAnnounceBannedException : IOException
{
    public PcpAnnounceBannedException() : base("Tracker denied announcement.") { }
}
