namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionThirdSetParseResult
{
    Parsed,
    MissingThirdSetHeader,
    NeedMoreData,
    InvalidClassOutputOrder,
}

public sealed class PlaybackIsoBmffErErrorProtectionThirdSet
{
    internal PlaybackIsoBmffErErrorProtectionThirdSet(
        PlaybackIsoBmffErErrorProtectionThirdSetHeader header,
        IReadOnlyList<PlaybackIsoBmffErErrorProtectionThirdSetFirstClass> classes,
        bool reorderedOutput,
        IReadOnlyList<byte> outputOrder,
        int consumedBitLength)
    {
        Header = header;
        Classes = classes;
        ReorderedOutput = reorderedOutput;
        OutputOrder = outputOrder;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionThirdSetHeader Header { get; }
    public IReadOnlyList<PlaybackIsoBmffErErrorProtectionThirdSetFirstClass>
        Classes
    { get; }
    public bool ReorderedOutput { get; }
    public IReadOnlyList<byte> OutputOrder { get; }
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionThirdSetParser
{
    public static PlaybackIsoBmffErErrorProtectionThirdSetParseResult Parse(
        PlaybackIsoBmffErErrorProtectionThirdSetHeader? header,
        out PlaybackIsoBmffErErrorProtectionThirdSet? set)
    {
        set = null;
        if (header is null)
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetParseResult
                .MissingThirdSetHeader;
        }

        var classes = new PlaybackIsoBmffErErrorProtectionThirdSetFirstClass[
            header.ClassCount];
        var position = header.ConsumedBitLength;
        for (var classIndex = 0; classIndex < classes.Length; classIndex++)
        {
            if (PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParser.ParseAt(
                    header, classIndex, position, out var parsedClass) !=
                PlaybackIsoBmffErErrorProtectionThirdSetFirstClassParseResult
                    .Parsed || parsedClass is null)
            {
                return PlaybackIsoBmffErErrorProtectionThirdSetParseResult
                    .NeedMoreData;
            }

            classes[classIndex] = parsedClass;
            position = parsedClass.ConsumedBitLength;
        }

        var bytes = header.SecondSet.Header.FirstSet.Header.Prefix.Requirement
            .GaSpecificConfig.AudioSpecificConfigHeader.ConfigurationBytes.Span;
        var reader = new BitReader(bytes, position);
        if (!reader.TryRead(1, out var reorderedOutputValue))
        {
            return PlaybackIsoBmffErErrorProtectionThirdSetParseResult.NeedMoreData;
        }

        var reorderedOutput = reorderedOutputValue != 0;
        var outputOrder = reorderedOutput ? new byte[classes.Length] : [];
        if (reorderedOutput)
        {
            var seen = new bool[classes.Length];
            for (var index = 0; index < outputOrder.Length; index++)
            {
                if (!reader.TryRead(6, out var orderValue))
                {
                    return PlaybackIsoBmffErErrorProtectionThirdSetParseResult
                        .NeedMoreData;
                }

                if (orderValue >= classes.Length || seen[orderValue])
                {
                    return PlaybackIsoBmffErErrorProtectionThirdSetParseResult
                        .InvalidClassOutputOrder;
                }

                outputOrder[index] = checked((byte)orderValue);
                seen[orderValue] = true;
            }
        }

        set = new(header, Array.AsReadOnly(classes), reorderedOutput,
            Array.AsReadOnly(outputOrder), reader.Position);
        return PlaybackIsoBmffErErrorProtectionThirdSetParseResult.Parsed;
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
                value = (value << 1) | (uint)((_payload[absoluteBit / 8] >>
                    (7 - (absoluteBit % 8))) & 1);
            }

            Position += count;
            return true;
        }
    }
}
