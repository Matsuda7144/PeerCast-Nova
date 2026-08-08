namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffImplicitPsSyncExtensionParseResult
{
    Parsed,
    MissingSbrSyncExtension,
    SbrNotPresent,
    NotPresent,
    NeedMoreData,
}

public sealed class PlaybackIsoBmffImplicitPsSyncExtension
{
    internal PlaybackIsoBmffImplicitPsSyncExtension(
        PlaybackIsoBmffImplicitSbrSyncExtension sbrSyncExtension,
        bool psPresent,
        int consumedBitLength)
    {
        SbrSyncExtension = sbrSyncExtension;
        PsPresent = psPresent;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffImplicitSbrSyncExtension SbrSyncExtension { get; }

    public bool PsPresent { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffImplicitPsSyncExtensionParser
{
    private const uint SyncExtensionType = 0x548;

    public static PlaybackIsoBmffImplicitPsSyncExtensionParseResult Parse(
        PlaybackIsoBmffImplicitSbrSyncExtension? sbrSyncExtension,
        out PlaybackIsoBmffImplicitPsSyncExtension? extension)
    {
        extension = null;
        if (sbrSyncExtension is null)
        {
            return PlaybackIsoBmffImplicitPsSyncExtensionParseResult
                .MissingSbrSyncExtension;
        }

        if (!sbrSyncExtension.SbrPresent)
        {
            return PlaybackIsoBmffImplicitPsSyncExtensionParseResult
                .SbrNotPresent;
        }

        var reader = new BitReader(
            sbrSyncExtension.GaSpecificConfig.AudioSpecificConfigHeader
                .ConfigurationBytes.Span,
            sbrSyncExtension.ConsumedBitLength);
        if (reader.RemainingBitLength < 11)
        {
            return PlaybackIsoBmffImplicitPsSyncExtensionParseResult.NotPresent;
        }

        _ = reader.TryRead(11, out var syncExtensionType);
        if (syncExtensionType != SyncExtensionType)
        {
            return PlaybackIsoBmffImplicitPsSyncExtensionParseResult.NotPresent;
        }

        if (!reader.TryRead(1, out var psPresentValue))
        {
            return PlaybackIsoBmffImplicitPsSyncExtensionParseResult.NeedMoreData;
        }

        extension = new(
            sbrSyncExtension,
            psPresentValue != 0,
            reader.Position);
        return PlaybackIsoBmffImplicitPsSyncExtensionParseResult.Parsed;
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

        internal long RemainingBitLength =>
            (_payload.Length * 8L) - Position;

        internal bool TryRead(int bitCount, out uint value)
        {
            value = 0;
            if (bitCount is < 1 or > 32 || Position < 0 ||
                bitCount > RemainingBitLength)
            {
                return false;
            }

            for (var index = 0; index < bitCount; index++)
            {
                var absoluteBit = Position + index;
                var current = _payload[absoluteBit / 8];
                var shift = 7 - (absoluteBit % 8);
                value = (value << 1) | (uint)((current >> shift) & 1);
            }

            Position += bitCount;
            return true;
        }
    }
}
