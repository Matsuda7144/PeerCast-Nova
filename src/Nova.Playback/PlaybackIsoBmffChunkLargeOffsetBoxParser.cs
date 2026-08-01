using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffChunkLargeOffsetBoxParseResult
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
}

public sealed record PlaybackIsoBmffChunkLargeOffsetBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffChunkLargeOffsetBox
{
    internal PlaybackIsoBmffChunkLargeOffsetBox(
        ReadOnlyCollection<ulong> chunkOffsets)
    {
        ChunkOffsets = chunkOffsets;
    }

    public IReadOnlyList<ulong> ChunkOffsets { get; }
}

public static class PlaybackIsoBmffChunkLargeOffsetBoxParser
{
    private const uint ChunkLargeOffsetBoxType = 0x636F3634;
    private const int FixedPayloadSize = 8;
    private const int EntrySize = 8;

    public static PlaybackIsoBmffChunkLargeOffsetBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffChunkLargeOffsetBoxParserOptions? options = null)
    {
        chunkLargeOffset = null;
        options ??= new PlaybackIsoBmffChunkLargeOffsetBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffChunkLargeOffsetBoxParserOptions
                .MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult
                .InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.InvalidPayload;
        }

        if (header.Type != ChunkLargeOffsetBoxType)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffChunkLargeOffsetBoxParseResult
                    .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.InvalidPayload;
        }

        var chunkOffsets = new ulong[entryCount];
        for (var index = 0; index < chunkOffsets.Length; index++)
        {
            chunkOffsets[index] = BinaryPrimitives.ReadUInt64BigEndian(
                payload.Slice(FixedPayloadSize + (index * EntrySize), EntrySize));
        }

        chunkLargeOffset = new(Array.AsReadOnly(chunkOffsets));
        return PlaybackIsoBmffChunkLargeOffsetBoxParseResult.Parsed;
    }
}
