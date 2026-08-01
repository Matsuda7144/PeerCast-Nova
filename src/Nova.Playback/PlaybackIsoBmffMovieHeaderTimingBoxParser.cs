using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieHeaderTimingBoxParseResult
{
    Parsed,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
    InvalidTimescale,
}

public sealed class PlaybackIsoBmffMovieHeaderTimingBox
{
    internal PlaybackIsoBmffMovieHeaderTimingBox(
        byte version,
        ulong creationTime,
        ulong modificationTime,
        uint timescale,
        ulong? durationUnits)
    {
        Version = version;
        CreationTime = creationTime;
        ModificationTime = modificationTime;
        Timescale = timescale;
        DurationUnits = durationUnits;
    }

    public byte Version { get; }

    public ulong CreationTime { get; }

    public ulong ModificationTime { get; }

    public uint Timescale { get; }

    public ulong? DurationUnits { get; }

    public bool HasKnownDuration => DurationUnits is not null;
}

public static class PlaybackIsoBmffMovieHeaderTimingBoxParser
{
    private const uint MovieHeaderBoxType = 0x6d766864;
    private const int Version0PayloadSize = 100;
    private const int Version1PayloadSize = 112;

    public static PlaybackIsoBmffMovieHeaderTimingBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffMovieHeaderTimingBox? timing)
    {
        timing = null;
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.InvalidPayload;
        }

        if (header.Type != MovieHeaderBoxType)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version is not 0 and not 1)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.NonzeroFlags;
        }

        var expectedPayloadSize = fullBox.Version == 0
            ? Version0PayloadSize
            : Version1PayloadSize;
        if (payload.Length != expectedPayloadSize)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.InvalidPayload;
        }

        ulong creationTime;
        ulong modificationTime;
        uint timescale;
        ulong duration;
        ulong unknownDuration;
        if (fullBox.Version == 0)
        {
            creationTime = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
            modificationTime = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4));
            timescale = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(16, 4));
            unknownDuration = uint.MaxValue;
        }
        else
        {
            creationTime = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(4, 8));
            modificationTime = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(12, 8));
            timescale = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(20, 4));
            duration = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(24, 8));
            unknownDuration = ulong.MaxValue;
        }

        if (timescale == 0)
        {
            return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.InvalidTimescale;
        }

        timing = new(
            fullBox.Version,
            creationTime,
            modificationTime,
            timescale,
            duration == unknownDuration ? null : duration);
        return PlaybackIsoBmffMovieHeaderTimingBoxParseResult.Parsed;
    }
}
