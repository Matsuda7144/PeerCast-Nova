using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackFragmentHeaderBoxParseResult
{
    Parsed,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    UnknownFlags,
    InvalidPayload,
    InvalidTrackId,
}

public sealed class PlaybackIsoBmffTrackFragmentHeaderBox
{
    internal PlaybackIsoBmffTrackFragmentHeaderBox(
        uint trackId,
        ulong? baseDataOffset,
        uint? sampleDescriptionIndex,
        uint? defaultSampleDuration,
        uint? defaultSampleSize,
        uint? defaultSampleFlags,
        bool durationIsEmpty,
        bool defaultBaseIsMoof)
    {
        TrackId = trackId;
        BaseDataOffset = baseDataOffset;
        SampleDescriptionIndex = sampleDescriptionIndex;
        DefaultSampleDuration = defaultSampleDuration;
        DefaultSampleSize = defaultSampleSize;
        DefaultSampleFlags = defaultSampleFlags;
        DurationIsEmpty = durationIsEmpty;
        DefaultBaseIsMoof = defaultBaseIsMoof;
    }

    public uint TrackId { get; }

    public ulong? BaseDataOffset { get; }

    public uint? SampleDescriptionIndex { get; }

    public uint? DefaultSampleDuration { get; }

    public uint? DefaultSampleSize { get; }

    public uint? DefaultSampleFlags { get; }

    public bool DurationIsEmpty { get; }

    public bool DefaultBaseIsMoof { get; }
}

public static class PlaybackIsoBmffTrackFragmentHeaderBoxParser
{
    private const uint TrackFragmentHeaderBoxType = 0x74666864;
    private const uint BaseDataOffsetPresent = 0x000001;
    private const uint SampleDescriptionIndexPresent = 0x000002;
    private const uint DefaultSampleDurationPresent = 0x000008;
    private const uint DefaultSampleSizePresent = 0x000010;
    private const uint DefaultSampleFlagsPresent = 0x000020;
    private const uint DurationIsEmpty = 0x010000;
    private const uint DefaultBaseIsMoof = 0x020000;
    private const uint KnownFlags =
        BaseDataOffsetPresent |
        SampleDescriptionIndexPresent |
        DefaultSampleDurationPresent |
        DefaultSampleSizePresent |
        DefaultSampleFlagsPresent |
        DurationIsEmpty |
        DefaultBaseIsMoof;
    private const int RequiredPayloadSize = 8;

    public static PlaybackIsoBmffTrackFragmentHeaderBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffTrackFragmentHeaderBox? fragmentHeader)
    {
        fragmentHeader = null;
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
        }

        if (header.Type != TrackFragmentHeaderBoxType)
        {
            return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return
                    PlaybackIsoBmffTrackFragmentHeaderBoxParseResult
                        .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < RequiredPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return
                PlaybackIsoBmffTrackFragmentHeaderBoxParseResult
                    .UnsupportedVersion;
        }

        if ((fullBox.Flags & ~KnownFlags) != 0)
        {
            return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.UnknownFlags;
        }

        var trackId = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (trackId == 0)
        {
            return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidTrackId;
        }

        var cursor = RequiredPayloadSize;
        ulong? baseDataOffset = null;
        uint? sampleDescriptionIndex = null;
        uint? defaultSampleDuration = null;
        uint? defaultSampleSize = null;
        uint? defaultSampleFlags = null;

        if ((fullBox.Flags & BaseDataOffsetPresent) != 0)
        {
            if (!TryReadUInt64(payload, ref cursor, out var value))
            {
                return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
            }

            baseDataOffset = value;
        }

        if ((fullBox.Flags & SampleDescriptionIndexPresent) != 0)
        {
            if (!TryReadUInt32(payload, ref cursor, out var value))
            {
                return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
            }

            sampleDescriptionIndex = value;
        }

        if ((fullBox.Flags & DefaultSampleDurationPresent) != 0)
        {
            if (!TryReadUInt32(payload, ref cursor, out var value))
            {
                return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
            }

            defaultSampleDuration = value;
        }

        if ((fullBox.Flags & DefaultSampleSizePresent) != 0)
        {
            if (!TryReadUInt32(payload, ref cursor, out var value))
            {
                return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
            }

            defaultSampleSize = value;
        }

        if ((fullBox.Flags & DefaultSampleFlagsPresent) != 0)
        {
            if (!TryReadUInt32(payload, ref cursor, out var value))
            {
                return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
            }

            defaultSampleFlags = value;
        }

        if (cursor != payload.Length)
        {
            return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.InvalidPayload;
        }

        fragmentHeader = new(
            trackId,
            baseDataOffset,
            sampleDescriptionIndex,
            defaultSampleDuration,
            defaultSampleSize,
            defaultSampleFlags,
            (fullBox.Flags & DurationIsEmpty) != 0,
            (fullBox.Flags & DefaultBaseIsMoof) != 0);
        return PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed;
    }

    private static bool TryReadUInt32(
        ReadOnlySpan<byte> payload,
        ref int cursor,
        out uint value)
    {
        if (payload.Length - cursor < sizeof(uint))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(cursor, 4));
        cursor += sizeof(uint);
        return true;
    }

    private static bool TryReadUInt64(
        ReadOnlySpan<byte> payload,
        ref int cursor,
        out ulong value)
    {
        if (payload.Length - cursor < sizeof(ulong))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(cursor, 8));
        cursor += sizeof(ulong);
        return true;
    }
}
