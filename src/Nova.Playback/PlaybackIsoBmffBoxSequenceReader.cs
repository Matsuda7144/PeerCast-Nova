namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffBoxSequenceReadResult
{
    BoxAvailable,
    EndOfContainer,
    InvalidOffset,
    InvalidHeader,
    TruncatedBox,
}

public sealed class PlaybackIsoBmffBoxLocation
{
    internal PlaybackIsoBmffBoxLocation(
        PlaybackIsoBmffBoxHeader header,
        int offset,
        int boxLength)
    {
        Header = header;
        Offset = offset;
        BoxLength = boxLength;
    }

    public PlaybackIsoBmffBoxHeader Header { get; }

    public int Offset { get; }

    public int BoxLength { get; }

    public int PayloadOffset => Offset + Header.HeaderSize;

    public int PayloadLength => BoxLength - Header.HeaderSize;

    public int NextOffset => Offset + BoxLength;
}

public static class PlaybackIsoBmffBoxSequenceReader
{
    public static PlaybackIsoBmffBoxSequenceReadResult ReadNext(
        ReadOnlySpan<byte> container,
        int offset,
        out PlaybackIsoBmffBoxLocation? box)
    {
        box = null;
        if (offset < 0 || offset > container.Length)
        {
            return PlaybackIsoBmffBoxSequenceReadResult.InvalidOffset;
        }

        if (offset == container.Length)
        {
            return PlaybackIsoBmffBoxSequenceReadResult.EndOfContainer;
        }

        var remaining = container[offset..];
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(
            remaining,
            out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffBoxSequenceReadResult.TruncatedBox;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffBoxSequenceReadResult.InvalidHeader;
        }

        int boxLength;
        if (header.BoxSize is null)
        {
            boxLength = remaining.Length;
        }
        else if (header.BoxSize > (ulong)remaining.Length)
        {
            return PlaybackIsoBmffBoxSequenceReadResult.TruncatedBox;
        }
        else
        {
            boxLength = checked((int)header.BoxSize.Value);
        }

        box = new(header, offset, boxLength);
        return PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable;
    }
}
