namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult
{
    Parsed,
    MissingFirstSet,
    NoAdditionalSets,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionSecondSetHeader
{
    internal PlaybackIsoBmffErErrorProtectionSecondSetHeader(
        PlaybackIsoBmffErErrorProtectionFirstSet firstSet,
        byte classCount,
        int consumedBitLength)
    {
        FirstSet = firstSet;
        SetIndex = 1;
        ClassCount = classCount;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionFirstSet FirstSet { get; }

    public int SetIndex { get; }

    public byte ClassCount { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionSecondSetHeaderParser
{
    public static PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult Parse(
        PlaybackIsoBmffErErrorProtectionFirstSet? firstSet,
        out PlaybackIsoBmffErErrorProtectionSecondSetHeader? header)
    {
        header = null;
        if (firstSet is null)
        {
            return PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult
                .MissingFirstSet;
        }

        if (firstSet.Header.Prefix.PredefinedSetCount < 2)
        {
            return PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult
                .NoAdditionalSets;
        }

        var bytes = firstSet.Header.Prefix.Requirement.GaSpecificConfig
            .AudioSpecificConfigHeader.ConfigurationBytes.Span;
        var position = firstSet.ConsumedBitLength;
        if (position < 0 || 6 > (bytes.Length * 8L) - position)
        {
            return PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult
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
        header = new(firstSet, classCount, position);
        return PlaybackIsoBmffErErrorProtectionSecondSetHeaderParseResult.Parsed;
    }
}
