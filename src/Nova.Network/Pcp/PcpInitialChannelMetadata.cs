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

    internal static bool HasAnyMetadata(ChannelMetadataSnapshot? metadata)
    {
        if (metadata is null) return false;
        return !string.IsNullOrEmpty(metadata.Name) ||
            !string.IsNullOrEmpty(metadata.Genre) ||
            !string.IsNullOrEmpty(metadata.Description) ||
            !string.IsNullOrEmpty(metadata.Comment) ||
            !string.IsNullOrEmpty(metadata.ContactUrl) ||
            !string.IsNullOrEmpty(metadata.ContentType) ||
            !string.IsNullOrEmpty(metadata.StreamType) ||
            !string.IsNullOrEmpty(metadata.StreamExtension) ||
            metadata.BitrateKbps is > 0 ||
            HasTrackMetadata(metadata.Track);
    }

    internal static ReadOnlyMemory<byte> TryEncode(Guid channelId, ChannelMetadataSnapshot? metadata)
    {
        if (channelId == Guid.Empty || !HasAnyMetadata(metadata))
        {
            return default;
        }

        try
        {
            return Encode(channelId, metadata!);
        }
        catch (ArgumentException)
        {
            return default;
        }
    }

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
        var includeTrack = HasTrackMetadata(metadata.Track);
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomWriter.WriteParentHeader(writer, PcpKnownAtomNames.Channel, includeTrack ? 3 : 2);
        PcpAtomWriter.WriteLeaf(writer, PcpKnownAtomNames.ChannelId, PcpWireValueCodec.EncodeGuid(channelId));
        PcpAtomWriter.WriteParentHeader(writer, PcpKnownAtomNames.ChannelInfo, values.Count + (metadata.BitrateKbps.HasValue ? 1 : 0));
        foreach (var (name, value) in values)
        {
            WriteValidatedUtf8Leaf(writer, name, value);
        }
        if (metadata.BitrateKbps is { } bitrate)
            PcpAtomWriter.WriteLeaf(writer, PcpKnownAtomNames.Bitrate, PcpWireValueCodec.EncodeUInt32(checked((uint)bitrate)));
        if (includeTrack && metadata.Track is { } track)
        {
            var trackValues = new (PcpAtomName Name, string? Value)[]
            {
                (PcpKnownAtomNames.Title, track.Title),
                (PcpKnownAtomNames.Genre, track.Genre),
                (PcpKnownAtomNames.Album, track.Album),
                (PcpKnownAtomNames.Creator, track.Creator),
                (PcpKnownAtomNames.Url, track.ContactUrl),
            };
            PcpAtomWriter.WriteParentHeader(writer, PcpKnownAtomNames.ChannelTrack, trackValues.Length);
            foreach (var (name, value) in trackValues)
            {
                WriteValidatedUtf8Leaf(writer, name, value);
            }
        }
        if (writer.WrittenCount > MaximumBytes) throw new ArgumentException("Initial metadata exceeds 32 KiB.", nameof(metadata));
        return writer.WrittenSpan.ToArray();
    }

    private static bool HasTrackMetadata(ChannelTrackMetadataSnapshot? track) =>
        track is not null &&
        (!string.IsNullOrEmpty(track.Title) ||
            !string.IsNullOrEmpty(track.Genre) ||
            !string.IsNullOrEmpty(track.Album) ||
            !string.IsNullOrEmpty(track.Creator) ||
            !string.IsNullOrEmpty(track.ContactUrl));

    private static void WriteValidatedUtf8Leaf(ArrayBufferWriter<byte> writer, PcpAtomName name, string? value)
    {
        var text = value ?? string.Empty;
        if (StrictUtf8.GetByteCount(text) + 1 > 4096 || text.EnumerateRunes().Any(r => Rune.IsControl(r) || r.Value is 0x2028 or 0x2029))
            throw new ArgumentException("Initial metadata text exceeds the PCP payload contract.", nameof(value));
        PcpAtomWriter.WriteLeaf(writer, name, PcpWireValueCodec.EncodeNullTerminatedUtf8(text));
    }
}
