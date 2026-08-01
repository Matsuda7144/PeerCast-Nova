using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleToChunkBoxParseResult
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
    InvalidFirstChunk,
    NonIncreasingFirstChunk,
    InvalidSamplesPerChunk,
    InvalidSampleDescriptionIndex,
}

public sealed record PlaybackIsoBmffSampleToChunkBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public readonly record struct PlaybackIsoBmffSampleToChunkEntry(
    uint FirstChunk,
    uint SamplesPerChunk,
    uint SampleDescriptionIndex);

public sealed class PlaybackIsoBmffSampleToChunkBox
{
    internal PlaybackIsoBmffSampleToChunkBox(
        ReadOnlyCollection<PlaybackIsoBmffSampleToChunkEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<PlaybackIsoBmffSampleToChunkEntry> Entries { get; }
}

public static class PlaybackIsoBmffSampleToChunkBoxParser
{
    private const uint SampleToChunkBoxType = 0x73747363;
    private const int FixedPayloadSize = 8;
    private const int EntrySize = 12;

    public static PlaybackIsoBmffSampleToChunkBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
        PlaybackIsoBmffSampleToChunkBoxParserOptions? options = null)
    {
        sampleToChunk = null;
        options ??= new PlaybackIsoBmffSampleToChunkBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffSampleToChunkBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidPayload;
        }

        if (header.Type != SampleToChunkBoxType)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleToChunkBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleToChunkBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidPayload;
        }

        var entries = new PlaybackIsoBmffSampleToChunkEntry[entryCount];
        uint previousFirstChunk = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = FixedPayloadSize + (index * EntrySize);
            var firstChunk = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(offset, 4));
            if (firstChunk == 0 || (index == 0 && firstChunk != 1))
            {
                return PlaybackIsoBmffSampleToChunkBoxParseResult.InvalidFirstChunk;
            }

            if (firstChunk <= previousFirstChunk)
            {
                return PlaybackIsoBmffSampleToChunkBoxParseResult
                    .NonIncreasingFirstChunk;
            }

            var samplesPerChunk = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(offset + 4, 4));
            if (samplesPerChunk == 0)
            {
                return PlaybackIsoBmffSampleToChunkBoxParseResult
                    .InvalidSamplesPerChunk;
            }

            var sampleDescriptionIndex = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(offset + 8, 4));
            if (sampleDescriptionIndex == 0)
            {
                return PlaybackIsoBmffSampleToChunkBoxParseResult
                    .InvalidSampleDescriptionIndex;
            }

            entries[index] = new(
                firstChunk,
                samplesPerChunk,
                sampleDescriptionIndex);
            previousFirstChunk = firstChunk;
        }

        sampleToChunk = new(Array.AsReadOnly(entries));
        return PlaybackIsoBmffSampleToChunkBoxParseResult.Parsed;
    }
}
