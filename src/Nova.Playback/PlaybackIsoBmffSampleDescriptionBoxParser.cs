using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleDescriptionBoxParseResult
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
    EntrySequenceMismatch,
}

public sealed record PlaybackIsoBmffSampleDescriptionBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 16;
    public const int MaximumEntryCountHardLimit = 256;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffSampleDescriptionBox
{
    internal PlaybackIsoBmffSampleDescriptionBox(
        ReadOnlyCollection<PlaybackIsoBmffBoxLocation> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<PlaybackIsoBmffBoxLocation> Entries { get; }
}

public static class PlaybackIsoBmffSampleDescriptionBoxParser
{
    private const uint SampleDescriptionBoxType = 0x73747364;
    private const int FixedPayloadSize = 8;

    public static PlaybackIsoBmffSampleDescriptionBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffSampleDescriptionBox? sampleDescription,
        PlaybackIsoBmffSampleDescriptionBoxParserOptions? options = null)
    {
        sampleDescription = null;
        options ??= new PlaybackIsoBmffSampleDescriptionBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffSampleDescriptionBoxParserOptions
                .MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.InvalidPayload;
        }

        if (header.Type != SampleDescriptionBoxType)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleDescriptionBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleDescriptionBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult.EntryLimitExceeded;
        }

        var entriesPayload = payload[FixedPayloadSize..];
        var entries = new PlaybackIsoBmffBoxLocation[entryCount];
        var entryOffset = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    entriesPayload,
                    entryOffset,
                    out var entry) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                entry is null)
            {
                return PlaybackIsoBmffSampleDescriptionBoxParseResult
                    .EntrySequenceMismatch;
            }

            entries[index] = new(
                entry.Header,
                header.HeaderSize + FixedPayloadSize + entry.Offset,
                entry.BoxLength);
            entryOffset = entry.NextOffset;
        }

        if (entryOffset != entriesPayload.Length)
        {
            return PlaybackIsoBmffSampleDescriptionBoxParseResult
                .EntrySequenceMismatch;
        }

        sampleDescription = new(Array.AsReadOnly(entries));
        return PlaybackIsoBmffSampleDescriptionBoxParseResult.Parsed;
    }
}
