using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffElementaryStreamDescriptorParseResult
{
    Parsed,
    InvalidConfiguration,
    MissingAudioEntry,
    UnsupportedAudioEntry,
    MissingElementaryStreamBox,
    DuplicateElementaryStreamBox,
    BoxLocationMismatch,
    ConfigurationLimitExceeded,
    InvalidFullBox,
    InvalidDescriptor,
    UnsupportedDescriptor,
    DescriptorLimitExceeded,
    InvalidChildSequence,
    MissingDecoderConfigDescriptor,
    DuplicateDecoderConfigDescriptor,
    MissingSlConfigDescriptor,
    DuplicateSlConfigDescriptor,
}

public sealed record PlaybackIsoBmffElementaryStreamDescriptorParserOptions
{
    public const int DefaultMaximumDescriptorCount = 64;
    public const int MaximumDescriptorCountHardLimit = 256;

    public int MaximumDescriptorCount { get; init; } =
        DefaultMaximumDescriptorCount;

    public int MaximumConfigurationBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumCodecExtradataBytes;
}

public sealed class PlaybackIsoBmffDescriptorLocation
{
    internal PlaybackIsoBmffDescriptorLocation(
        PlaybackIsoBmffDescriptorHeader header,
        int offset)
    {
        Header = header;
        Offset = offset;
    }

    public PlaybackIsoBmffDescriptorHeader Header { get; }

    public int Offset { get; }

    public int PayloadOffset => Offset + Header.HeaderLength;

    public int NextOffset => Offset + Header.TotalLength;
}

public sealed class PlaybackIsoBmffElementaryStreamDescriptor
{
    private readonly byte[] _descriptorBytes;
    private readonly byte[] _url;

    internal PlaybackIsoBmffElementaryStreamDescriptor(
        PlaybackIsoBmffAudioSampleEntry audioEntry,
        PlaybackIsoBmffBoxLocation elementaryStreamBox,
        ushort elementaryStreamId,
        byte streamPriority,
        ushort? dependsOnElementaryStreamId,
        byte[] url,
        ushort? ocrElementaryStreamId,
        ReadOnlyCollection<PlaybackIsoBmffDescriptorLocation> children,
        PlaybackIsoBmffDescriptorLocation decoderConfigDescriptor,
        PlaybackIsoBmffDescriptorLocation slConfigDescriptor,
        byte[] descriptorBytes)
    {
        AudioEntry = audioEntry;
        ElementaryStreamBox = elementaryStreamBox;
        ElementaryStreamId = elementaryStreamId;
        StreamPriority = streamPriority;
        DependsOnElementaryStreamId = dependsOnElementaryStreamId;
        _url = url;
        OcrElementaryStreamId = ocrElementaryStreamId;
        Children = children;
        DecoderConfigDescriptor = decoderConfigDescriptor;
        SlConfigDescriptor = slConfigDescriptor;
        _descriptorBytes = descriptorBytes;
    }

    public PlaybackIsoBmffAudioSampleEntry AudioEntry { get; }

    public PlaybackIsoBmffBoxLocation ElementaryStreamBox { get; }

    public ushort ElementaryStreamId { get; }

    public byte StreamPriority { get; }

    public ushort? DependsOnElementaryStreamId { get; }

    public ReadOnlyMemory<byte> Url => _url;

    public ushort? OcrElementaryStreamId { get; }

    public IReadOnlyList<PlaybackIsoBmffDescriptorLocation> Children { get; }

    public PlaybackIsoBmffDescriptorLocation DecoderConfigDescriptor { get; }

    public PlaybackIsoBmffDescriptorLocation SlConfigDescriptor { get; }

    public ReadOnlyMemory<byte> DescriptorBytes => _descriptorBytes;

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
            location.PayloadOffset >
                _descriptorBytes.Length - location.Header.PayloadLength)
        {
            return false;
        }

        payload = _descriptorBytes.AsMemory(
            location.PayloadOffset,
            location.Header.PayloadLength);
        return true;
    }
}

public static class PlaybackIsoBmffElementaryStreamDescriptorParser
{
    private const uint ElementaryStreamBoxType = 0x65736473;
    private const byte ElementaryStreamDescriptorTag = 0x03;
    private const byte DecoderConfigDescriptorTag = 0x04;
    private const byte SlConfigDescriptorTag = 0x06;

    public static PlaybackIsoBmffElementaryStreamDescriptorParseResult Parse(
        ReadOnlySpan<byte> sampleDescriptionBox,
        PlaybackIsoBmffAudioSampleEntry? audioEntry,
        out PlaybackIsoBmffElementaryStreamDescriptor? descriptor,
        PlaybackIsoBmffElementaryStreamDescriptorParserOptions? options = null)
    {
        descriptor = null;
        options ??=
            new PlaybackIsoBmffElementaryStreamDescriptorParserOptions();
        if (options.MaximumDescriptorCount is < 2 or
                > PlaybackIsoBmffElementaryStreamDescriptorParserOptions
                    .MaximumDescriptorCountHardLimit ||
            options.MaximumConfigurationBytes is < 1 or
                > PlaybackBoundedDemuxOptions
                    .MaximumCodecExtradataBytesHardLimit)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .InvalidConfiguration;
        }

