namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigElementParseResult
{
    Parsed,
    MissingHeader,
    NeedMoreData,
    NonZeroAlignmentBits,
}

public sealed record PlaybackIsoBmffProgramConfigChannelElement(
    bool IsChannelPair,
    byte ElementTag);

public sealed record PlaybackIsoBmffProgramConfigCouplingElement(
    bool IsIndependentSwitch,
    byte ElementTag);

public sealed record PlaybackIsoBmffProgramConfigMatrixMixdown(
    byte Index,
    bool PseudoSurroundEnabled);

public sealed class PlaybackIsoBmffProgramConfigElement
{
    internal PlaybackIsoBmffProgramConfigElement(
        PlaybackIsoBmffProgramConfigElementHeader header,
        byte? monoMixdownElementTag,
        byte? stereoMixdownElementTag,
        PlaybackIsoBmffProgramConfigMatrixMixdown? matrixMixdown,
        IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> front,
        IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> side,
        IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> back,
        IReadOnlyList<byte> lfe,
        IReadOnlyList<byte> associatedData,
        IReadOnlyList<PlaybackIsoBmffProgramConfigCouplingElement> validCc,
        byte[] commentBytes,
        int consumedBitLength)
    {
        Header = header;
        MonoMixdownElementTag = monoMixdownElementTag;
        StereoMixdownElementTag = stereoMixdownElementTag;
        MatrixMixdown = matrixMixdown;
        FrontChannelElements = front;
        SideChannelElements = side;
        BackChannelElements = back;
        LfeChannelElementTags = lfe;
        AssociatedDataElementTags = associatedData;
        ValidCcElements = validCc;
        CommentBytes = commentBytes;
        ConsumedBitLength = consumedBitLength;
    }

    public PlaybackIsoBmffProgramConfigElementHeader Header { get; }
    public byte? MonoMixdownElementTag { get; }
    public byte? StereoMixdownElementTag { get; }
    public PlaybackIsoBmffProgramConfigMatrixMixdown? MatrixMixdown { get; }
    public IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> FrontChannelElements { get; }
    public IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> SideChannelElements { get; }
    public IReadOnlyList<PlaybackIsoBmffProgramConfigChannelElement> BackChannelElements { get; }
    public IReadOnlyList<byte> LfeChannelElementTags { get; }
    public IReadOnlyList<byte> AssociatedDataElementTags { get; }
    public IReadOnlyList<PlaybackIsoBmffProgramConfigCouplingElement> ValidCcElements { get; }
    public ReadOnlyMemory<byte> CommentBytes { get; }
    public int ConsumedBitLength { get; }
}

public static class PlaybackIsoBmffProgramConfigElementParser
{
    public static PlaybackIsoBmffProgramConfigElementParseResult Parse(
        PlaybackIsoBmffProgramConfigElementHeader? header,
        out PlaybackIsoBmffProgramConfigElement? element)
    {
        element = null;
        if (header is null)
        {
            return PlaybackIsoBmffProgramConfigElementParseResult.MissingHeader;
        }

        var reader = new BitReader(
            header.GaSpecificConfigPrefix.AudioSpecificConfigHeader
                .ConfigurationBytes.Span,
            header.ConsumedBitLength);
        if (!TryReadOptionalTag(ref reader, out var mono) ||
            !TryReadOptionalTag(ref reader, out var stereo) ||
            !TryReadMatrix(ref reader, out var matrix) ||
            !TryReadChannelElements(ref reader, header.FrontChannelElementCount, out var front) ||
            !TryReadChannelElements(ref reader, header.SideChannelElementCount, out var side) ||
            !TryReadChannelElements(ref reader, header.BackChannelElementCount, out var back) ||
            !TryReadTags(ref reader, header.LfeChannelElementCount, out var lfe) ||
            !TryReadTags(ref reader, header.AssociatedDataElementCount, out var associated) ||
            !TryReadCouplingElements(ref reader, header.ValidCcElementCount, out var validCc))
        {
            return PlaybackIsoBmffProgramConfigElementParseResult.NeedMoreData;
        }

        while ((reader.Position & 7) != 0)
        {
            if (!reader.TryRead(1, out var alignment))
            {
                return PlaybackIsoBmffProgramConfigElementParseResult.NeedMoreData;
            }

            if (alignment != 0)
            {
                return PlaybackIsoBmffProgramConfigElementParseResult.NonZeroAlignmentBits;
            }
        }

        if (!reader.TryRead(8, out var commentLength) ||
            !reader.TryReadBytes(checked((int)commentLength), out var comment))
        {
            return PlaybackIsoBmffProgramConfigElementParseResult.NeedMoreData;
        }

        element = new(
            header, mono, stereo, matrix,
            Array.AsReadOnly(front), Array.AsReadOnly(side),
            Array.AsReadOnly(back), Array.AsReadOnly(lfe),
            Array.AsReadOnly(associated), Array.AsReadOnly(validCc),
            comment, reader.Position);
        return PlaybackIsoBmffProgramConfigElementParseResult.Parsed;
    }

