using System.Buffers.Binary;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackHeaderIdentityBoxParseResult
{
    Parsed,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    UnsupportedFlags,
    InvalidPayload,
    InvalidTrackId,
}

public sealed class PlaybackIsoBmffTrackHeaderIdentityBox
{
    internal PlaybackIsoBmffTrackHeaderIdentityBox(
        byte version,
        uint flags,
        ulong creationTime,
        ulong modificationTime,
        uint trackId,
        ulong? durationUnits,
        uint widthFixedPoint,
        uint heightFixedPoint)
    {
        Version = version;
        Flags = flags;
        CreationTime = creationTime;
        ModificationTime = modificationTime;
        TrackId = trackId;
        DurationUnits = durationUnits;
        WidthFixedPoint = widthFixedPoint;
        HeightFixedPoint = heightFixedPoint;
    }

    public byte Version { get; }

    public uint Flags { get; }

    public bool IsEnabled => (Flags & 1) != 0;

    public bool IsInMovie => (Flags & 2) != 0;

    public bool IsInPreview => (Flags & 4) != 0;

    public bool SizeIsAspectRatio => (Flags & 8) != 0;

    public ulong CreationTime { get; }

    public ulong ModificationTime { get; }

    public uint TrackId { get; }

    public ulong? DurationUnits { get; }

    public bool HasKnownDuration => DurationUnits is not null;

    public uint WidthFixedPoint { get; }

    public uint HeightFixedPoint { get; }
}

public static class PlaybackIsoBmffTrackHeaderIdentityBoxParser
{
    private const uint TrackHeaderBoxType = 0x746b6864;
    private const uint SupportedFlagsMask = 0x00000f;
    private const int Version0PayloadSize = 84;
    private const int Version1PayloadSize = 96;

    public static PlaybackIsoBmffTrackHeaderIdentityBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffTrackHeaderIdentityBox? identity)
    {
        identity = null;
        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidPayload;
        }

        if (header.Type != TrackHeaderBoxType)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version is not 0 and not 1)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.UnsupportedVersion;
        }

        if ((fullBox.Flags & ~SupportedFlagsMask) != 0)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.UnsupportedFlags;
        }

        var expectedPayloadSize = fullBox.Version == 0
            ? Version0PayloadSize
            : Version1PayloadSize;
        if (payload.Length != expectedPayloadSize)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidPayload;
        }

        ulong creationTime;
        ulong modificationTime;
        uint trackId;
        ulong duration;
        ulong unknownDuration;
        int firstReservedOffset;
        int secondReservedOffset;
        int dimensionsOffset;
        if (fullBox.Version == 0)
        {
            creationTime = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
            modificationTime = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4));
            trackId = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(20, 4));
            unknownDuration = uint.MaxValue;
            firstReservedOffset = 16;
            secondReservedOffset = 24;
            dimensionsOffset = 76;
        }
        else
        {
            creationTime = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(4, 8));
            modificationTime = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(12, 8));
            trackId = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(20, 4));
            duration = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(28, 8));
            unknownDuration = ulong.MaxValue;
            firstReservedOffset = 24;
            secondReservedOffset = 36;
            dimensionsOffset = 88;
        }

        if (trackId == 0)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidTrackId;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(firstReservedOffset, 4)) != 0 ||
            BinaryPrimitives.ReadUInt64BigEndian(
                payload.Slice(secondReservedOffset, 8)) != 0 ||
            BinaryPrimitives.ReadUInt16BigEndian(
                payload.Slice(dimensionsOffset - 38, 2)) != 0)
        {
            return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.InvalidPayload;
        }

        identity = new(
            fullBox.Version,
            fullBox.Flags,
            creationTime,
            modificationTime,
            trackId,
            duration == unknownDuration ? null : duration,
            BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(dimensionsOffset, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(dimensionsOffset + 4, 4)));
        return PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.Parsed;
    }
}
