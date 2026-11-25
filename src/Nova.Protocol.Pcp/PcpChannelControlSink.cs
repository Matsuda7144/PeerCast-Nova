using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Extracts bounded CHAN control fields from the incremental PCP decoder.
/// Packet data is measured but never copied or retained.
/// </summary>
public sealed class PcpChannelControlSink : IPcpAtomSink
{
    private readonly Action<PcpChannelControlMessage> _onMessage;
    private readonly PcpChannelControlLimits _limits;
    private readonly List<PcpAtomName> _parents = [];
    private ChannelBuilder? _channel;
    private int _channelDepth = -1;
    private bool _channelIsBroadcast;
    private Guid? _broadcastChannelId;

    public PcpChannelControlSink(
        Action<PcpChannelControlMessage> onMessage,
        PcpChannelControlLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(onMessage);

        _onMessage = onMessage;
        _limits = limits is null
            ? new PcpChannelControlLimits()
            : limits with { };
        _limits.Validate();
    }

    public void OnParentStarted(PcpParentStarted atom)
    {
        RequireStartDepth(atom.Depth);

        if (atom.Depth == 0 && atom.Header.Name == PcpKnownAtomNames.Broadcast)
        {
            _broadcastChannelId = null;
        }
        else if (atom.Depth == 0 && atom.Header.Name == PcpKnownAtomNames.Channel)
        {
            if (_channel is not null)
            {
                throw new InvalidDataException(
                    "A CHAN message started before the previous message ended.");
            }

            _channel = new ChannelBuilder();
            _channelDepth = 0;
            _channelIsBroadcast = false;
        }
        else if (atom.Depth == 1 &&
            _parents.Count > 0 &&
            _parents[0] == PcpKnownAtomNames.Broadcast &&
            atom.Header.Name == PcpKnownAtomNames.Channel)
        {
            if (_channel is null)
            {
                _channel = new ChannelBuilder();
                _channelDepth = 1;
                _channelIsBroadcast = true;
            }
        }
        else if (_channel is not null &&
            atom.Depth == _channelDepth + 1 &&
            _parents.Count > _channelDepth &&
            _parents[_channelDepth] == PcpKnownAtomNames.Channel)
        {
            _channel.StartSection(atom.Header.Name);
        }

        _parents.Add(atom.Header.Name);
    }

    public void OnLeafAtom(PcpLeafAtom atom)
    {
        RequireStartDepth(atom.Depth);

        if (atom.Depth == 0 && atom.Header.Name == PcpKnownAtomNames.Channel)
        {
            throw new InvalidDataException("A CHAN atom must be a parent.");
        }

        if (atom.Depth == 1 &&
            _parents.Count > 0 &&
            _parents[0] == PcpKnownAtomNames.Broadcast &&
            atom.Header.Name == PcpKnownAtomNames.BroadcastChannelId &&
            atom.Payload.Length == 16)
        {
            _broadcastChannelId = DecodeGuid(atom);
            return;
        }

        if (_channel is null ||
            _channelDepth < 0 ||
            _parents.Count <= _channelDepth ||
            _parents[_channelDepth] != PcpKnownAtomNames.Channel)
        {
            return;
        }

        if (atom.Depth == _channelDepth + 1)
        {
            if (atom.Header.Name == PcpKnownAtomNames.ChannelPacket ||
                atom.Header.Name == PcpKnownAtomNames.ChannelInfo ||
                atom.Header.Name == PcpKnownAtomNames.ChannelTrack)
            {
                throw new InvalidDataException(
                    $"CHAN section '{atom.Header.Name}' must be a parent.");
            }

            ParseChannelLeaf(atom, _channel);
            return;
        }

        if (atom.Depth != _channelDepth + 2)
        {
            return;
        }

        var section = _parents[_channelDepth + 1];

        if (section == PcpKnownAtomNames.ChannelPacket)
        {
            ParsePacketLeaf(atom, _channel);
        }
        else if (section == PcpKnownAtomNames.ChannelInfo)
        {
            ParseInfoLeaf(atom, _channel);
        }
        else if (section == PcpKnownAtomNames.ChannelTrack)
        {
            ParseTrackLeaf(atom, _channel);
        }
    }

