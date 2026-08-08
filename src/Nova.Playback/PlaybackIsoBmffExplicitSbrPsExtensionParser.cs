namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffExplicitSbrPsExtensionParseResult
{
    Parsed,
    MissingAudioSpecificConfigHeader,
    UnsupportedAudioObjectType,
    NeedMoreData,
    InvalidExtensionSamplingFrequency,
    InvalidCoreAudioObjectType,
}

public sealed class PlaybackIsoBmffExplicitSbrPsExtension
{
    internal PlaybackIsoBmffExplicitSbrPsExtension(
        PlaybackIsoBmffAudioSpecificConfigHeader audioSpecificConfigHeader,
        bool sbrPresent,
        bool psPresent,
        byte extensionSamplingFrequencyIndex,
        uint extensionSamplingFrequency,
        byte coreAudioObjectType,
        byte? extensionChannelConfiguration,
        int consumedBitLength)
    {
        AudioSpecificConfigHeader = audioSpecificConfigHeader;
        SbrPresent = sbrPresent;
        PsPresent = psPresent;
        ExtensionSamplingFrequencyIndex = extensionSamplingFrequencyIndex;
        ExtensionSamplingFrequency = extensionSamplingFrequency;
        CoreAudioObjectType = coreAudioObjectType;
        ExtensionChannelConfiguration = extensionChannelConfiguration;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffAudioSpecificConfigHeader AudioSpecificConfigHeader
    {
        get;
    }

    public bool SbrPresent { get; }

    public bool PsPresent { get; }

    public byte ExtensionSamplingFrequencyIndex { get; }

    public uint ExtensionSamplingFrequency { get; }

    public byte CoreAudioObjectType { get; }

    public byte? ExtensionChannelConfiguration { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffExplicitSbrPsExtensionParser
{
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

    public static PlaybackIsoBmffExplicitSbrPsExtensionParseResult Parse(
        PlaybackIsoBmffAudioSpecificConfigHeader? audioSpecificConfigHeader,
        out PlaybackIsoBmffExplicitSbrPsExtension? extension)
    {
        extension = null;
        if (audioSpecificConfigHeader is null)
        {
            return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .MissingAudioSpecificConfigHeader;
        }

        if (audioSpecificConfigHeader.AudioObjectType is not (5 or 29))
        {
            return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .UnsupportedAudioObjectType;
        }

        var reader = new BitReader(
            audioSpecificConfigHeader.ConfigurationBytes.Span,
            audioSpecificConfigHeader.HeaderBitLength);
        if (!reader.TryRead(4, out var frequencyIndexValue))
        {
            return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .NeedMoreData;
        }

        uint extensionSamplingFrequency;
        if (frequencyIndexValue == ExplicitSamplingFrequencyIndex)
        {
            if (!reader.TryRead(24, out extensionSamplingFrequency))
            {
                return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                    .NeedMoreData;
            }

            if (extensionSamplingFrequency == 0)
            {
                return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                    .InvalidExtensionSamplingFrequency;
            }
        }
        else
        {
            if (frequencyIndexValue >= SamplingFrequencies.Length)
            {
                return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                    .InvalidExtensionSamplingFrequency;
            }

            extensionSamplingFrequency =
                SamplingFrequencies[checked((int)frequencyIndexValue)];
        }

        if (!TryReadAudioObjectType(ref reader, out var coreAudioObjectType))
        {
            return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .NeedMoreData;
        }

        if (coreAudioObjectType == 0)
        {
            return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .InvalidCoreAudioObjectType;
        }

        byte? extensionChannelConfiguration = null;
        if (coreAudioObjectType == 22)
        {
            if (!reader.TryRead(4, out var extensionChannelValue))
            {
                return PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                    .NeedMoreData;
            }

            extensionChannelConfiguration =
                checked((byte)extensionChannelValue);
        }

        extension = new(
            audioSpecificConfigHeader,
            true,
            audioSpecificConfigHeader.AudioObjectType == 29,
            checked((byte)frequencyIndexValue),
            extensionSamplingFrequency,
            checked((byte)coreAudioObjectType),
            extensionChannelConfiguration,
            reader.Position);
        return PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed;
    }

    private static bool TryReadAudioObjectType(
        ref BitReader reader,
        out uint audioObjectType)
    {
        if (!reader.TryRead(5, out audioObjectType))
        {
            return false;
        }

        if (audioObjectType != 31)
        {
            return true;
        }

        if (!reader.TryRead(6, out var extension))
        {
            return false;
        }

        audioObjectType = 32 + extension;
        return true;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;

        internal BitReader(ReadOnlySpan<byte> payload, int position)
        {
            _payload = payload;
            Position = position;
        }

        internal int Position { get; private set; }

        internal bool TryRead(int bitCount, out uint value)
        {
            value = 0;
            if (bitCount is < 1 or > 32 || Position < 0 ||
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
