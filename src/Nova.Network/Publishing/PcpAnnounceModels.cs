using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Publishing;

internal sealed record PcpAnnounceEndpoint
{
    private PcpAnnounceEndpoint(Uri uri) => Uri = uri;
    internal Uri Uri { get; }
    internal static PcpAnnounceEndpoint Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        var slash = separator < 0 ? -1 : value.IndexOf('/', separator + 3);
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != "pcp" || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath is not ("" or "/") || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Port is 0 or > 65535 || uri.HostNameType == UriHostNameType.IPv6 ||
            value != value.Trim() || value.Contains('\\', StringComparison.Ordinal) ||
            (slash >= 0 && slash != value.Length - 1) || value.Any(char.IsControl))
            throw new ArgumentException("An explicit PCP IPv4 tracker endpoint is required.", nameof(value));
        return new(uri);
    }
    internal int Port => Uri.Port < 0 ? 7144 : Uri.Port;
}

/// <summary>No production Ready factory exists in this slice. Tests can authorize only a numeric loopback destination.</summary>
internal sealed class PcpAnnounceAccess
{
    private readonly IPEndPoint? _loopback;
    private PcpAnnounceAccess(bool enabled, IPEndPoint? loopback) { Enabled = enabled; _loopback = loopback; }
    internal static PcpAnnounceAccess Disabled { get; } = new(false, null);
    internal static PcpAnnounceAccess NotReady { get; } = new(true, null);
    internal bool Enabled { get; }
    internal static PcpAnnounceAccess ForLoopbackFixture(IPEndPoint endpoint)
    {
        if (endpoint.AddressFamily != AddressFamily.InterNetwork || !IPAddress.IsLoopback(endpoint.Address) || endpoint.Port == 0)
            throw new ArgumentException("Only an IPv4 localhost fixture is permitted.", nameof(endpoint));
        return new(true, new IPEndPoint(new IPAddress(endpoint.Address.GetAddressBytes()), endpoint.Port));
    }
    internal IPEndPoint? Resolve(PcpAnnounceEndpoint endpoint) =>
        _loopback is not null && endpoint.Port == _loopback.Port &&
        IPAddress.TryParse(endpoint.Uri.Host, out var address) && address.Equals(_loopback.Address)
            ? new IPEndPoint(_loopback.Address, _loopback.Port) : null;
}

internal enum PcpAnnounceState
{
    Disabled, NotReady, Connecting, Handshaking, Connected, AnnounceSent, UpdateSent,
    Failed, Stopping, StopNoticeSent, Stopped, CleanupIncomplete,
}
internal enum PcpAnnounceFailure { None, Connection, Timeout, Cancelled, Protocol, Banned, TrackerQuit, Transport, Cleanup }
internal enum PcpAnnounceStopReason { Finish, ObsNormalEos, Dispose, Cancelled }

/// <summary>
/// Generation is local ownership. PublishingSessionId is the host's generation ID.
/// Current ObsPublishChannelHost also uses that value in downstream OLEH; the explicit
/// binding below records this existing relay contract, not a universal node-SID rule.
/// BCID belongs to the owner across generations and is never the SID.
/// </summary>
internal sealed record PcpAnnounceIdentity(long Generation, Guid ChannelId, Guid PublishingSessionId, Guid SenderSessionId, Guid BroadcastId);

internal sealed record PcpAnnounceResult(PcpAnnounceIdentity Identity, PcpAnnounceFailure Failure,
    bool AnnounceSent, bool StopNoticeSent, bool QuitSent, bool CleanupProven, PcpAnnounceStopReason? StopReason);

internal sealed record PcpAnnounceOptions
{
    internal static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(30);
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    internal TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    internal TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(10);
    internal TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
    internal const int MaximumControlBytes = 64 * 1024;
    internal static PcpProtocolLimits Limits => new() { MaxContentLength = MaximumControlBytes, MaxChildCount = 128, MaxNestingDepth = 8 };
    internal void Validate()
    {
        foreach (var value in new[] { ConnectTimeout, HandshakeTimeout, WriteTimeout, ShutdownTimeout })
            if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(value));
    }
}

