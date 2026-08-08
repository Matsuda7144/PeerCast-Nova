namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionFirstClassParseResult
{
    Parsed,
    MissingFirstSetHeader,
    NoClasses,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionFirstClass
{
    internal PlaybackIsoBmffErErrorProtectionFirstClass(
        PlaybackIsoBmffErErrorProtectionFirstSetHeader firstSetHeader,
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
        FirstSetHeader = firstSetHeader;
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

    public PlaybackIsoBmffErErrorProtectionFirstSetHeader FirstSetHeader { get; }
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

public static class PlaybackIsoBmffErErrorProtectionFirstClassParser
{
    public static PlaybackIsoBmffErErrorProtectionFirstClassParseResult Parse(
        PlaybackIsoBmffErErrorProtectionFirstSetHeader? firstSetHeader,
        out PlaybackIsoBmffErErrorProtectionFirstClass? firstClass)
    {
        firstClass = null;
        if (firstSetHeader is null)
        {
            return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                .MissingFirstSetHeader;
        }

        if (firstSetHeader.ClassCount == 0)
        {
            return PlaybackIsoBmffErErrorProtectionFirstClassParseResult.NoClasses;
        }

        return ParseAt(
            firstSetHeader,
            classIndex: 0,
            firstSetHeader.ConsumedBitLength,
            out firstClass);
    }

    internal static PlaybackIsoBmffErErrorProtectionFirstClassParseResult ParseAt(
        PlaybackIsoBmffErErrorProtectionFirstSetHeader firstSetHeader,
        int classIndex,
        int startBitPosition,
        out PlaybackIsoBmffErErrorProtectionFirstClass? parsedClass)
    {
        parsedClass = null;

        var prefix = firstSetHeader.Prefix;
        var reader = new BitReader(
            prefix.Requirement.GaSpecificConfig.AudioSpecificConfigHeader
                .ConfigurationBytes.Span,
            startBitPosition);
        if (!reader.TryRead(1, out var lengthEscapeValue) ||
            !reader.TryRead(1, out var rateEscapeValue) ||
            !reader.TryRead(1, out var crcLengthEscapeValue))
        {
            return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                .NeedMoreData;
        }

        bool? concatenate = null;
        if (prefix.ConcatenatedFrameCount != 1)
        {
            if (!reader.TryRead(1, out var concatenateValue))
            {
                return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                    .NeedMoreData;
            }

            concatenate = concatenateValue != 0;
        }

        if (!reader.TryRead(2, out var fecTypeValue))
        {
            return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                .NeedMoreData;
        }

        bool? termination = null;
        if (fecTypeValue == 0)
        {
            if (!reader.TryRead(1, out var terminationValue))
            {
                return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                    .NeedMoreData;
            }

            termination = terminationValue != 0;
        }

        byte? interleave = null;
        if (prefix.InterleaveType == 2)
        {
            if (!reader.TryRead(2, out var interleaveValue))
            {
                return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                    .NeedMoreData;
            }

            interleave = checked((byte)interleaveValue);
        }

        if (!reader.TryRead(1, out var optionalValue))
        {
            return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                .NeedMoreData;
        }

        var lengthEscape = lengthEscapeValue != 0;
        byte? numberOfBitsForLength = null;
        ushort? classLength = null;
        if (lengthEscape)
        {
            if (!reader.TryRead(4, out var numberOfBitsValue))
            {
                return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                    .NeedMoreData;
            }

            numberOfBitsForLength = checked((byte)numberOfBitsValue);
        }
        else
        {
            if (!reader.TryRead(16, out var classLengthValue))
            {
                return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                    .NeedMoreData;
            }

            classLength = checked((ushort)classLengthValue);
        }

        var rateEscape = rateEscapeValue != 0;
        byte? classRate = null;
        if (!rateEscape)
        {
            var rateBitCount = fecTypeValue == 0 ? 5 : 7;
            if (!reader.TryRead(rateBitCount, out var classRateValue))
            {
                return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                    .NeedMoreData;
            }

            classRate = checked((byte)classRateValue);
        }

        var crcLengthEscape = crcLengthEscapeValue != 0;
        byte? classCrcLength = null;
        if (!crcLengthEscape)
        {
            if (!reader.TryRead(5, out var classCrcLengthValue))
            {
                return PlaybackIsoBmffErErrorProtectionFirstClassParseResult
                    .NeedMoreData;
            }

            classCrcLength = checked((byte)classCrcLengthValue);
        }

        parsedClass = new(
            firstSetHeader,
            classIndex,
            lengthEscape,
            rateEscape,
            crcLengthEscape,
            concatenate,
            checked((byte)fecTypeValue),
            termination,
            interleave,
            optionalValue != 0,
            numberOfBitsForLength,
            classLength,
            classRate,
            classCrcLength,
            reader.Position);
        return PlaybackIsoBmffErErrorProtectionFirstClassParseResult.Parsed;
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
