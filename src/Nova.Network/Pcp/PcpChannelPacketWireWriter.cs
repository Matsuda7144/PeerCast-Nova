using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Writes the fixed CHAN/id/pkt/type/pos/data prefix into caller-owned memory.
/// The data header declares the payload length, but the payload itself remains
/// in the fan-out's shared bounded memory and is written separately.
/// </summary>
internal static class PcpChannelPacketWireWriter
{
    public const int PrefixLength =
        PcpAtomCodec.HeaderSize +
        PcpAtomCodec.HeaderSize + 16 +
        PcpAtomCodec.HeaderSize +
        PcpAtomCodec.HeaderSize + sizeof(uint) +
        PcpAtomCodec.HeaderSize + sizeof(uint) +
        PcpAtomCodec.HeaderSize;

    public static int WritePrefix(
        Span<byte> destination,
        Guid channelId,
        PcpRelayPacket packet,
        int maxPacketBytes)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (channelId == Guid.Empty)
        {
            throw new ArgumentException(
                "The channel identifier cannot be empty.",
                nameof(channelId));
        }

        if (maxPacketBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPacketBytes),
                maxPacketBytes,
                "The packet limit cannot be negative.");
        }

        if (packet.Payload.Length > maxPacketBytes)
        {
            throw new InvalidDataException(
                "The relay packet exceeds the downstream packet limit.");
        }

        if (destination.Length < PrefixLength)
        {
            throw new ArgumentException(
                $"The destination must contain at least {PrefixLength} bytes.",
                nameof(destination));
        }

        var offset = 0;
        WriteHeader(
            destination,
            ref offset,
            PcpKnownAtomNames.Channel,
            isParent: true,
            value: 2);
        WriteHeader(
            destination,
            ref offset,
            PcpKnownAtomNames.ChannelId,
            isParent: false,
            value: 16);
        PcpWireValueCodec.WriteGuid(destination[offset..], channelId);
        offset += 16;

        WriteHeader(
            destination,
            ref offset,
            PcpKnownAtomNames.ChannelPacket,
            isParent: true,
            value: 3);
        WriteHeader(
            destination,
            ref offset,
            PcpKnownAtomNames.Type,
            isParent: false,
            value: sizeof(uint));
        PcpWireValueCodec.WriteUInt32(destination[offset..], packet.Type);
        offset += sizeof(uint);

        WriteHeader(
            destination,
            ref offset,
            PcpKnownAtomNames.Position,
            isParent: false,
            value: sizeof(uint));
        PcpWireValueCodec.WriteUInt32(
            destination[offset..],
            packet.WirePosition);
        offset += sizeof(uint);

        WriteHeader(
            destination,
            ref offset,
            PcpKnownAtomNames.Data,
            isParent: false,
            value: packet.Payload.Length);

        return offset;
    }

    private static void WriteHeader(
        Span<byte> destination,
        ref int offset,
        PcpAtomName name,
        bool isParent,
        int value)
    {
        PcpAtomCodec.WriteHeader(
            destination[offset..],
            new PcpAtomHeader(name, isParent, value));
        offset += PcpAtomCodec.HeaderSize;
    }
}