    public void OnParentEnded(PcpParentEnded atom)
    {
        if (_parents.Count == 0 ||
            atom.Depth != _parents.Count - 1 ||
            atom.Name != _parents[^1])
        {
            throw new InvalidDataException(
                "The PCP parent callback sequence is inconsistent.");
        }

        PcpChannelControlMessage? completedMessage = null;

        if (_channel is not null &&
            atom.Depth == _channelDepth &&
            atom.Name == PcpKnownAtomNames.Channel)
        {
            var builder = _channel;
            var isBroadcast = _channelIsBroadcast;
            _channel = null;
            _channelDepth = -1;
            _channelIsBroadcast = false;

            if (isBroadcast)
            {
                builder.ChannelId ??= _broadcastChannelId;
                if (builder.ChannelId is { } broadcastId && broadcastId != Guid.Empty)
                {
                    completedMessage = builder.Build(isBroadcast: true);
                }
            }
            else
            {
                completedMessage = builder.Build(isBroadcast: false);
            }
        }
        else if (atom.Depth == 0 && atom.Name == PcpKnownAtomNames.Broadcast)
        {
            _broadcastChannelId = null;
        }

        _parents.RemoveAt(_parents.Count - 1);

        if (completedMessage is not null)
        {
            _onMessage(completedMessage);
        }
    }

    private static void ParseChannelLeaf(
        PcpLeafAtom atom,
        ChannelBuilder channel)
    {
        if (atom.Header.Name != PcpKnownAtomNames.ChannelId)
        {
            return;
        }

        channel.MarkField(
            PcpKnownAtomNames.Channel,
            PcpKnownAtomNames.ChannelId);

        channel.ChannelId = DecodeGuid(atom);
    }