        if (audioEntry is null)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingAudioEntry;
        }

        if (audioEntry.Selection.CodecCandidate !=
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a ||
            audioEntry.Selection.MediaKind != PlaybackIsoBmffHandlerKind.Audio)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .UnsupportedAudioEntry;
        }

        PlaybackIsoBmffBoxLocation? elementaryStreamBox = null;
        foreach (var child in audioEntry.Children)
        {
            if (child.Header.Type != ElementaryStreamBoxType)
            {
                continue;
            }

            if (elementaryStreamBox is not null)
            {
                return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .DuplicateElementaryStreamBox;
            }

            elementaryStreamBox = child;
        }

        if (elementaryStreamBox is null)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingElementaryStreamBox;
        }

        var recorded = elementaryStreamBox;
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                sampleDescriptionBox,
                recorded.Offset,
                out var actual) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            actual is null ||
            actual.Offset != recorded.Offset ||
            actual.BoxLength != recorded.BoxLength ||
            actual.Header.Type != ElementaryStreamBoxType ||
            actual.Header.HeaderSize != recorded.Header.HeaderSize)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .BoxLocationMismatch;
        }

        var payload = sampleDescriptionBox.Slice(
            actual.PayloadOffset,
            actual.PayloadLength);
        if (payload.Length < PlaybackIsoBmffFullBoxHeaderParser.HeaderSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null || fullBox.Version != 0 || fullBox.Flags != 0)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .InvalidFullBox;
        }

        var descriptorPayload =
            payload[PlaybackIsoBmffFullBoxHeaderParser.HeaderSize..];
        if (descriptorPayload.Length > options.MaximumConfigurationBytes)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .ConfigurationLimitExceeded;
        }

        var headerOptions = new PlaybackIsoBmffDescriptorHeaderParserOptions
        {
            MaximumPayloadBytes = options.MaximumConfigurationBytes,
        };
        if (PlaybackIsoBmffDescriptorHeaderParser.Parse(
                descriptorPayload,
                out var outerHeader,
                headerOptions) !=
                PlaybackIsoBmffDescriptorHeaderParseResult.Parsed ||
            outerHeader is null)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .InvalidDescriptor;
        }

        if (outerHeader.Tag != ElementaryStreamDescriptorTag)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .UnsupportedDescriptor;
        }

        var bytes = descriptorPayload.ToArray();
        var outer = bytes.AsSpan(
            outerHeader.HeaderLength,
            outerHeader.PayloadLength);
        if (outer.Length < 3)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .InvalidDescriptor;
        }

        var elementaryStreamId = BinaryPrimitives.ReadUInt16BigEndian(outer);
        var flags = outer[2];
        var streamPriority = (byte)(flags & 0x1F);
        var offset = 3;
        ushort? dependsOnElementaryStreamId = null;
        if ((flags & 0x80) != 0)
        {
            if (!TryReadUInt16(outer, ref offset,
                    out var dependsOnElementaryStreamIdValue))
            {
                return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .InvalidDescriptor;
            }

            dependsOnElementaryStreamId = dependsOnElementaryStreamIdValue;
        }

        byte[] url = [];
        if ((flags & 0x40) != 0)
        {
            if (offset >= outer.Length)
            {
                return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .InvalidDescriptor;
            }

            var urlLength = outer[offset++];
            if (outer.Length - offset < urlLength)
            {
                return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .InvalidDescriptor;
            }

            url = outer.Slice(offset, urlLength).ToArray();
            offset += urlLength;
        }

        ushort? ocrElementaryStreamId = null;
        if ((flags & 0x20) != 0)
        {
            if (!TryReadUInt16(outer, ref offset,
                    out var ocrElementaryStreamIdValue))
            {
                return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .InvalidDescriptor;
            }

            ocrElementaryStreamId = ocrElementaryStreamIdValue;
        }

        var children = new List<PlaybackIsoBmffDescriptorLocation>();
        PlaybackIsoBmffDescriptorLocation? decoderConfig = null;
        PlaybackIsoBmffDescriptorLocation? slConfig = null;
        while (offset != outer.Length)
        {
            if (children.Count >= options.MaximumDescriptorCount)
            {
                return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .DescriptorLimitExceeded;
            }

            var remaining = outer[offset..];
            if (PlaybackIsoBmffDescriptorHeaderParser.Parse(
                    remaining,
                    out var childHeader,
                    headerOptions,
                    requireExactLength: false) !=
                    PlaybackIsoBmffDescriptorHeaderParseResult.Parsed ||
                childHeader is null)
            {
                return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                    .InvalidChildSequence;
            }

            var location = new PlaybackIsoBmffDescriptorLocation(
                childHeader,
                outerHeader.HeaderLength + offset);
            if (childHeader.Tag == DecoderConfigDescriptorTag)
            {
                if (decoderConfig is not null)
                {
                    return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                        .DuplicateDecoderConfigDescriptor;
                }

                if (slConfig is not null)
                {
                    return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                        .InvalidChildSequence;
                }

                decoderConfig = location;
            }
            else if (childHeader.Tag == SlConfigDescriptorTag)
            {
                if (slConfig is not null)
                {
                    return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                        .DuplicateSlConfigDescriptor;
                }

                if (decoderConfig is null)
                {
                    return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                        .InvalidChildSequence;
                }

                slConfig = location;
            }

            children.Add(location);
            offset += childHeader.TotalLength;
        }

        if (decoderConfig is null)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingDecoderConfigDescriptor;
        }

        if (slConfig is null)
        {
            return PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingSlConfigDescriptor;
        }

        descriptor = new(
            audioEntry,
            actual,
            elementaryStreamId,
            streamPriority,
            dependsOnElementaryStreamId,
            url,
            ocrElementaryStreamId,
            children.AsReadOnly(),
            decoderConfig,
            slConfig,
            bytes);
        return PlaybackIsoBmffElementaryStreamDescriptorParseResult.Parsed;
    }

    private static bool TryReadUInt16(
        ReadOnlySpan<byte> payload,
        ref int offset,
        out ushort value)
    {
        value = 0;
        if (payload.Length - offset < 2)
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(offset, 2));
        offset += 2;
        return true;
    }
}
