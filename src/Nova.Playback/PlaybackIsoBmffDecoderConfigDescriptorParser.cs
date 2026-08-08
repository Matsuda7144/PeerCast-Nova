using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffDecoderConfigDescriptorParseResult
{
    Parsed,
    InvalidConfiguration,
    MissingElementaryStreamDescriptor,
    InvalidHierarchy,
    InvalidPayload,
    UnsupportedStreamType,
    DescriptorLimitExceeded,
    DescriptorPayloadLimitExceeded,
    InvalidChildSequence,
    MissingDecoderSpecificInfo,
    DuplicateDecoderSpecificInfo,
    InvalidDecoderSpecificInfo,
}

public sealed record PlaybackIsoBmffDecoderConfigDescriptorParserOptions
{
    public const int DefaultMaximumDescriptorCount = 64;
    public const int MaximumDescriptorCountHardLimit = 256;

    public int MaximumDescriptorCount { get; init; } =
        DefaultMaximumDescriptorCount;

    public int MaximumDecoderSpecificBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumCodecExtradataBytes;
}

public sealed class PlaybackIsoBmffDecoderConfigDescriptor
{
    internal PlaybackIsoBmffDecoderConfigDescriptor(
        PlaybackIsoBmffElementaryStreamDescriptor elementaryStream,
        byte objectTypeIndication,
        byte streamType,
        bool upstream,
        uint bufferSizeBytes,
        uint maximumBitrate,
        uint averageBitrate,
        ReadOnlyCollection<PlaybackIsoBmffDescriptorLocation> children,
        PlaybackIsoBmffDescriptorLocation decoderSpecificInfoDescriptor)
    {
        ElementaryStream = elementaryStream;
        ObjectTypeIndication = objectTypeIndication;
        StreamType = streamType;
        Upstream = upstream;
        BufferSizeBytes = bufferSizeBytes;
        MaximumBitrate = maximumBitrate;
        AverageBitrate = averageBitrate;
        Children = children;
        DecoderSpecificInfoDescriptor = decoderSpecificInfoDescriptor;
    }

    public PlaybackIsoBmffElementaryStreamDescriptor ElementaryStream { get; }

    public byte ObjectTypeIndication { get; }

    public byte StreamType { get; }

    public bool Upstream { get; }

    public uint BufferSizeBytes { get; }

    public uint MaximumBitrate { get; }

    public uint AverageBitrate { get; }

    public IReadOnlyList<PlaybackIsoBmffDescriptorLocation> Children { get; }

    public PlaybackIsoBmffDescriptorLocation DecoderSpecificInfoDescriptor
    {
        get;
    }

    public bool TryGetPayload(
        PlaybackIsoBmffDescriptorLocation? location,
        out ReadOnlyMemory<byte> payload)
    {
        payload = default;
        if (location is null)
        {
            return false;
        }

        var found = false;
        foreach (var child in Children)
        {
            if (ReferenceEquals(child, location))
            {
                found = true;
                break;
            }
        }

        if (!found || location.PayloadOffset < 0 ||
            location.Header.PayloadLength < 0 ||
            location.PayloadOffset > ElementaryStream.DescriptorBytes.Length -
                location.Header.PayloadLength)
        {
            return false;
        }

        payload = ElementaryStream.DescriptorBytes.Slice(
            location.PayloadOffset,
            location.Header.PayloadLength);
        return true;
    }
}

public static class PlaybackIsoBmffDecoderConfigDescriptorParser
{
    private const int FixedPayloadLength = 13;
    private const byte AudioStreamType = 0x05;
    private const byte DecoderSpecificInfoTag = 0x05;

    public static PlaybackIsoBmffDecoderConfigDescriptorParseResult Parse(
        PlaybackIsoBmffElementaryStreamDescriptor? elementaryStream,
        out PlaybackIsoBmffDecoderConfigDescriptor? decoderConfig,
        PlaybackIsoBmffDecoderConfigDescriptorParserOptions? options = null)
    {
        decoderConfig = null;
        options ??= new PlaybackIsoBmffDecoderConfigDescriptorParserOptions();
        if (options.MaximumDescriptorCount is < 1 or
                > PlaybackIsoBmffDecoderConfigDescriptorParserOptions
                    .MaximumDescriptorCountHardLimit ||
            options.MaximumDecoderSpecificBytes is < 1 or
                > PlaybackBoundedDemuxOptions
                    .MaximumCodecExtradataBytesHardLimit)
        {
            return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .InvalidConfiguration;
        }

        if (elementaryStream is null)
        {
            return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .MissingElementaryStreamDescriptor;
        }

        if (!elementaryStream.TryGetPayload(
                elementaryStream.DecoderConfigDescriptor,
                out var payloadMemory))
        {
            return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .InvalidHierarchy;
        }

        var payload = payloadMemory.Span;
        if (payload.Length < FixedPayloadLength || (payload[1] & 0x01) == 0)
        {
            return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .InvalidPayload;
        }

        var streamType = (byte)((payload[1] >> 2) & 0x3F);
        if (streamType != AudioStreamType)
        {
            return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .UnsupportedStreamType;
        }

        var bufferSizeBytes =
            ((uint)payload[2] << 16) |
            ((uint)payload[3] << 8) |
            payload[4];
        var children = new List<PlaybackIsoBmffDescriptorLocation>();
        PlaybackIsoBmffDescriptorLocation? decoderSpecificInfo = null;
        var offset = FixedPayloadLength;
        var headerOptions = new PlaybackIsoBmffDescriptorHeaderParserOptions
        {
            MaximumPayloadBytes = options.MaximumDecoderSpecificBytes,
        };
        while (offset != payload.Length)
        {
            if (children.Count >= options.MaximumDescriptorCount)
            {
                return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                    .DescriptorLimitExceeded;
            }

            var childResult = PlaybackIsoBmffDescriptorHeaderParser.Parse(
                    payload[offset..],
                    out var childHeader,
                    headerOptions,
                    requireExactLength: false);
            if (childResult ==
                PlaybackIsoBmffDescriptorHeaderParseResult.PayloadLimitExceeded)
            {
                return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                    .DescriptorPayloadLimitExceeded;
            }

            if (childResult !=
                    PlaybackIsoBmffDescriptorHeaderParseResult.Parsed ||
                childHeader is null)
            {
                return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                    .InvalidChildSequence;
            }

            var location = new PlaybackIsoBmffDescriptorLocation(
                childHeader,
                elementaryStream.DecoderConfigDescriptor.PayloadOffset +
                    offset);
            if (childHeader.Tag == DecoderSpecificInfoTag)
            {
                if (decoderSpecificInfo is not null)
                {
                    return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                        .DuplicateDecoderSpecificInfo;
                }

                if (childHeader.PayloadLength == 0)
                {
                    return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                        .InvalidDecoderSpecificInfo;
                }

                decoderSpecificInfo = location;
            }

            children.Add(location);
            offset += childHeader.TotalLength;
        }

        if (decoderSpecificInfo is null)
        {
            return PlaybackIsoBmffDecoderConfigDescriptorParseResult
                .MissingDecoderSpecificInfo;
        }

        decoderConfig = new(
            elementaryStream,
            payload[0],
            streamType,
            (payload[1] & 0x02) != 0,
            bufferSizeBytes,
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(5, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(9, 4)),
            children.AsReadOnly(),
            decoderSpecificInfo);
        return PlaybackIsoBmffDecoderConfigDescriptorParseResult.Parsed;
    }
}
