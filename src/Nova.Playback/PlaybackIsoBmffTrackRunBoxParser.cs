using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackRunBoxParseResult
{
    Parsed,
    InvalidConfiguration,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    UnknownFlags,
    InvalidFlagCombination,
    InvalidPayload,
    EntryLimitExceeded,
}

public sealed record PlaybackIsoBmffTrackRunBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public readonly record struct PlaybackIsoBmffTrackRunEntry(
    uint? SampleDuration,
    uint? SampleSize,
    uint? SampleFlags,
    long? SampleCompositionTimeOffset);

public sealed class PlaybackIsoBmffTrackRunBox
{
    internal PlaybackIsoBmffTrackRunBox(
        byte version,
        uint sampleCount,
        int? dataOffset,
        uint? firstSampleFlags,
        ReadOnlyCollection<PlaybackIsoBmffTrackRunEntry> entries)
    {
        Version = version;
        SampleCount = sampleCount;
        DataOffset = dataOffset;
        FirstSampleFlags = firstSampleFlags;
        Entries = entries;
    }

    public byte Version { get; }

    public uint SampleCount { get; }

    public int? DataOffset { get; }

    public uint? FirstSampleFlags { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackRunEntry> Entries { get; }
}

public static class PlaybackIsoBmffTrackRunBoxParser
{
    private const uint TrackRunBoxType = 0x7472756E;
    private const uint DataOffsetPresent = 0x000001;
    private const uint FirstSampleFlagsPresent = 0x000004;
    private const uint SampleDurationPresent = 0x000100;
    private const uint SampleSizePresent = 0x000200;
    private const uint SampleFlagsPresent = 0x000400;
    private const uint SampleCompositionTimeOffsetPresent = 0x000800;
    private const uint KnownFlags =
        DataOffsetPresent |
        FirstSampleFlagsPresent |
        SampleDurationPresent |
        SampleSizePresent |
        SampleFlagsPresent |
        SampleCompositionTimeOffsetPresent;
    private const int FixedPayloadSize = 8;

    public static PlaybackIsoBmffTrackRunBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffTrackRunBox? trackRun,
        PlaybackIsoBmffTrackRunBoxParserOptions? options = null)
    {
        trackRun = null;
        options ??= new PlaybackIsoBmffTrackRunBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffTrackRunBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.InvalidPayload;
        }

        if (header.Type != TrackRunBoxType)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackRunBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffTrackRunBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version is not (0 or 1))
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.UnsupportedVersion;
        }

        if ((fullBox.Flags & ~KnownFlags) != 0)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.UnknownFlags;
        }

        if ((fullBox.Flags & FirstSampleFlagsPresent) != 0 &&
            (fullBox.Flags & SampleFlagsPresent) != 0)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.InvalidFlagCombination;
        }

        var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (sampleCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.EntryLimitExceeded;
        }

        var optionalHeaderSize =
            ((fullBox.Flags & DataOffsetPresent) != 0 ? 4 : 0) +
            ((fullBox.Flags & FirstSampleFlagsPresent) != 0 ? 4 : 0);
        var entrySize =
            ((fullBox.Flags & SampleDurationPresent) != 0 ? 4 : 0) +
            ((fullBox.Flags & SampleSizePresent) != 0 ? 4 : 0) +
            ((fullBox.Flags & SampleFlagsPresent) != 0 ? 4 : 0) +
            ((fullBox.Flags & SampleCompositionTimeOffsetPresent) != 0 ? 4 : 0);
        var expectedPayloadSize =
            FixedPayloadSize + optionalHeaderSize +
            (checked((int)sampleCount) * entrySize);
        if (payload.Length != expectedPayloadSize)
        {
            return PlaybackIsoBmffTrackRunBoxParseResult.InvalidPayload;
        }

        var cursor = FixedPayloadSize;
        int? dataOffset = null;
        uint? firstSampleFlags = null;
        if ((fullBox.Flags & DataOffsetPresent) != 0)
        {
            dataOffset = BinaryPrimitives.ReadInt32BigEndian(
                payload.Slice(cursor, 4));
            cursor += 4;
        }

        if ((fullBox.Flags & FirstSampleFlagsPresent) != 0)
        {
            firstSampleFlags = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(cursor, 4));
            cursor += 4;
        }

        var entries = new PlaybackIsoBmffTrackRunEntry[sampleCount];
        for (var index = 0; index < entries.Length; index++)
        {
            uint? sampleDuration = null;
            uint? sampleSize = null;
            uint? sampleFlags = null;
            long? compositionTimeOffset = null;
            if ((fullBox.Flags & SampleDurationPresent) != 0)
            {
                sampleDuration = BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(cursor, 4));
                cursor += 4;
            }

            if ((fullBox.Flags & SampleSizePresent) != 0)
            {
                sampleSize = BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(cursor, 4));
                cursor += 4;
            }

            if ((fullBox.Flags & SampleFlagsPresent) != 0)
            {
                sampleFlags = BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(cursor, 4));
                cursor += 4;
            }

            if ((fullBox.Flags & SampleCompositionTimeOffsetPresent) != 0)
            {
                compositionTimeOffset = fullBox.Version == 0
                    ? BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(cursor, 4))
                    : BinaryPrimitives.ReadInt32BigEndian(payload.Slice(cursor, 4));
                cursor += 4;
            }

            entries[index] = new(
                sampleDuration,
                sampleSize,
                sampleFlags,
                compositionTimeOffset);
        }

        trackRun = new(
            fullBox.Version,
            sampleCount,
            dataOffset,
            firstSampleFlags,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackRunBoxParseResult.Parsed;
    }
}
