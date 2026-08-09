using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackExtendsBoxParseResult
{
    Parsed,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
    InvalidTrackId,
    InvalidSampleDescriptionIndex,
}

public sealed class PlaybackIsoBmffTrackExtendsBox
{
    internal PlaybackIsoBmffTrackExtendsBox(
        uint trackId,
        uint defaultSampleDescriptionIndex,
        uint defaultSampleDuration,
        uint defaultSampleSize,
        uint defaultSampleFlags)
    {
        TrackId = trackId;
        DefaultSampleDescriptionIndex = defaultSampleDescriptionIndex;
        DefaultSampleDuration = defaultSampleDuration;
        DefaultSampleSize = defaultSampleSize;
        DefaultSampleFlags = defaultSampleFlags;
    }

    public uint TrackId { get; }

    public uint DefaultSampleDescriptionIndex { get; }

    public uint DefaultSampleDuration { get; }

    public uint DefaultSampleSize { get; }

    public uint DefaultSampleFlags { get; }
}

public static class PlaybackIsoBmffTrackExtendsBoxParser
{
    private const uint TrackExtendsBoxType = 0x74726578;
    private const int PayloadSize = 24;

    public static PlaybackIsoBmffTrackExtendsBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffTrackExtendsBox? trackExtends)
    {
        trackExtends = null;
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffTrackExtendsBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffTrackExtendsBoxParseResult.InvalidPayload;
        }

        if (header.Type != TrackExtendsBoxType)
        {
            return PlaybackIsoBmffTrackExtendsBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackExtendsBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackExtendsBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length != PayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffTrackExtendsBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffTrackExtendsBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffTrackExtendsBoxParseResult.NonzeroFlags;
        }

        var trackId = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (trackId == 0)
        {
            return PlaybackIsoBmffTrackExtendsBoxParseResult.InvalidTrackId;
        }

        var sampleDescriptionIndex =
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4));
        if (sampleDescriptionIndex == 0)
        {
            return
                PlaybackIsoBmffTrackExtendsBoxParseResult
                    .InvalidSampleDescriptionIndex;
        }

        trackExtends = new(
            trackId,
            sampleDescriptionIndex,
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(16, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(20, 4)));
        return PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed;
    }
}
