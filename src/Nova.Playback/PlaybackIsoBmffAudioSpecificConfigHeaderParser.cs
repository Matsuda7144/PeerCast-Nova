namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
{
    Parsed,
    InvalidConfiguration,
    MissingDecoderConfig,
    InvalidHierarchy,
    ConfigurationLimitExceeded,
    UnsupportedObjectTypeIndication,
    NeedMoreData,
    InvalidAudioObjectType,
    InvalidSamplingFrequency,
}

public sealed record PlaybackIsoBmffAudioSpecificConfigHeaderParserOptions
{
    public int MaximumConfigurationBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumCodecExtradataBytes;
}

public sealed class PlaybackIsoBmffAudioSpecificConfigHeader
{
    internal PlaybackIsoBmffAudioSpecificConfigHeader(
        PlaybackIsoBmffDecoderConfigDescriptor decoderConfig,
        byte audioObjectType,
        byte samplingFrequencyIndex,
        uint samplingFrequency,
        byte channelConfiguration,
        int headerBitLength,
        ReadOnlyMemory<byte> configurationBytes)
    {
        DecoderConfig = decoderConfig;
        AudioObjectType = audioObjectType;
        SamplingFrequencyIndex = samplingFrequencyIndex;
        SamplingFrequency = samplingFrequency;
        ChannelConfiguration = channelConfiguration;
        HeaderBitLength = headerBitLength;
        ConfigurationBytes = configurationBytes;
    }

    public PlaybackIsoBmffDecoderConfigDescriptor DecoderConfig { get; }

    public byte AudioObjectType { get; }

    public byte SamplingFrequencyIndex { get; }

    public uint SamplingFrequency { get; }

    public byte ChannelConfiguration { get; }

    public int HeaderBitLength { get; }

    public ReadOnlyMemory<byte> ConfigurationBytes { get; }
}

public static class PlaybackIsoBmffAudioSpecificConfigHeaderParser
{
    private const byte Mpeg4AudioObjectTypeIndication = 0x40;
    private const byte ExplicitSamplingFrequencyIndex = 0x0F;

    private static readonly uint[] SamplingFrequencies =
    [
        96_000,
        88_200,
        64_000,
        48_000,
        44_100,
        32_000,
        24_000,
        22_050,
        16_000,
        12_000,
        11_025,
        8_000,
        7_350,
    ];

    public static PlaybackIsoBmffAudioSpecificConfigHeaderParseResult Parse(
        PlaybackIsoBmffDecoderConfigDescriptor? decoderConfig,
        out PlaybackIsoBmffAudioSpecificConfigHeader? header,
        PlaybackIsoBmffAudioSpecificConfigHeaderParserOptions? options = null)
    {
        header = null;
        options ??=
            new PlaybackIsoBmffAudioSpecificConfigHeaderParserOptions();
        if (options.MaximumConfigurationBytes is < 1 or
            > PlaybackBoundedDemuxOptions.MaximumCodecExtradataBytesHardLimit)
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .InvalidConfiguration;
        }

        if (decoderConfig is null)
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .MissingDecoderConfig;
        }

        if (decoderConfig.ObjectTypeIndication !=
            Mpeg4AudioObjectTypeIndication)
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .UnsupportedObjectTypeIndication;
        }

        if (!decoderConfig.TryGetPayload(
                decoderConfig.DecoderSpecificInfoDescriptor,
                out var configuration))
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .InvalidHierarchy;
        }

        if (configuration.Length > options.MaximumConfigurationBytes)
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .ConfigurationLimitExceeded;
        }

        var reader = new BitReader(configuration.Span);
        if (!reader.TryRead(5, out var audioObjectTypeValue))
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .NeedMoreData;
        }

        if (audioObjectTypeValue == 31)
        {
            if (!reader.TryRead(6, out var extendedAudioObjectType))
            {
                return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                    .NeedMoreData;
            }

            audioObjectTypeValue = 32 + extendedAudioObjectType;
        }

        if (audioObjectTypeValue is 0 or > byte.MaxValue)
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .InvalidAudioObjectType;
        }

        if (!reader.TryRead(4, out var samplingFrequencyIndexValue))
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .NeedMoreData;
        }

        uint samplingFrequency;
        if (samplingFrequencyIndexValue == ExplicitSamplingFrequencyIndex)
        {
            if (!reader.TryRead(24, out samplingFrequency))
            {
                return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                    .NeedMoreData;
            }

            if (samplingFrequency == 0)
            {
                return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                    .InvalidSamplingFrequency;
            }
        }
        else
        {
            if (samplingFrequencyIndexValue >= SamplingFrequencies.Length)
            {
                return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                    .InvalidSamplingFrequency;
            }

            samplingFrequency =
                SamplingFrequencies[checked((int)samplingFrequencyIndexValue)];
        }

        if (!reader.TryRead(4, out var channelConfiguration))
        {
            return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult
                .NeedMoreData;
        }

        header = new(
            decoderConfig,
            checked((byte)audioObjectTypeValue),
            checked((byte)samplingFrequencyIndexValue),
            samplingFrequency,
            checked((byte)channelConfiguration),
            reader.Position,
            configuration);
        return PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;

        internal BitReader(ReadOnlySpan<byte> payload)
        {
            _payload = payload;
        }

        internal int Position { get; private set; }

        internal bool TryRead(int bitCount, out uint value)
        {
            value = 0;
            if (bitCount is < 1 or > 32 ||
                bitCount > (_payload.Length * 8L) - Position)
            {
                return false;
            }

            for (var index = 0; index < bitCount; index++)
            {
                var absoluteBit = Position + index;
                var current = _payload[absoluteBit / 8];
                var shift = 7 - (absoluteBit % 8);
                value = (value << 1) | (uint)((current >> shift) & 1);
            }

            Position += bitCount;
            return true;
        }
    }
}