/// <summary>Immutable allowlisted public metadata. No settings bag, stream key or filesystem path enters the wire builder.</summary>
internal sealed class PcpAnnounceDocument
{
    private readonly PcpAtom _info;
    private readonly PcpAtom? _track;
    private readonly IPAddress _address;
    internal ushort Port { get; }
    internal bool HasSameEndpoint(PcpAnnounceDocument other) => Port == other.Port && _address.Equals(other._address);
    private readonly uint _listeners, _relays, _uptime;
    private readonly bool _relay, _direct;
    private static readonly Guid MetadataChannelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    internal PcpAnnounceDocument(ChannelMetadataSnapshot metadata, IPEndPoint publicEndpoint,
        uint listeners = 0, uint relays = 0, uint uptimeSeconds = 0, bool relayAvailable = false, bool directAvailable = false)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(publicEndpoint);
        if (publicEndpoint.AddressFamily != AddressFamily.InterNetwork || publicEndpoint.Port == 0)
            throw new ArgumentException("IPv4 endpoint required.", nameof(publicEndpoint));
        _address = new IPAddress(publicEndpoint.Address.GetAddressBytes());
        Port = checked((ushort)publicEndpoint.Port);
        _listeners = listeners; _relays = relays; _uptime = uptimeSeconds; _relay = relayAvailable; _direct = directAvailable;
        ValidatePublicUrl(metadata.ContactUrl);
        ValidatePublicUrl(metadata.Track?.ContactUrl);
        var bytes = PcpInitialChannelMetadata.Encode(MetadataChannelId, metadata);
        var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(bytes));
        if (!PcpAtomTreeCodec.TryRead(ref reader, PcpAnnounceOptions.Limits, out var channel, out _))
            throw new ArgumentException("Invalid channel metadata.", nameof(metadata));
        _info = channel.FindFirst(PcpKnownAtomNames.ChannelInfo)!;
        if (metadata.Track is { } track)
        {
            _track = PcpAtom.Parent(PcpKnownAtomNames.ChannelTrack,
                Text("titl", track.Title), Text("gnre", track.Genre), Text("albm", track.Album),
                Text("crea", track.Creator), Text("url", track.ContactUrl));
        }
        // Validate total outgoing size at snapshot creation, before any connection.
        var sid = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var bcid = Guid.Parse("00000000-0000-0000-0000-000000000003");
        Encode(new(1, MetadataChannelId, sid, sid, bcid), true);
    }
    internal byte[] Encode(PcpAnnounceIdentity identity, bool receiving)
    {
        var atom = PcpAnnounceCodec.CreateBroadcast(identity.ChannelId, identity.SenderSessionId, identity.BroadcastId,
            _info, _track, _address, Port, _listeners, _relays, _uptime, receiving, _relay, _direct);
        return EncodeAtom(atom);
    }
    internal static byte[] EncodeAtom(PcpAtom atom)
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, atom, PcpAnnounceOptions.Limits);
        if (writer.WrittenCount > PcpAnnounceOptions.MaximumControlBytes) throw new ArgumentException("Announcement exceeds its wire budget.", nameof(atom));
        return writer.WrittenSpan.ToArray();
    }
    private static PcpAtom Text(string name, string? text)
    {
        text ??= string.Empty;
        if (new UTF8Encoding(false, true).GetByteCount(text) + 1 > 4096 || text.EnumerateRunes().Any(r => Rune.IsControl(r) || r.Value is 0x2028 or 0x2029))
            throw new ArgumentException("Track metadata exceeds its public text contract.", nameof(text));
        return PcpAtom.Leaf(PcpAtomName.FromAscii(name), PcpWireValueCodec.EncodeNullTerminatedUtf8(text));
    }
    private static void ValidatePublicUrl(string? text)
    {
        if (!string.IsNullOrEmpty(text) && (!System.Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0))
            throw new ArgumentException("Public metadata URL must be HTTP(S), without credentials.", nameof(text));
    }
}
