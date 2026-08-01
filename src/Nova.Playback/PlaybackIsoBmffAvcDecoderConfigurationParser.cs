using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcDecoderConfigurationParseResult
{
    Parsed,
    InvalidConfiguration,
    MissingVisualEntry,
    MissingConfigurationBox,
    DuplicateConfigurationBox,
    ConfigurationLocationMismatch,
    ConfigurationLimitExceeded,
    ParameterSetLimitExceeded,
    InvalidPayload,
}

public enum PlaybackIsoBmffAvcParameterSetKind
{
    Sequence,
    Picture,
    SequenceExtension,
}

public sealed record PlaybackIsoBmffAvcDecoderConfigurationParserOptions
{
    public const int DefaultMaximumParameterSetCount = 256;
    public const int MaximumParameterSetCountHardLimit = 4_096;

    public int MaximumParameterSetCount { get; init; } =
        DefaultMaximumParameterSetCount;

    public int MaximumConfigurationBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumCodecExtradataBytes;
}

public sealed class PlaybackIsoBmffAvcParameterSet
{
    private readonly byte[] _payload;

    internal PlaybackIsoBmffAvcParameterSet(
        PlaybackIsoBmffAvcParameterSetKind kind,
        byte[] payload)
    {
        Kind = kind;
        _payload = payload;
    }

    public PlaybackIsoBmffAvcParameterSetKind Kind { get; }

    public ReadOnlyMemory<byte> Payload => _payload;
}

public sealed class PlaybackIsoBmffAvcDecoderConfiguration
{
    private readonly byte[] _configurationRecord;

    internal PlaybackIsoBmffAvcDecoderConfiguration(
        PlaybackIsoBmffVisualSampleEntry visualEntry,
        PlaybackIsoBmffBoxLocation configurationBox,
        byte profileIndication,
        byte profileCompatibility,
        byte levelIndication,
        byte nalUnitLengthBytes,
        byte? chromaFormat,
        byte? bitDepthLumaMinus8,
        byte? bitDepthChromaMinus8,
        ReadOnlyCollection<PlaybackIsoBmffAvcParameterSet> parameterSets,
        byte[] configurationRecord)
    {
        VisualEntry = visualEntry;
        ConfigurationBox = configurationBox;
        ProfileIndication = profileIndication;
        ProfileCompatibility = profileCompatibility;
        LevelIndication = levelIndication;
        NalUnitLengthBytes = nalUnitLengthBytes;
        ChromaFormat = chromaFormat;
        BitDepthLumaMinus8 = bitDepthLumaMinus8;
        BitDepthChromaMinus8 = bitDepthChromaMinus8;
        ParameterSets = parameterSets;
        _configurationRecord = configurationRecord;
    }

    public PlaybackIsoBmffVisualSampleEntry VisualEntry { get; }

    public PlaybackIsoBmffBoxLocation ConfigurationBox { get; }

    public byte ProfileIndication { get; }

    public byte ProfileCompatibility { get; }

    public byte LevelIndication { get; }

    public byte NalUnitLengthBytes { get; }

    public byte? ChromaFormat { get; }

    public byte? BitDepthLumaMinus8 { get; }

    public byte? BitDepthChromaMinus8 { get; }

    public IReadOnlyList<PlaybackIsoBmffAvcParameterSet> ParameterSets { get; }

    public ReadOnlyMemory<byte> ConfigurationRecord => _configurationRecord;
}

public static class PlaybackIsoBmffAvcDecoderConfigurationParser
{
    private const uint AvcConfigurationType = 0x61766343;

    public static PlaybackIsoBmffAvcDecoderConfigurationParseResult Parse(
        ReadOnlySpan<byte> sampleDescriptionBox,
        PlaybackIsoBmffVisualSampleEntry? visualEntry,
        out PlaybackIsoBmffAvcDecoderConfiguration? configuration,
        PlaybackIsoBmffAvcDecoderConfigurationParserOptions? options = null)
    {
        configuration = null;
        options ??= new PlaybackIsoBmffAvcDecoderConfigurationParserOptions();
        if (options.MaximumParameterSetCount is < 1 or
                > PlaybackIsoBmffAvcDecoderConfigurationParserOptions
                    .MaximumParameterSetCountHardLimit ||
            options.MaximumConfigurationBytes is < 1 or
                > PlaybackBoundedDemuxOptions
                    .MaximumCodecExtradataBytesHardLimit)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .InvalidConfiguration;
        }

