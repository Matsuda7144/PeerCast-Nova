using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult
{
    Parsed,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
}

public sealed class PlaybackIsoBmffTrackFragmentDecodeTimeBox
{
    internal PlaybackIsoBmffTrackFragmentDecodeTimeBox(
        byte version,
        ulong baseMediaDecodeTime)
    {
        Version = version;
        BaseMediaDecodeTime = baseMediaDecodeTime;
    }

    public byte Version { get; }

    public ulong BaseMediaDecodeTime { get; }
}

public static class PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser
{
    private const uint TrackFragmentDecodeTimeBoxType = 0x74666474;
    private const int VersionZeroPayloadSize = 8;
    private const int VersionOnePayloadSize = 12;

    public static PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffTrackFragmentDecodeTimeBox? decodeTime)
    {
        decodeTime = null;
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return
                PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult
                    .InvalidPayload;
        }

        if (header.Type != TrackFragmentDecodeTimeBoxType)
        {
            return PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return
                    PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult
                        .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return
                PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult
                    .InvalidPayload;
        }

        if (fullBox.Version is not (0 or 1))
        {
            return
                PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult
                    .UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return
                PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult
                    .NonzeroFlags;
        }

        var expectedPayloadSize = fullBox.Version == 0
            ? VersionZeroPayloadSize
            : VersionOnePayloadSize;
        if (payload.Length != expectedPayloadSize)
        {
            return
                PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult
                    .InvalidPayload;
        }

        var baseMediaDecodeTime = fullBox.Version == 0
            ? BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4))
            : BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(4, 8));
        decodeTime = new(fullBox.Version, baseMediaDecodeTime);
        return PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.Parsed;
    }
}
