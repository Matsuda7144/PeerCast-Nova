using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
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
}

public sealed record PlaybackIsoBmffCompositionTimeToSampleBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public readonly record struct PlaybackIsoBmffCompositionTimeToSampleEntry(
    uint SampleCount,
    long SampleOffset);

public sealed class PlaybackIsoBmffCompositionTimeToSampleBox
{
    internal PlaybackIsoBmffCompositionTimeToSampleBox(
        byte version,
        ReadOnlyCollection<PlaybackIsoBmffCompositionTimeToSampleEntry> entries,
        ulong totalSampleCount)
    {
        Version = version;
        Entries = entries;
        TotalSampleCount = totalSampleCount;
    }

    public byte Version { get; }

    public IReadOnlyList<PlaybackIsoBmffCompositionTimeToSampleEntry> Entries
    { get; }

    public ulong TotalSampleCount { get; }
}

public static class PlaybackIsoBmffCompositionTimeToSampleBoxParser
{
    private const uint CompositionTimeToSampleBoxType = 0x63747473;
    private const int FixedPayloadSize = 8;
    private const int EntrySize = 8;

    public static PlaybackIsoBmffCompositionTimeToSampleBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffCompositionTimeToSampleBox? compositionTime,
        PlaybackIsoBmffCompositionTimeToSampleBoxParserOptions? options = null)
    {
        compositionTime = null;
        options ??= new PlaybackIsoBmffCompositionTimeToSampleBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffCompositionTimeToSampleBoxParserOptions
                .MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
                .InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.InvalidPayload;
        }

        if (header.Type != CompositionTimeToSampleBoxType)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
                    .NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
                    .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version is not 0 and not 1)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
                .UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
                .EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.InvalidPayload;
        }

        var entries =
            new PlaybackIsoBmffCompositionTimeToSampleEntry[entryCount];
        ulong totalSampleCount = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = FixedPayloadSize + (index * EntrySize);
            var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(offset, 4));
            if (sampleCount == 0)
            {
                return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult
                    .InvalidSampleCount;
            }

            totalSampleCount = checked(totalSampleCount + sampleCount);

            var sampleOffset = fullBox.Version == 0
                ? (long)BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(offset + 4, 4))
                : (long)BinaryPrimitives.ReadInt32BigEndian(
                    payload.Slice(offset + 4, 4));
            entries[index] = new(sampleCount, sampleOffset);
        }

        compositionTime = new(
            fullBox.Version,
            Array.AsReadOnly(entries),
            totalSampleCount);
        return PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed;
    }
}
