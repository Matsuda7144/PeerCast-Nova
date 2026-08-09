using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieFragmentHeaderBoxParseResult
{
    Parsed,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
}

public sealed class PlaybackIsoBmffMovieFragmentHeaderBox
{
    internal PlaybackIsoBmffMovieFragmentHeaderBox(uint sequenceNumber)
    {
        SequenceNumber = sequenceNumber;
    }

    public uint SequenceNumber { get; }
}

public static class PlaybackIsoBmffMovieFragmentHeaderBoxParser
{
    private const uint MovieFragmentHeaderBoxType = 0x6D666864;
    private const int PayloadSize = 8;

    public static PlaybackIsoBmffMovieFragmentHeaderBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffMovieFragmentHeaderBox? fragmentHeader)
    {
        fragmentHeader = null;
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.InvalidPayload;
        }

        if (header.Type != MovieFragmentHeaderBoxType)
        {
            return PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return
                    PlaybackIsoBmffMovieFragmentHeaderBoxParseResult
                        .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length != PayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return
                PlaybackIsoBmffMovieFragmentHeaderBoxParseResult
                    .UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.NonzeroFlags;
        }

        fragmentHeader = new(
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)));
        return PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.Parsed;
    }
}
