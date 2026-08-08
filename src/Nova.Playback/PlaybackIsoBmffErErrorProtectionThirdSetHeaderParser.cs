namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult
{
    Parsed,
    MissingSecondSet,
    NoAdditionalSets,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionThirdSetHeader
{
    internal PlaybackIsoBmffErErrorProtectionThirdSetHeader(
        PlaybackIsoBmffErErrorProtectionSecondSet secondSet,
        byte classCount,
        int consumedBitLength)
    {
        SecondSet = secondSet;
        SetIndex = 2;
        ClassCount = classCount;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionSecondSet SecondSet { get; }

    public int SetIndex { get; }

    public byte ClassCount { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionThirdSetHeaderParser
{
    public static PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult Parse(
        PlaybackIsoBmffErErrorProtectionSecondSet? secondSet,
        out PlaybackIsoBmffErErrorProtectionThirdSetHeader? header)
    {
        header = null;
        if (secondSet is null)
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult
                .MissingSecondSet;
        }

        if (secondSet.Header.FirstSet.Header.Prefix.PredefinedSetCount < 3)
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult
                .NoAdditionalSets;
        }

        var bytes = secondSet.Header.FirstSet.Header.Prefix.Requirement
            .GaSpecificConfig.AudioSpecificConfigHeader.ConfigurationBytes.Span;
        var position = secondSet.ConsumedBitLength;
        if (position < 0 || 6 > (bytes.Length * 8L) - position)
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult
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
        header = new(secondSet, classCount, position);
        return PlaybackIsoBmffErErrorProtectionThirdSetHeaderParseResult.Parsed;
    }
}
