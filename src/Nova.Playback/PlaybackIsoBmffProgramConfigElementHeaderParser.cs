namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigElementHeaderParseResult
{
    Parsed,
    MissingGaSpecificConfigPrefix,
    ProgramConfigElementNotRequired,
    NeedMoreData,
    InvalidSamplingFrequencyIndex,
}

public sealed class PlaybackIsoBmffProgramConfigElementHeader
{
    internal PlaybackIsoBmffProgramConfigElementHeader(
        PlaybackIsoBmffGaSpecificConfigPrefix gaSpecificConfigPrefix,
        byte elementInstanceTag,
        byte objectType,
        byte samplingFrequencyIndex,
        byte frontChannelElementCount,
        byte sideChannelElementCount,
        byte backChannelElementCount,
        byte lfeChannelElementCount,
        byte associatedDataElementCount,
        byte validCcElementCount,
        int consumedBitLength)
    {
        GaSpecificConfigPrefix = gaSpecificConfigPrefix;
        ElementInstanceTag = elementInstanceTag;
        ObjectType = objectType;
        SamplingFrequencyIndex = samplingFrequencyIndex;
        FrontChannelElementCount = frontChannelElementCount;
        SideChannelElementCount = sideChannelElementCount;
        BackChannelElementCount = backChannelElementCount;
        LfeChannelElementCount = lfeChannelElementCount;
        AssociatedDataElementCount = associatedDataElementCount;
        ValidCcElementCount = validCcElementCount;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffGaSpecificConfigPrefix GaSpecificConfigPrefix
    {
        get;
    }

    public byte ElementInstanceTag { get; }

    public byte ObjectType { get; }

    public byte SamplingFrequencyIndex { get; }

    public byte FrontChannelElementCount { get; }

    public byte SideChannelElementCount { get; }

    public byte BackChannelElementCount { get; }

    public byte LfeChannelElementCount { get; }

    public byte AssociatedDataElementCount { get; }

    public byte ValidCcElementCount { get; }

    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffProgramConfigElementHeaderParser
{
    public static PlaybackIsoBmffProgramConfigElementHeaderParseResult Parse(
        PlaybackIsoBmffGaSpecificConfigPrefix? gaSpecificConfigPrefix,
        out PlaybackIsoBmffProgramConfigElementHeader? header)
    {
        header = null;
        if (gaSpecificConfigPrefix is null)
        {
            return PlaybackIsoBmffProgramConfigElementHeaderParseResult
                .MissingGaSpecificConfigPrefix;
        }

        if (!gaSpecificConfigPrefix.RequiresProgramConfigElement)
        {
            return PlaybackIsoBmffProgramConfigElementHeaderParseResult
                .ProgramConfigElementNotRequired;
        }

        var reader = new BitReader(
            gaSpecificConfigPrefix.AudioSpecificConfigHeader.ConfigurationBytes
                .Span,
            gaSpecificConfigPrefix.ConsumedBitLength);
        if (!reader.TryRead(4, out var elementTag) ||
            !reader.TryRead(2, out var objectType) ||
            !reader.TryRead(4, out var samplingFrequencyIndex) ||
            !reader.TryRead(4, out var frontCount) ||
            !reader.TryRead(4, out var sideCount) ||
            !reader.TryRead(4, out var backCount) ||
            !reader.TryRead(2, out var lfeCount) ||
            !reader.TryRead(3, out var associatedCount) ||
            !reader.TryRead(4, out var validCcCount))
        {
            return PlaybackIsoBmffProgramConfigElementHeaderParseResult
                .NeedMoreData;
        }

        if (samplingFrequencyIndex >= 13)
        {
            return PlaybackIsoBmffProgramConfigElementHeaderParseResult
                .InvalidSamplingFrequencyIndex;
        }

        header = new(
            gaSpecificConfigPrefix,
            checked((byte)elementTag),
            checked((byte)objectType),
            checked((byte)samplingFrequencyIndex),
            checked((byte)frontCount),
            checked((byte)sideCount),
            checked((byte)backCount),
            checked((byte)lfeCount),
            checked((byte)associatedCount),
            checked((byte)validCcCount),
            reader.Position);
        return PlaybackIsoBmffProgramConfigElementHeaderParseResult.Parsed;
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
