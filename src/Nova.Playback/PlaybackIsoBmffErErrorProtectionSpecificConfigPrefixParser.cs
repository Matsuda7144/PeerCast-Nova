namespace PeerCast.Nova.Playback;

public sealed class PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParserOptions
{
    public const int DefaultMaximumPredefinedSetCount = 64;
    public const int MaximumPredefinedSetCountLimit = byte.MaxValue;

    public int MaximumPredefinedSetCount { get; init; } =
        DefaultMaximumPredefinedSetCount;
}

public enum PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
{
    Parsed,
    MissingRequirement,
    NotRequired,
    InvalidOptions,
    PredefinedSetLimitExceeded,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix
{
    internal PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix(
        PlaybackIsoBmffErAudioProtectionRequirement requirement,
        byte predefinedSetCount,
        byte interleaveType,
        byte bitStuffing,
        byte concatenatedFrameCount,
        int consumedBitLength)
    {
        Requirement = requirement;
        PredefinedSetCount = predefinedSetCount;
        InterleaveType = interleaveType;
        BitStuffing = bitStuffing;
        ConcatenatedFrameCount = concatenatedFrameCount;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErAudioProtectionRequirement Requirement { get; }

    public byte PredefinedSetCount { get; }

    public byte InterleaveType { get; }

    public byte BitStuffing { get; }

    public byte ConcatenatedFrameCount { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParser
{
    public static
        PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult Parse(
            PlaybackIsoBmffErAudioProtectionRequirement? requirement,
            out PlaybackIsoBmffErErrorProtectionSpecificConfigPrefix? prefix,
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParserOptions?
                options = null)
    {
        prefix = null;
        if (requirement is null)
        {
            return PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .MissingRequirement;
        }

        if (!requirement.RequiresErrorProtectionSpecificConfig)
        {
            return PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .NotRequired;
        }

        options ??= new();
        if (options.MaximumPredefinedSetCount is < 0 or >
            PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParserOptions
                .MaximumPredefinedSetCountLimit)
        {
            return PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .InvalidOptions;
        }

        var reader = new BitReader(
            requirement.GaSpecificConfig.AudioSpecificConfigHeader
                .ConfigurationBytes.Span,
            requirement.ConsumedBitLength);
        if (!reader.TryRead(8, out var predefinedSetCount) ||
            !reader.TryRead(2, out var interleaveType) ||
            !reader.TryRead(3, out var bitStuffing) ||
            !reader.TryRead(3, out var concatenatedFrameCount))
        {
            return PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .NeedMoreData;
        }

        if (predefinedSetCount > options.MaximumPredefinedSetCount)
        {
            return PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
                .PredefinedSetLimitExceeded;
        }

        prefix = new(
            requirement,
            checked((byte)predefinedSetCount),
            checked((byte)interleaveType),
            checked((byte)bitStuffing),
            checked((byte)concatenatedFrameCount),
            reader.Position);
        return PlaybackIsoBmffErErrorProtectionSpecificConfigPrefixParseResult
            .Parsed;
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
