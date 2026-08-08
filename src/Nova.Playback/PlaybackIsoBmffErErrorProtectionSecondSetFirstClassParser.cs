namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
{
    Parsed,
    MissingSecondSetHeader,
    NoClasses,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionSecondSetFirstClass
{
    internal PlaybackIsoBmffErErrorProtectionSecondSetFirstClass(
        PlaybackIsoBmffErErrorProtectionSecondSetHeader secondSetHeader,
        int classIndex,
        bool lengthEscape,
        bool rateEscape,
        bool crcLengthEscape,
        bool? concatenate,
        byte fecType,
        bool? termination,
        byte? interleave,
        bool optional,
        byte? numberOfBitsForLength,
        ushort? classLength,
        byte? classRate,
        byte? classCrcLength,
        int consumedBitLength)
    {
        SecondSetHeader = secondSetHeader;
        ClassIndex = classIndex;
        LengthEscape = lengthEscape;
        RateEscape = rateEscape;
        CrcLengthEscape = crcLengthEscape;
        Concatenate = concatenate;
        FecType = fecType;
        Termination = termination;
        Interleave = interleave;
        Optional = optional;
        NumberOfBitsForLength = numberOfBitsForLength;
        ClassLength = classLength;
        ClassRate = classRate;
        ClassCrcLength = classCrcLength;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionSecondSetHeader SecondSetHeader
    {
        get;
    }

    public int ClassIndex { get; }
    public bool LengthEscape { get; }
    public bool RateEscape { get; }
    public bool CrcLengthEscape { get; }
    public bool? Concatenate { get; }
    public byte FecType { get; }
    public bool? Termination { get; }
    public byte? Interleave { get; }
    public bool Optional { get; }
    public byte? NumberOfBitsForLength { get; }
    public ushort? ClassLength { get; }
    public byte? ClassRate { get; }
    public byte? ClassCrcLength { get; }
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParser
{
    public static
        PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult Parse(
            PlaybackIsoBmffErErrorProtectionSecondSetHeader? secondSetHeader,
            out PlaybackIsoBmffErErrorProtectionSecondSetFirstClass?
                firstClass)
    {
        firstClass = null;
        if (secondSetHeader is null)
        {
            return PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
                .MissingSecondSetHeader;
        }

        if (secondSetHeader.ClassCount == 0)
        {
            return PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
                .NoClasses;
        }

        return ParseAt(
            secondSetHeader,
            classIndex: 0,
            secondSetHeader.ConsumedBitLength,
            out firstClass);
    }

    internal static
        PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult ParseAt(
            PlaybackIsoBmffErErrorProtectionSecondSetHeader secondSetHeader,
            int classIndex,
            int startBitPosition,
            out PlaybackIsoBmffErErrorProtectionSecondSetFirstClass?
                parsedSecondSetClass)
    {
        parsedSecondSetClass = null;
        var firstSetHeader = secondSetHeader.FirstSet.Header;
        var result = PlaybackIsoBmffErErrorProtectionFirstClassParser.ParseAt(
            firstSetHeader,
            classIndex,
            startBitPosition,
            out var parsedClass);
        if (result != PlaybackIsoBmffErErrorProtectionFirstClassParseResult.Parsed ||
            parsedClass is null)
        {
            return PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
                .NeedMoreData;
        }

        parsedSecondSetClass = new(
            secondSetHeader,
            classIndex,
            parsedClass.LengthEscape,
            parsedClass.RateEscape,
            parsedClass.CrcLengthEscape,
            parsedClass.Concatenate,
            parsedClass.FecType,
            parsedClass.Termination,
            parsedClass.Interleave,
            parsedClass.Optional,
            parsedClass.NumberOfBitsForLength,
            parsedClass.ClassLength,
            parsedClass.ClassRate,
            parsedClass.ClassCrcLength,
            parsedClass.ConsumedBitLength);
        return PlaybackIsoBmffErErrorProtectionSecondSetFirstClassParseResult
            .Parsed;
    }
}
