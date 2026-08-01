using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTimeToSampleBoxParseResult
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
    InvalidSampleCount,
    AggregateOverflow,
}

public sealed record PlaybackIsoBmffTimeToSampleBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public readonly record struct PlaybackIsoBmffTimeToSampleEntry(
    uint SampleCount,
    uint SampleDelta);

public sealed class PlaybackIsoBmffTimeToSampleBox
{
    internal PlaybackIsoBmffTimeToSampleBox(
        ReadOnlyCollection<PlaybackIsoBmffTimeToSampleEntry> entries,
        ulong totalSampleCount,
        ulong totalDurationUnits)
    {
        Entries = entries;
        TotalSampleCount = totalSampleCount;
        TotalDurationUnits = totalDurationUnits;
    }

    public IReadOnlyList<PlaybackIsoBmffTimeToSampleEntry> Entries { get; }

    public ulong TotalSampleCount { get; }

    public ulong TotalDurationUnits { get; }
}

public static class PlaybackIsoBmffTimeToSampleBoxParser
{
    private const uint TimeToSampleBoxType = 0x73747473;
    private const int FixedPayloadSize = 8;
    private const int EntrySize = 8;

    public static PlaybackIsoBmffTimeToSampleBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffTimeToSampleBoxParserOptions? options = null)
    {
        timeToSample = null;
        options ??= new PlaybackIsoBmffTimeToSampleBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffTimeToSampleBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidPayload;
        }

        if (header.Type != TimeToSampleBoxType)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffTimeToSampleBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffTimeToSampleBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidPayload;
        }

        var entries = new PlaybackIsoBmffTimeToSampleEntry[entryCount];
        ulong totalSampleCount = 0;
        ulong totalDurationUnits = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = FixedPayloadSize + (index * EntrySize);
            var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(offset, 4));
            if (sampleCount == 0)
            {
                return PlaybackIsoBmffTimeToSampleBoxParseResult.InvalidSampleCount;
            }

            var sampleDelta = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(offset + 4, 4));
            try
            {
                totalSampleCount = checked(totalSampleCount + sampleCount);
                totalDurationUnits = checked(
                    totalDurationUnits + ((ulong)sampleCount * sampleDelta));
            }
            catch (OverflowException)
            {
                return PlaybackIsoBmffTimeToSampleBoxParseResult.AggregateOverflow;
            }

            entries[index] = new(sampleCount, sampleDelta);
        }

        timeToSample = new(
            Array.AsReadOnly(entries),
            totalSampleCount,
            totalDurationUnits);
        return PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed;
    }
}
