namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult
{
    Parsed,
    MissingPredefinedSets,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffErErrorProtectionHeaderProtection
{
    internal PlaybackIsoBmffErErrorProtectionHeaderProtection(
        PlaybackIsoBmffErErrorProtectionPredefinedSets predefinedSets,
        bool enabled,
        byte? headerRate,
        byte? headerCrcLength,
        int consumedBitLength)
    {
        PredefinedSets = predefinedSets;
        Enabled = enabled;
        HeaderRate = headerRate;
        HeaderCrcLength = headerCrcLength;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffErErrorProtectionPredefinedSets PredefinedSets
    { get; }
    public bool Enabled { get; }
    public byte? HeaderRate { get; }
    public byte? HeaderCrcLength { get; }
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffErErrorProtectionHeaderProtectionParser
{
    public static PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult
        Parse(
            PlaybackIsoBmffErErrorProtectionPredefinedSets? predefinedSets,
            out PlaybackIsoBmffErErrorProtectionHeaderProtection?
                headerProtection)
    {
        headerProtection = null;
        if (predefinedSets is null)
        {
            return PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult
                .MissingPredefinedSets;
        }

        var bytes = predefinedSets.Prefix.Requirement.GaSpecificConfig
            .AudioSpecificConfigHeader.ConfigurationBytes.Span;
        var reader = new BitReader(bytes, predefinedSets.ConsumedBitLength);
        if (!reader.TryRead(1, out var enabledValue))
        {
            return PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult
                .NeedMoreData;
        }

        var enabled = enabledValue != 0;
        byte? headerRate = null;
        byte? headerCrcLength = null;
        if (enabled)
        {
            if (!reader.TryRead(5, out var headerRateValue) ||
                !reader.TryRead(5, out var headerCrcLengthValue))
            {
                return PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult
                    .NeedMoreData;
            }

            headerRate = checked((byte)headerRateValue);
            headerCrcLength = checked((byte)headerCrcLengthValue);
        }

        headerProtection = new(predefinedSets, enabled, headerRate,
            headerCrcLength, reader.Position);
        return PlaybackIsoBmffErErrorProtectionHeaderProtectionParseResult.Parsed;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;
        internal BitReader(ReadOnlySpan<byte> payload, int position)
        { _payload = payload; Position = position; }
        internal int Position { get; private set; }
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
