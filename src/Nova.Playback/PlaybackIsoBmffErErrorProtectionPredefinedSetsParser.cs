namespace PeerCast.Nova.Playback;

public sealed class PlaybackIsoBmffErErrorProtectionPredefinedSetsParserOptions
{
    public const int DefaultMaximumTotalClassCount = 4096;
    public const int MaximumTotalClassCountLimit = byte.MaxValue * 63;

    public int MaximumTotalClassCount { get; init; } =
        DefaultMaximumTotalClassCount;
}

public enum PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult
{
    Parsed,
    MissingPrefix,
    InvalidOptions,
    AggregateClassLimitExceeded,
    NeedMoreData,
    InvalidClassOutputOrder,
}

public sealed class PlaybackIsoBmffErErrorProtectionPredefinedClass
{
    private readonly PlaybackIsoBmffErErrorProtectionFirstClass _syntax;

    internal PlaybackIsoBmffErErrorProtectionPredefinedClass(
        int setIndex,
        int classIndex,
        PlaybackIsoBmffErErrorProtectionFirstClass syntax)
    {
        SetIndex = setIndex;
        ClassIndex = classIndex;
        _syntax = syntax;
    }

    public int SetIndex { get; }
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

public sealed class PlaybackIsoBmffErErrorProtectionPredefinedSet
{
    internal PlaybackIsoBmffErErrorProtectionPredefinedSet(
        int setIndex,
        IReadOnlyList<PlaybackIsoBmffErErrorProtectionPredefinedClass> classes,
        bool reorderedOutput,
        IReadOnlyList<byte> outputOrder,
        int consumedBitLength)
    {
        SetIndex = setIndex;
        Classes = classes;
        ReorderedOutput = reorderedOutput;
        OutputOrder = outputOrder;
        ConsumedBitLength = consumedBitLength;
    }

    public int SetIndex { get; }
    public IReadOnlyList<PlaybackIsoBmffErErrorProtectionPredefinedClass>
        Classes
    { get; }
    public bool ReorderedOutput { get; }
    public IReadOnlyList<byte> OutputOrder { get; }
    public int ConsumedBitLength { get; }
}

public sealed class PlaybackIsoBmffErErrorProtectionPredefinedSets
{
    internal PlaybackIsoBmffErErrorProtectionPredefinedSets(
        PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix prefix,
        IReadOnlyList<PlaybackIsoBmffErErrorProtectionPredefinedSet> sets,
        int totalClassCount,
        int consumedBitLength)
    {
        Prefix = prefix;
        Sets = sets;
        TotalClassCount = totalClassCount;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix Prefix { get; }
    public IReadOnlyList<PlaybackIsoBmffErErrorProtectionPredefinedSet> Sets
    { get; }
    public int TotalClassCount { get; }
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionPredefinedSetsParser
{
    public static PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult Parse(
        PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix? prefix,
        out PlaybackIsoBmffErErrorProtectionPredefinedSets? predefinedSets,
        PlaybackIsoBmffErErrorProtectionPredefinedSetsParserOptions? options = null)
    {
        predefinedSets = null;
        if (prefix is null)
            return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.MissingPrefix;

        options ??= new();
        if (options.MaximumTotalClassCount is < 0 or >
            PlaybackIsoBmffErErrorProtectionPredefinedSetsParserOptions
                .MaximumTotalClassCountLimit)
            return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.InvalidOptions;

        var bytes = prefix.Requirement.GaSpecificConfig.AudioSpecificConfigHeader
            .ConfigurationBytes.Span;
        var reader = new BitReader(bytes, prefix.ConsumedBitLength);
        var sets = new PlaybackIsoBmffErErrorProtectionPredefinedSet[
            prefix.PredefinedSetCount];
        var totalClassCount = 0;
        for (var setIndex = 0; setIndex < sets.Length; setIndex++)
        {
            if (!reader.TryRead(6, out var classCountValue))
                return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.NeedMoreData;

            var classCount = checked((byte)classCountValue);
            if (classCount > options.MaximumTotalClassCount - totalClassCount)
                return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult
                    .AggregateClassLimitExceeded;
            totalClassCount += classCount;

            var syntaxHeader = new PlaybackIsoBmffErErrorProtectionFirstSetHeader(
                prefix, classCount, reader.Position);
            var classes = new PlaybackIsoBmffErErrorProtectionPredefinedClass[
                classCount];
            for (var classIndex = 0; classIndex < classes.Length; classIndex++)
            {
                if (PlaybackIsoBmffErErrorProtectionFirstClassParser.ParseAt(
                        syntaxHeader, classIndex, reader.Position, out var syntax) !=
                    PlaybackIsoBmffErErrorProtectionFirstClassParseResult.Parsed ||
                    syntax is null)
                    return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.NeedMoreData;
                classes[classIndex] = new(setIndex, classIndex, syntax);
                reader.Position = syntax.ConsumedBitLength;
            }

            if (!reader.TryRead(1, out var reorderedValue))
                return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.NeedMoreData;
            var reordered = reorderedValue != 0;
            var outputOrder = reordered ? new byte[classCount] : [];
            if (reordered)
            {
                var seen = new bool[classCount];
                for (var index = 0; index < outputOrder.Length; index++)
                {
                    if (!reader.TryRead(6, out var orderValue))
                        return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.NeedMoreData;
                    if (orderValue >= classCount || seen[orderValue])
                        return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult
                            .InvalidClassOutputOrder;
                    outputOrder[index] = checked((byte)orderValue);
                    seen[orderValue] = true;
                }
            }

            sets[setIndex] = new(setIndex, Array.AsReadOnly(classes), reordered,
                Array.AsReadOnly(outputOrder), reader.Position);
        }

        predefinedSets = new(prefix, Array.AsReadOnly(sets), totalClassCount,
            reader.Position);
        return PlaybackIsoBmffErErrorProtectionPredefinedSetsParseResult.Parsed;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;
        internal BitReader(ReadOnlySpan<byte> payload, int position)
        { _payload = payload; Position = position; }
        internal int Position { get; set; }
        internal bool TryRead(int count, out uint value)
        {
            value = 0;
            if (count is < 1 or > 32 || Position < 0 ||
                count > (_payload.Length * 8L) - Position) return false;
            for (var index = 0; index < count; index++)
            {
                var bit = Position + index;
                value = (value << 1) |
                    (uint)((_payload[bit / 8] >> (7 - (bit % 8))) & 1);
            }
            Position += count;
            return true;
        }
    }
}
