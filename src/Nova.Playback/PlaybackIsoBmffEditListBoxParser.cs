using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditListBoxParseResult
{
    Parsed,
    InvalidConfiguration,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
    EntryLimitExceeded,
    AggregateOverflow,
}

public sealed record PlaybackIsoBmffEditListBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public readonly record struct PlaybackIsoBmffEditListEntry(
    ulong SegmentDurationUnits,
    long MediaTimeUnits,
    short MediaRateInteger,
    short MediaRateFraction)
{
    public bool IsEmptyEdit => MediaTimeUnits == -1;
}

public sealed class PlaybackIsoBmffEditListBox
{
    internal PlaybackIsoBmffEditListBox(
        byte version,
        ReadOnlyCollection<PlaybackIsoBmffEditListEntry> entries,
        ulong totalSegmentDurationUnits)
    {
        Version = version;
        Entries = entries;
        TotalSegmentDurationUnits = totalSegmentDurationUnits;
    }

    public byte Version { get; }

    public IReadOnlyList<PlaybackIsoBmffEditListEntry> Entries { get; }

    public ulong TotalSegmentDurationUnits { get; }
}

public static class PlaybackIsoBmffEditListBoxParser
{
    private const uint EditListBoxType = 0x656C7374;
    private const int FixedPayloadSize = 8;
    private const int Version0EntrySize = 12;
    private const int Version1EntrySize = 20;

    public static PlaybackIsoBmffEditListBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffEditListBoxParserOptions? options = null)
    {
        editList = null;
        options ??= new PlaybackIsoBmffEditListBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffEditListBoxParserOptions
                .MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffEditListBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffEditListBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffEditListBoxParseResult.InvalidPayload;
        }

        if (header.Type != EditListBoxType)
        {
            return PlaybackIsoBmffEditListBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffEditListBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffEditListBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffEditListBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version is not 0 and not 1)
        {
            return PlaybackIsoBmffEditListBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffEditListBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffEditListBoxParseResult.EntryLimitExceeded;
        }

        var entrySize = fullBox.Version == 0
            ? Version0EntrySize
            : Version1EntrySize;
        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * entrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffEditListBoxParseResult.InvalidPayload;
        }

        var entries = new PlaybackIsoBmffEditListEntry[entryCount];
        ulong totalSegmentDurationUnits = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = FixedPayloadSize + (index * entrySize);
            ulong segmentDurationUnits;
            long mediaTimeUnits;
            int rateOffset;
            if (fullBox.Version == 0)
            {
                segmentDurationUnits = BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(offset, 4));
                mediaTimeUnits = BinaryPrimitives.ReadInt32BigEndian(
                    payload.Slice(offset + 4, 4));
                rateOffset = offset + 8;
            }
            else
            {
                segmentDurationUnits = BinaryPrimitives.ReadUInt64BigEndian(
                    payload.Slice(offset, 8));
                mediaTimeUnits = BinaryPrimitives.ReadInt64BigEndian(
                    payload.Slice(offset + 8, 8));
                rateOffset = offset + 16;
            }

            try
            {
                totalSegmentDurationUnits = checked(
                    totalSegmentDurationUnits + segmentDurationUnits);
            }
            catch (OverflowException)
            {
                return PlaybackIsoBmffEditListBoxParseResult.AggregateOverflow;
            }

            entries[index] = new(
                segmentDurationUnits,
                mediaTimeUnits,
                BinaryPrimitives.ReadInt16BigEndian(
                    payload.Slice(rateOffset, 2)),
                BinaryPrimitives.ReadInt16BigEndian(
                    payload.Slice(rateOffset + 2, 2)));
        }

        editList = new(
            fullBox.Version,
            Array.AsReadOnly(entries),
            totalSegmentDurationUnits);
        return PlaybackIsoBmffEditListBoxParseResult.Parsed;
    }
}
