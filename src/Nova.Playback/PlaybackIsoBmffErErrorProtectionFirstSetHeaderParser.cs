namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult
{
    Parsed,
    MissingPrefix,
    NoPredefinedSets,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionFirstSetHeader
{
    internal PlaybackIsoBmffErErrorProtectionFirstSetHeader(
        PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix prefix,
        byte classCount,
        int consumedBitLength)
    {
        Prefix = prefix;
        SetIndex = 0;
        ClassCount = classCount;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix Prefix { get; }

    public int SetIndex { get; }

    public byte ClassCount { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionFirstSetHeaderParser
{
    public static PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult Parse(
        PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix? prefix,
        out PlaybackIsoBmffErErrorProtectionFirstSetHeader? header)
    {
        header = null;
        if (prefix is null)
        {
            return PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult
                .MissingPrefix;
        }

        if (prefix.PredefinedSetCount == 0)
        {
            return PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult
                .NoPredefinedSets;
        }

        var bytes = prefix.Requirement.GaSpecificConfig.AudioSpecificConfigHeader
            .ConfigurationBytes.Span;
        var position = prefix.ConsumedBitLength;
        if (position < 0 || 6 > (bytes.Length * 8L) - position)
        {
            return PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult
                .NeedMoreData;
        }

        byte classCount = 0;
        for (var index = 0; index < 6; index++)
        {
            var absoluteBit = position + index;
            classCount = (byte)((classCount << 1) |
                ((bytes[absoluteBit / 8] >> (7 - (absoluteBit % 8))) & 1));
        }

        position += 6;
        header = new(prefix, classCount, position);
        return PlaybackIsoBmffErErrorProtectionFirstSetHeaderParseResult.Parsed;
    }
}
