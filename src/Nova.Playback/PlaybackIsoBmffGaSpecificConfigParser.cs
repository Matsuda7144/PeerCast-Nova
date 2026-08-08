namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffGaSpecificConfigParseResult
{
    Parsed,
    MissingAudioSpecificConfigHeader,
    MissingExplicitSbrPsExtension,
    UnsupportedAudioObjectType,
    UnsupportedProgramConfigElement,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffGaSpecificConfig
{
    internal PlaybackIsoBmffGaSpecificConfig(
        PlaybackIsoBmffAudioSpecificConfigHeader audioSpecificConfigHeader,
        PlaybackIsoBmffExplicitSbrPsExtension? explicitSbrPsExtension,
        byte audioObjectType,
        bool frameLengthFlag,
        bool dependsOnCoreCoder,
        ushort? coreCoderDelay,
        bool extensionFlag,
        byte? layerNumber,
        byte? numberOfSubFrames,
        ushort? layerLength,
        bool? sectionDataResilienceFlag,
        bool? scalefactorDataResilienceFlag,
        bool? spectralDataResilienceFlag,
        bool? extensionFlag3,
        int consumedBitLength)
    {
        AudioSpecificConfigHeader = audioSpecificConfigHeader;
        ExplicitSbrPsExtension = explicitSbrPsExtension;
        AudioObjectType = audioObjectType;
        FrameLengthFlag = frameLengthFlag;
        DependsOnCoreCoder = dependsOnCoreCoder;
        CoreCoderDelay = coreCoderDelay;
        ExtensionFlag = extensionFlag;
        LayerNumber = layerNumber;
        NumberOfSubFrames = numberOfSubFrames;
        LayerLength = layerLength;
        SectionDataResilienceFlag = sectionDataResilienceFlag;
        ScalefactorDataResilienceFlag = scalefactorDataResilienceFlag;
        SpectralDataResilienceFlag = spectralDataResilienceFlag;
        ExtensionFlag3 = extensionFlag3;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffAudioSpecificConfigHeader AudioSpecificConfigHeader
    {
        get;
    }

    public PlaybackIsoBmffExplicitSbrPsExtension? ExplicitSbrPsExtension
    {
        get;
    }

    public byte AudioObjectType { get; }

    public bool FrameLengthFlag { get; }

    public bool DependsOnCoreCoder { get; }

    public ushort? CoreCoderDelay { get; }

    public bool ExtensionFlag { get; }

    public byte? LayerNumber { get; }

    public byte? NumberOfSubFrames { get; }

    public ushort? LayerLength { get; }

    public bool? SectionDataResilienceFlag { get; }

    public bool? ScalefactorDataResilienceFlag { get; }

    public bool? SpectralDataResilienceFlag { get; }

    public bool? ExtensionFlag3 { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffGaSpecificConfigParser
{
    public static PlaybackIsoBmffGaSpecificConfigParseResult Parse(
        PlaybackIsoBmffAudioSpecificConfigHeader? audioSpecificConfigHeader,
        out PlaybackIsoBmffGaSpecificConfig? config)
    {
        config = null;
        if (audioSpecificConfigHeader is null)
        {
            return PlaybackIsoBmffGaSpecificConfigParseResult
                .MissingAudioSpecificConfigHeader;
        }

        return ParseCore(
            audioSpecificConfigHeader,
            null,
            audioSpecificConfigHeader.AudioObjectType,
            audioSpecificConfigHeader.HeaderBitLength,
            out config);
    }

    public static PlaybackIsoBmffGaSpecificConfigParseResult
        ParseExplicitExtension(
            PlaybackIsoBmffExplicitSbrPsExtension? explicitSbrPsExtension,
            out PlaybackIsoBmffGaSpecificConfig? config)
    {
        config = null;
        if (explicitSbrPsExtension is null)
        {
            return PlaybackIsoBmffGaSpecificConfigParseResult
                .MissingExplicitSbrPsExtension;
        }

        return ParseCore(
            explicitSbrPsExtension.AudioSpecificConfigHeader,
            explicitSbrPsExtension,
            explicitSbrPsExtension.CoreAudioObjectType,
            explicitSbrPsExtension.ConsumedBitLength,
            out config);
    }

    private static PlaybackIsoBmffGaSpecificConfigParseResult ParseCore(
        PlaybackIsoBmffAudioSpecificConfigHeader audioSpecificConfigHeader,
        PlaybackIsoBmffExplicitSbrPsExtension? explicitSbrPsExtension,
        byte audioObjectType,
        int startBitLength,
        out PlaybackIsoBmffGaSpecificConfig? config)
    {
        config = null;
        if (!IsGeneralAudioObjectType(audioObjectType))
        {
            return PlaybackIsoBmffGaSpecificConfigParseResult
                .UnsupportedAudioObjectType;
        }

        if (audioSpecificConfigHeader.ChannelConfiguration == 0)
        {
            return PlaybackIsoBmffGaSpecificConfigParseResult
                .UnsupportedProgramConfigElement;
        }

        var reader = new BitReader(
            audioSpecificConfigHeader.ConfigurationBytes.Span,
            startBitLength);
        if (!reader.TryReadBoolean(out var frameLengthFlag) ||
            !reader.TryReadBoolean(out var dependsOnCoreCoder))
        {
            return PlaybackIsoBmffGaSpecificConfigParseResult.NeedMoreData;
        }

        ushort? coreCoderDelay = null;
        if (dependsOnCoreCoder)
        {
            if (!reader.TryRead(14, out var coreCoderDelayValue))
            {
                return PlaybackIsoBmffGaSpecificConfigParseResult.NeedMoreData;
            }

            coreCoderDelay = checked((ushort)coreCoderDelayValue);
        }

        if (!reader.TryReadBoolean(out var extensionFlag))
        {
            return PlaybackIsoBmffGaSpecificConfigParseResult.NeedMoreData;
        }

        byte? layerNumber = null;
        if (audioObjectType is 6 or 20)
        {
            if (!reader.TryRead(3, out var layerNumberValue))
            {
                return PlaybackIsoBmffGaSpecificConfigParseResult.NeedMoreData;
            }

            layerNumber = checked((byte)layerNumberValue);
        }

        byte? numberOfSubFrames = null;
        ushort? layerLength = null;
        bool? sectionDataResilienceFlag = null;
        bool? scalefactorDataResilienceFlag = null;
        bool? spectralDataResilienceFlag = null;
        bool? extensionFlag3 = null;
        if (extensionFlag)
        {
            if (audioObjectType == 22)
            {
                if (!reader.TryRead(5, out var numberOfSubFramesValue) ||
                    !reader.TryRead(11, out var layerLengthValue))
                {
                    return PlaybackIsoBmffGaSpecificConfigParseResult
                        .NeedMoreData;
                }

                numberOfSubFrames = checked((byte)numberOfSubFramesValue);
                layerLength = checked((ushort)layerLengthValue);
            }

            if (audioObjectType is 17 or 19 or 20 or 23)
            {
                if (!reader.TryReadBoolean(
                        out var sectionDataResilienceFlagValue) ||
                    !reader.TryReadBoolean(
                        out var scalefactorDataResilienceFlagValue) ||
                    !reader.TryReadBoolean(
                        out var spectralDataResilienceFlagValue))
                {
                    return PlaybackIsoBmffGaSpecificConfigParseResult
                        .NeedMoreData;
                }

                sectionDataResilienceFlag =
                    sectionDataResilienceFlagValue;
                scalefactorDataResilienceFlag =
                    scalefactorDataResilienceFlagValue;
                spectralDataResilienceFlag =
                    spectralDataResilienceFlagValue;
            }

            if (!reader.TryReadBoolean(out var extensionFlag3Value))
            {
                return PlaybackIsoBmffGaSpecificConfigParseResult.NeedMoreData;
            }

            extensionFlag3 = extensionFlag3Value;
        }

        config = new(
            audioSpecificConfigHeader,
            explicitSbrPsExtension,
            audioObjectType,
            frameLengthFlag,
            dependsOnCoreCoder,
            coreCoderDelay,
            extensionFlag,
            layerNumber,
            numberOfSubFrames,
            layerLength,
            sectionDataResilienceFlag,
            scalefactorDataResilienceFlag,
            spectralDataResilienceFlag,
            extensionFlag3,
            reader.Position);
        return PlaybackIsoBmffGaSpecificConfigParseResult.Parsed;
    }

    private static bool IsGeneralAudioObjectType(byte audioObjectType) =>
        audioObjectType is 1 or 2 or 3 or 4 or 6 or 7 or 17 or 19 or 20 or 21
            or 22 or 23;

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;

        internal BitReader(ReadOnlySpan<byte> payload, int position)
        {
            _payload = payload;
            Position = position;
        }

        internal int Position { get; private set; }

        internal bool TryReadBoolean(out bool value)
        {
            var result = TryRead(1, out var raw);
            value = raw != 0;
            return result;
        }

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
