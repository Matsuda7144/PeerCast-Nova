namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffGaSpecificConfigPrefixParseResult
{
    Parsed,
    MissingAudioSpecificConfigHeader,
    MissingExplicitSbrPsExtension,
    UnsupportedAudioObjectType,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffGaSpecificConfigPrefix
{
    internal PlaybackIsoBmffGaSpecificConfigPrefix(
        PlaybackIsoBmffAudioSpecificConfigHeader audioSpecificConfigHeader,
        PlaybackIsoBmffExplicitSbrPsExtension? explicitSbrPsExtension,
        byte audioObjectType,
        bool frameLengthFlag,
        bool dependsOnCoreCoder,
        ushort? coreCoderDelay,
        bool extensionFlag,
        int consumedBitLength)
    {
        AudioSpecificConfigHeader = audioSpecificConfigHeader;
        ExplicitSbrPsExtension = explicitSbrPsExtension;
        AudioObjectType = audioObjectType;
        FrameLengthFlag = frameLengthFlag;
        DependsOnCoreCoder = dependsOnCoreCoder;
        CoreCoderDelay = coreCoderDelay;
        ExtensionFlag = extensionFlag;
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

    public bool RequiresProgramConfigElement =>
        AudioSpecificConfigHeader.ChannelConfiguration == 0;

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffGaSpecificConfigPrefixParser
{
    public static PlaybackIsoBmffGaSpecificConfigPrefixParseResult Parse(
        PlaybackIsoBmffAudioSpecificConfigHeader? audioSpecificConfigHeader,
        out PlaybackIsoBmffGaSpecificConfigPrefix? prefix)
    {
        prefix = null;
        if (audioSpecificConfigHeader is null)
        {
            return PlaybackIsoBmffGaSpecificConfigPrefixParseResult
                .MissingAudioSpecificConfigHeader;
        }

        return ParseCore(
            audioSpecificConfigHeader,
            null,
            audioSpecificConfigHeader.AudioObjectType,
            audioSpecificConfigHeader.HeaderBitLength,
            out prefix);
    }

    public static PlaybackIsoBmffGaSpecificConfigPrefixParseResult
        ParseExplicitExtension(
            PlaybackIsoBmffExplicitSbrPsExtension? explicitSbrPsExtension,
            out PlaybackIsoBmffGaSpecificConfigPrefix? prefix)
    {
        prefix = null;
        if (explicitSbrPsExtension is null)
        {
            return PlaybackIsoBmffGaSpecificConfigPrefixParseResult
                .MissingExplicitSbrPsExtension;
        }

        return ParseCore(
            explicitSbrPsExtension.AudioSpecificConfigHeader,
            explicitSbrPsExtension,
            explicitSbrPsExtension.CoreAudioObjectType,
            explicitSbrPsExtension.ConsumedBitLength,
            out prefix);
    }

    private static PlaybackIsoBmffGaSpecificConfigPrefixParseResult ParseCore(
        PlaybackIsoBmffAudioSpecificConfigHeader header,
        PlaybackIsoBmffExplicitSbrPsExtension? explicitExtension,
        byte audioObjectType,
        int startBitLength,
        out PlaybackIsoBmffGaSpecificConfigPrefix? prefix)
    {
        prefix = null;
        if (!IsGeneralAudioObjectType(audioObjectType))
        {
            return PlaybackIsoBmffGaSpecificConfigPrefixParseResult
                .UnsupportedAudioObjectType;
        }

        var reader = new BitReader(
            header.ConfigurationBytes.Span,
            startBitLength);
        if (!reader.TryRead(1, out var frameLengthValue) ||
            !reader.TryRead(1, out var dependsValue))
        {
            return PlaybackIsoBmffGaSpecificConfigPrefixParseResult.NeedMoreData;
        }

        ushort? coreCoderDelay = null;
        if (dependsValue != 0)
        {
            if (!reader.TryRead(14, out var delayValue))
            {
                return PlaybackIsoBmffGaSpecificConfigPrefixParseResult
                    .NeedMoreData;
            }

            coreCoderDelay = checked((ushort)delayValue);
        }

        if (!reader.TryRead(1, out var extensionValue))
        {
            return PlaybackIsoBmffGaSpecificConfigPrefixParseResult.NeedMoreData;
        }

        prefix = new(
            header,
            explicitExtension,
            audioObjectType,
            frameLengthValue != 0,
            dependsValue != 0,
            coreCoderDelay,
            extensionValue != 0,
            reader.Position);
        return PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed;
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

        internal bool TryRead(int count, out uint value)
        {
            value = 0;
            if (count is < 1 or > 32 || Position < 0 ||
                count > (_payload.Length * 8L) - Position)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                var absoluteBit = Position + index;
                value = (value << 1) |
                    (uint)((_payload[absoluteBit / 8] >>
                        (7 - (absoluteBit % 8))) & 1);
            }

            Position += count;
            return true;
        }
    }
}
