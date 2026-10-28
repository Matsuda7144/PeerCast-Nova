using System.Buffers;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>One bounded control document, never a media packet or fanout input.</summary>
internal static class PcpInitialChannelMetadata
{
    internal const int MaximumBytes = 32 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] Encode(Guid channelId, ChannelMetadataSnapshot metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (channelId == Guid.Empty) throw new ArgumentException("Channel identity is required.", nameof(channelId));
        var values = new List<(PcpAtomName Name, string? Value)>
        {
            (PcpKnownAtomNames.Type, metadata.ContentType), (PcpKnownAtomNames.Name, metadata.Name),
            (PcpKnownAtomNames.Genre, metadata.Genre), (PcpKnownAtomNames.Description, metadata.Description),
            (PcpKnownAtomNames.Comment, metadata.Comment), (PcpKnownAtomNames.Url, metadata.ContactUrl),
            (PcpKnownAtomNames.StreamExtension, metadata.StreamExtension),
        };
        if (metadata.StreamType is not null) values.Add((PcpKnownAtomNames.StreamType, metadata.StreamType));
        if (metadata.BitrateKbps is <= 0) throw new ArgumentException("Declared bitrate must be positive.", nameof(metadata));
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomWriter.WriteParentHeader(writer, PcpKnownAtomNames.Channel, 2);
        PcpAtomWriter.WriteLeaf(writer, PcpKnownAtomNames.ChannelId, PcpWireValueCodec.EncodeGuid(channelId));
        PcpAtomWriter.WriteParentHeader(writer, PcpKnownAtomNames.ChannelInfo, values.Count + (metadata.BitrateKbps.HasValue ? 1 : 0));
        foreach (var (name, value) in values)
        {
            var text = value ?? string.Empty;
            if (StrictUtf8.GetByteCount(text) + 1 > 4096 || text.EnumerateRunes().Any(r => Rune.IsControl(r) || r.Value is 0x2028 or 0x2029))
                throw new ArgumentException("Initial metadata text exceeds the PCP payload contract.", nameof(metadata));
            PcpAtomWriter.WriteLeaf(writer, name, PcpWireValueCodec.EncodeNullTerminatedUtf8(text));
        }
        if (metadata.BitrateKbps is { } bitrate)
            PcpAtomWriter.WriteLeaf(writer, PcpKnownAtomNames.Bitrate, PcpWireValueCodec.EncodeUInt32(checked((uint)bitrate)));
        if (writer.WrittenCount > MaximumBytes) throw new ArgumentException("Initial metadata exceeds 32 KiB.", nameof(metadata));
        return writer.WrittenSpan.ToArray();
    }
}