    private static void ParsePacketLeaf(
        PcpLeafAtom atom,
        ChannelBuilder channel)
    {
        if (atom.Header.Name == PcpKnownAtomNames.Type)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelPacket,
                PcpKnownAtomNames.Type);
            channel.PacketType = DecodeUInt32(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Position)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelPacket,
                PcpKnownAtomNames.Position);
            channel.PacketPosition = DecodeUInt32(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Data)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelPacket,
                PcpKnownAtomNames.Data);
            channel.PacketDataLength = atom.Header.Value;
        }
        else if (atom.Header.Name == PcpKnownAtomNames.ChannelPacketContinuation)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelPacket,
                PcpKnownAtomNames.ChannelPacketContinuation);
            channel.PacketContinuation = DecodeByte(atom);
        }
    }

    private void ParseInfoLeaf(
        PcpLeafAtom atom,
        ChannelBuilder channel)
    {
        if (atom.Header.Name == PcpKnownAtomNames.Type)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.Type);
            channel.ContentType = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.StreamType)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.StreamType);
            channel.StreamType = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Name)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.Name);
            channel.Name = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Genre)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.Genre);
            channel.Genre = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Description)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.Description);
            channel.Description = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Comment)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.Comment);
            channel.Comment = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Url)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.Url);
            channel.InfoUrl = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.StreamExtension)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.StreamExtension);
            channel.StreamExtension = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Bitrate)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelInfo,
                PcpKnownAtomNames.Bitrate);
            channel.BitrateKbps = DecodeUInt32(atom);
        }
    }

    private void ParseTrackLeaf(
        PcpLeafAtom atom,
        ChannelBuilder channel)
    {
        if (atom.Header.Name == PcpKnownAtomNames.Title)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelTrack,
                PcpKnownAtomNames.Title);
            channel.Title = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Genre)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelTrack,
                PcpKnownAtomNames.Genre);
            channel.TrackGenre = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Album)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelTrack,
                PcpKnownAtomNames.Album);
            channel.Album = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Creator)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelTrack,
                PcpKnownAtomNames.Creator);
            channel.Creator = DecodeText(atom);
        }
        else if (atom.Header.Name == PcpKnownAtomNames.Url)
        {
            channel.MarkField(
                PcpKnownAtomNames.ChannelTrack,
                PcpKnownAtomNames.Url);
            channel.TrackUrl = DecodeText(atom);
        }
    }

    private string DecodeText(PcpLeafAtom atom)
    {
        if (atom.Header.Value > _limits.MaxTextBytes)
        {
            throw new InvalidDataException(
                $"PCP text field '{atom.Header.Name}' exceeds {_limits.MaxTextBytes} bytes.");
        }

        return PcpWireValueCodec.DecodeNullTerminatedUtf8(
            atom.CopyPayload());
    }

    private static Guid DecodeGuid(PcpLeafAtom atom)
    {
        RequireLength(atom, 16);
        Span<byte> value = stackalloc byte[16];
        atom.Payload.CopyTo(value);
        return PcpWireValueCodec.DecodeGuid(value);
    }

    private static uint DecodeUInt32(PcpLeafAtom atom)
    {
        RequireLength(atom, sizeof(uint));
        Span<byte> value = stackalloc byte[sizeof(uint)];
        atom.Payload.CopyTo(value);
        return PcpWireValueCodec.DecodeUInt32(value);
    }

    private static byte DecodeByte(PcpLeafAtom atom)
    {
        RequireLength(atom, sizeof(byte));
        Span<byte> value = stackalloc byte[sizeof(byte)];
        atom.Payload.CopyTo(value);
        return PcpWireValueCodec.DecodeByte(value);
    }

    private static void RequireLength(PcpLeafAtom atom, int expectedLength)
    {
        if (atom.Header.Value != expectedLength)
        {
            throw new InvalidDataException(
                $"PCP field '{atom.Header.Name}' must contain {expectedLength} bytes.");
        }
    }

    private void RequireStartDepth(int depth)
    {
        if (depth != _parents.Count)
        {
            throw new InvalidDataException(
                "The PCP atom callback depth is inconsistent.");
        }
    }

    private sealed class ChannelBuilder
    {
        private readonly HashSet<PcpAtomName> _sections = [];
        private readonly HashSet<(PcpAtomName Section, PcpAtomName Field)>
            _fields = [];

        public Guid? ChannelId { get; set; }

        public uint? PacketType { get; set; }

        public uint? PacketPosition { get; set; }

        public int? PacketDataLength { get; set; }

        public byte? PacketContinuation { get; set; }

        public string? ContentType { get; set; }

        public string? StreamType { get; set; }

        public string? Name { get; set; }

        public string? Genre { get; set; }

        public string? Description { get; set; }

        public string? Comment { get; set; }

        public string? InfoUrl { get; set; }

        public string? StreamExtension { get; set; }

        public uint? BitrateKbps { get; set; }

        public string? Title { get; set; }

        public string? TrackGenre { get; set; }

        public string? Album { get; set; }

        public string? Creator { get; set; }

        public string? TrackUrl { get; set; }

        public void StartSection(PcpAtomName section)
        {
            if (section != PcpKnownAtomNames.ChannelPacket &&
                section != PcpKnownAtomNames.ChannelInfo &&
                section != PcpKnownAtomNames.ChannelTrack)
            {
                return;
            }

            if (!_sections.Add(section))
            {
                throw new InvalidDataException(
                    $"CHAN contains duplicate '{section}' parents.");
            }
        }

        public void MarkField(PcpAtomName section, PcpAtomName field)
        {
            if (!_fields.Add((section, field)))
            {
                throw new InvalidDataException(
                    $"CHAN contains duplicate '{field}' fields in '{section}'.");
            }
        }

        public PcpChannelControlMessage Build(bool isBroadcast = false)
        {
            if (ChannelId is null || ChannelId == Guid.Empty)
            {
                throw new InvalidDataException(
                    "CHAN must contain a non-empty 16-byte channel ID.");
            }

            PcpChannelPacketHeader? packet = null;

            if (_sections.Contains(PcpKnownAtomNames.ChannelPacket))
            {
                if (PacketType is null ||
                    PacketPosition is null ||
                    PacketDataLength is null)
                {
                    throw new InvalidDataException(
                        "A CHAN packet must contain type, position, and data fields.");
                }

                packet = new PcpChannelPacketHeader(
                    PacketType.Value,
                    PacketPosition.Value,
                    PacketDataLength.Value,
                    PacketContinuation);
            }

            PcpChannelInfo? info = null;

            if (_sections.Contains(PcpKnownAtomNames.ChannelInfo))
            {
                info = new PcpChannelInfo(
                    ContentType,
                    StreamType,
                    Name,
                    Genre,
                    Description,
                    Comment,
                    InfoUrl,
                    StreamExtension,
                    BitrateKbps);
            }

            PcpChannelTrack? track = null;

            if (_sections.Contains(PcpKnownAtomNames.ChannelTrack))
            {
                track = new PcpChannelTrack(
                    Title,
                    TrackGenre,
                    Album,
                    Creator,
                    TrackUrl);
            }

            return new PcpChannelControlMessage(
                ChannelId.Value,
                packet,
                info,
                track,
                isBroadcast);
        }
    }
}