    private static bool TryReadOptionalTag(ref BitReader reader, out byte? tag)
    {
        tag = null;
        if (!reader.TryRead(1, out var present)) return false;
        if (present == 0) return true;
        if (!reader.TryRead(4, out var value)) return false;
        tag = checked((byte)value);
        return true;
    }

    private static bool TryReadMatrix(
        ref BitReader reader,
        out PlaybackIsoBmffProgramConfigMatrixMixdown? matrix)
    {
        matrix = null;
        if (!reader.TryRead(1, out var present)) return false;
        if (present == 0) return true;
        if (!reader.TryRead(2, out var index) || !reader.TryRead(1, out var pseudo)) return false;
        matrix = new(checked((byte)index), pseudo != 0);
        return true;
    }

    private static bool TryReadChannelElements(
        ref BitReader reader, int count,
        out PlaybackIsoBmffProgramConfigChannelElement[] elements)
    {
        elements = new PlaybackIsoBmffProgramConfigChannelElement[count];
        for (var index = 0; index < count; index++)
        {
            if (!reader.TryRead(1, out var pair) || !reader.TryRead(4, out var tag)) return false;
            elements[index] = new(pair != 0, checked((byte)tag));
        }
        return true;
    }

    private static bool TryReadTags(ref BitReader reader, int count, out byte[] tags)
    {
        tags = new byte[count];
        for (var index = 0; index < count; index++)
        {
            if (!reader.TryRead(4, out var tag)) return false;
            tags[index] = checked((byte)tag);
        }
        return true;
    }

    private static bool TryReadCouplingElements(
        ref BitReader reader, int count,
        out PlaybackIsoBmffProgramConfigCouplingElement[] elements)
    {
        elements = new PlaybackIsoBmffProgramConfigCouplingElement[count];
        for (var index = 0; index < count; index++)
        {
            if (!reader.TryRead(1, out var independent) || !reader.TryRead(4, out var tag)) return false;
            elements[index] = new(independent != 0, checked((byte)tag));
        }
        return true;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;
        internal BitReader(ReadOnlySpan<byte> payload, int position) { _payload = payload; Position = position; }
        internal int Position { get; private set; }

        internal bool TryRead(int count, out uint value)
        {
            value = 0;
            if (count is < 1 or > 32 || count > (_payload.Length * 8L) - Position) return false;
            for (var index = 0; index < count; index++)
            {
                var bit = Position + index;
                value = (value << 1) | (uint)((_payload[bit / 8] >> (7 - (bit % 8))) & 1);
            }
            Position += count;
            return true;
        }

        internal bool TryReadBytes(int count, out byte[] bytes)
        {
            bytes = [];
            if ((Position & 7) != 0 || count > (_payload.Length - (Position / 8))) return false;
            bytes = _payload.Slice(Position / 8, count).ToArray();
            Position += count * 8;
            return true;
        }
    }
}
