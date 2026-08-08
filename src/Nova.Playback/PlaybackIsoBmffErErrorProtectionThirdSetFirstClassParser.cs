namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult
{
    Parsed,
    MissingThirdSetHeader,
    NoClasses,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionThirdSetFirstClass
{
    private readonly PlaybackIsoBmffErErrorProtectionFirstClass _syntax;

    internal PlaybackIsoBmffErErrorProtectionThirdSetFirstClass(
        PlaybackIsoBmffErErrorProtectionThirdSetHeader thirdSetHeader,
        int classIndex,
        PlaybackIsoBmffErErrorProtectionFirstClass syntax)
    {
        ThirdSetHeader = thirdSetHeader;
        ClassIndex = classIndex;
        _syntax = syntax;
    }

    public PlaybackIsoBmffErErrorProtectionThirdSetHeader ThirdSetHeader { get; }
    public int ClassIndex { get; }
    public bool LengthEscape => _syntax.LengthEscape;
    public bool RateEscape => _syntax.RateEscape;
    public bool CrcLengthEscape => _syntax.CrcLengthEscape;
    public bool? Concatenate => _syntax.Concatenate;
    public byte FecType => _syntax.FecType;
    public bool? Termination => _syntax.Termination;
    public byte? Interleave => _syntax.Interleave;
    public bool Optional => _syntax.Optional;
    public byte? NumberOfBitsForLength => _syntax.NumberOfBitsForLength;
    public ushort? ClassLength => _syntax.ClassLength;
    public byte? ClassRate => _syntax.ClassRate;
    public byte? ClassCrcLength => _syntax.ClassCrcLength;
    public int ConsumedBitLength => _syntax.ConsumedBitLength;
}

public static class PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParser
{
    public static
        PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult Parse(
            PlaybackIsoBmffErErrorProtectionThirdSetHeader? thirdSetHeader,
            out PlaybackIsoBmffErErrorProtectionThirdSetFirstClass? firstClass)
    {
        firstClass = null;
        if (thirdSetHeader is null)
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult
                .MissingThirdSetHeader;
        }

        if (thirdSetHeader.ClassCount == 0)
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult
                .NoClasses;
        }

        return ParseAt(
            thirdSetHeader,
            classIndex: 0,
            thirdSetHeader.ConsumedBitLength,
            out firstClass);
    }

    internal static
        PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult ParseAt(
            PlaybackIsoBmffErErrorProtectionThirdSetHeader thirdSetHeader,
            int classIndex,
            int startBitPosition,
            out PlaybackIsoBmffErErrorProtectionThirdSetFirstClass? parsedClass)
    {
        parsedClass = null;
        var firstSetHeader = thirdSetHeader.SecondSet.Header.FirstSet.Header;
        var result = PlaybackIsoBmffErErrorProtectionFirstClassParser.ParseAt(
            firstSetHeader,
            classIndex,
            startBitPosition,
            out var syntax);
        if (result != PlaybackIsoBmffErErrorProtectionFirstClassParseResult.Parsed ||
            syntax is null)
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult
                .NeedMoreData;
        }

        parsedClass = new(thirdSetHeader, classIndex, syntax);
        return PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult
            .Parsed;
    }
}