        if (visualEntry is null)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .MissingVisualEntry;
        }

        PlaybackIsoBmffBoxLocation? configurationBox = null;
        foreach (var child in visualEntry.Children)
        {
            if (child.Header.Type != AvcConfigurationType)
            {
                continue;
            }

            if (configurationBox is not null)
            {
                return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                    .DuplicateConfigurationBox;
            }

            configurationBox = child;
        }

        if (configurationBox is null)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .MissingConfigurationBox;
        }

        var recorded = configurationBox;
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                sampleDescriptionBox,
                recorded.Offset,
                out var actual) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            actual is null ||
            actual.Offset != recorded.Offset ||
            actual.BoxLength != recorded.BoxLength ||
            actual.Header.Type != AvcConfigurationType ||
            actual.Header.HeaderSize != recorded.Header.HeaderSize)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLocationMismatch;
        }

        var payload = sampleDescriptionBox.Slice(
            actual.PayloadOffset,
            actual.PayloadLength);
        if (payload.Length > options.MaximumConfigurationBytes)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLimitExceeded;
        }

        if (payload.Length < 7 ||
            payload[0] != 1 ||
            (payload[4] & 0xFC) != 0xFC ||
            (payload[5] & 0xE0) != 0xE0)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .InvalidPayload;
        }

        var parameterSets = new List<PlaybackIsoBmffAvcParameterSet>();
        var offset = 6;
        var sequenceCount = payload[5] & 0x1F;
        var parseResult = ParseParameterSets(
            payload,
            ref offset,
            sequenceCount,
            PlaybackIsoBmffAvcParameterSetKind.Sequence,
            parameterSets,
            options.MaximumParameterSetCount);
        if (parseResult is not null)
        {
            return parseResult.Value;
        }

        if (offset >= payload.Length)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .InvalidPayload;
        }

        var pictureCount = payload[offset++];
        parseResult = ParseParameterSets(
            payload,
            ref offset,
            pictureCount,
            PlaybackIsoBmffAvcParameterSetKind.Picture,
            parameterSets,
            options.MaximumParameterSetCount);
        if (parseResult is not null)
        {
            return parseResult.Value;
        }

        byte? chromaFormat = null;
        byte? bitDepthLumaMinus8 = null;
        byte? bitDepthChromaMinus8 = null;
        if (offset != payload.Length)
        {
            if (!SupportsHighProfileExtension(payload[1]) ||
                payload.Length - offset < 4 ||
                (payload[offset] & 0xFC) != 0xFC ||
                (payload[offset + 1] & 0xF8) != 0xF8 ||
                (payload[offset + 2] & 0xF8) != 0xF8)
            {
                return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                    .InvalidPayload;
            }

            chromaFormat = (byte)(payload[offset++] & 0x03);
            bitDepthLumaMinus8 = (byte)(payload[offset++] & 0x07);
            bitDepthChromaMinus8 = (byte)(payload[offset++] & 0x07);
            var extensionCount = payload[offset++];
            parseResult = ParseParameterSets(
                payload,
                ref offset,
                extensionCount,
                PlaybackIsoBmffAvcParameterSetKind.SequenceExtension,
                parameterSets,
                options.MaximumParameterSetCount);
            if (parseResult is not null)
            {
                return parseResult.Value;
            }
        }

        if (offset != payload.Length || sequenceCount == 0 || pictureCount == 0)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .InvalidPayload;
        }

        configuration = new(
            visualEntry,
            actual,
            payload[1],
            payload[2],
            payload[3],
            (byte)((payload[4] & 0x03) + 1),
            chromaFormat,
            bitDepthLumaMinus8,
            bitDepthChromaMinus8,
            parameterSets.AsReadOnly(),
            payload.ToArray());
        return PlaybackIsoBmffAvcDecoderConfigurationParseResult.Parsed;
    }

    private static PlaybackIsoBmffAvcDecoderConfigurationParseResult?
        ParseParameterSets(
            ReadOnlySpan<byte> payload,
            ref int offset,
            int count,
            PlaybackIsoBmffAvcParameterSetKind kind,
            List<PlaybackIsoBmffAvcParameterSet> parameterSets,
            int maximumParameterSetCount)
    {
        if (count > maximumParameterSetCount - parameterSets.Count)
        {
            return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ParameterSetLimitExceeded;
        }

        for (var index = 0; index < count; index++)
        {
            if (payload.Length - offset < 2)
            {
                return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                    .InvalidPayload;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(
                payload.Slice(offset, 2));
            offset += 2;
            if (length == 0 || payload.Length - offset < length)
            {
                return PlaybackIsoBmffAvcDecoderConfigurationParseResult
                    .InvalidPayload;
            }

            parameterSets.Add(new(kind,
                payload.Slice(offset, length).ToArray()));
            offset += length;
        }

        return null;
    }

    private static bool SupportsHighProfileExtension(byte profile) =>
        profile is 100 or 110 or 122 or 144;
}
