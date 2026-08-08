namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
{
    Parsed,
    MissingGaSpecificConfig,
    MissingErAudioPostConfig,
    MissingErConfigurationAdmission,
    ExplicitExtensionAlreadyPresent,
    UnsupportedCoreAudioObjectType,
    NotPresent,
    NeedMoreData,
    UnsupportedExtensionAudioObjectType,
    InvalidExtensionSamplingFrequency,
}

public sealed class PlaybackIsoBmffImplicitSbrSyncExtension
{
    internal PlaybackIsoBmffImplicitSbrSyncExtension(
        PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
        bool sbrPresent,
        byte? extensionSamplingFrequencyIndex,
        uint? extensionSamplingFrequency,
        int consumedBitLength)
    {
        GaSpecificConfig = gaSpecificConfig;
        SbrPresent = sbrPresent;
        ExtensionSamplingFrequencyIndex = extensionSamplingFrequencyIndex;
        ExtensionSamplingFrequency = extensionSamplingFrequency;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffGaSpecificConfig GaSpecificConfig { get; }

    public bool SbrPresent { get; }

    public byte? ExtensionSamplingFrequencyIndex { get; }

    public uint? ExtensionSamplingFrequency { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffImplicitSbrSyncExtensionParser
{
    private const uint SyncExtensionType = 0x2B7;
    private const byte SbrAudioObjectType = 5;
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

    public static PlaybackIsoBmffImplicitSbrSyncExtensionParseResult Parse(
        PlaybackIsoBmffGaSpecificConfig? gaSpecificConfig,
        out PlaybackIsoBmffImplicitSbrSyncExtension? extension)
    {
        extension = null;
        if (gaSpecificConfig is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .MissingGaSpecificConfig;
        }

        return ParseCore(
            gaSpecificConfig,
            gaSpecificConfig.ConsumedBitLength,
            requireNonErCore: true,
            out extension);
    }

    public static PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
        ParseAfterErPostConfig(
            PlaybackIsoBmffErAudioPostConfig? erAudioPostConfig,
            out PlaybackIsoBmffImplicitSbrSyncExtension? extension)
    {
        extension = null;
        if (erAudioPostConfig is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .MissingErAudioPostConfig;
        }

        return ParseCore(
            erAudioPostConfig.GaSpecificConfig,
            erAudioPostConfig.ConsumedBitLength,
            requireNonErCore: false,
            out extension);
    }

    public static PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
        ParseAfterErConfigurationAdmission(
            PlaybackIsoBmffErConfigurationAdmission? admission,
            out PlaybackIsoBmffImplicitSbrSyncExtension? extension)
    {
        extension = null;
        if (admission is null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .MissingErConfigurationAdmission;
        }

        return ParseCore(
            admission.Requirement.GaSpecificConfig,
            admission.ConsumedBitLength,
            requireNonErCore: false,
            out extension);
    }

    private static PlaybackIsoBmffImplicitSbrSyncExtensionParseResult ParseCore(
        PlaybackIsoBmffGaSpecificConfig gaSpecificConfig,
        int startBitLength,
        bool requireNonErCore,
        out PlaybackIsoBmffImplicitSbrSyncExtension? extension)
    {
        extension = null;
        if (gaSpecificConfig.ExplicitSbrPsExtension is not null)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .ExplicitExtensionAlreadyPresent;
        }

        if (requireNonErCore &&
            gaSpecificConfig.AudioObjectType is not (1 or 2 or 3 or 4 or 6 or 7))
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedCoreAudioObjectType;
        }

        var reader = new BitReader(
            gaSpecificConfig.AudioSpecificConfigHeader.ConfigurationBytes.Span,
            startBitLength);
        if (reader.RemainingBitLength < 11)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NotPresent;
        }

        _ = reader.TryRead(11, out var syncExtensionType);
        if (syncExtensionType != SyncExtensionType)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.NotPresent;
        }

        if (!TryReadAudioObjectType(ref reader, out var extensionAudioObjectType))
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .NeedMoreData;
        }

        if (extensionAudioObjectType != SbrAudioObjectType)
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .UnsupportedExtensionAudioObjectType;
        }

        if (!reader.TryRead(1, out var sbrPresentValue))
        {
            return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                .NeedMoreData;
        }

        var sbrPresent = sbrPresentValue != 0;
        byte? frequencyIndex = null;
        uint? frequency = null;
        if (sbrPresent)
        {
            if (!reader.TryRead(4, out var frequencyIndexValue))
            {
                return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                    .NeedMoreData;
            }

            uint frequencyValue;
            if (frequencyIndexValue == ExplicitSamplingFrequencyIndex)
            {
                if (!reader.TryRead(24, out frequencyValue))
                {
                    return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                        .NeedMoreData;
                }

                if (frequencyValue == 0)
                {
                    return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                        .InvalidExtensionSamplingFrequency;
                }
            }
            else
            {
                if (frequencyIndexValue >= SamplingFrequencies.Length)
                {
                    return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult
                        .InvalidExtensionSamplingFrequency;
                }

                frequencyValue =
                    SamplingFrequencies[checked((int)frequencyIndexValue)];
            }

            frequencyIndex = checked((byte)frequencyIndexValue);
            frequency = frequencyValue;
        }

        extension = new(
            gaSpecificConfig,
            sbrPresent,
            frequencyIndex,
            frequency,
            reader.Position);
        return PlaybackIsoBmffImplicitSbrSyncExtensionParseResult.Parsed;
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

        internal long RemainingBitLength =>
            (_payload.Length * 8L) - Position;

        internal bool TryRead(int bitCount, out uint value)
        {
            value = 0;
            if (bitCount is < 1 or > 32 || Position < 0 ||
                bitCount > RemainingBitLength)
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
