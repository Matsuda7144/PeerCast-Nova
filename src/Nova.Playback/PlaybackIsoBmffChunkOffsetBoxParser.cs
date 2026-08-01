using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffChunkOffsetBoxParseResult
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

public sealed record PlaybackIsoBmffChunkOffsetBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffChunkOffsetBox
{
    internal PlaybackIsoBmffChunkOffsetBox(
        ReadOnlyCollection<uint> chunkOffsets)
    {
        ChunkOffsets = chunkOffsets;
    }

    public IReadOnlyList<uint> ChunkOffsets { get; }
}

public static class PlaybackIsoBmffChunkOffsetBoxParser
{
    private const uint ChunkOffsetBoxType = 0x7374636F;
    private const int FixedPayloadSize = 8;
    private const int EntrySize = 4;

    public static PlaybackIsoBmffChunkOffsetBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkOffsetBoxParserOptions? options = null)
    {
        chunkOffset = null;
        options ??= new PlaybackIsoBmffChunkOffsetBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffChunkOffsetBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.InvalidPayload;
        }

        if (header.Type != ChunkOffsetBoxType)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffChunkOffsetBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffChunkOffsetBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffChunkOffsetBoxParseResult.InvalidPayload;
        }

        var chunkOffsets = new uint[entryCount];
        for (var index = 0; index < chunkOffsets.Length; index++)
        {
            chunkOffsets[index] = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(FixedPayloadSize + (index * EntrySize), EntrySize));
        }

        chunkOffset = new(Array.AsReadOnly(chunkOffsets));
        return PlaybackIsoBmffChunkOffsetBoxParseResult.Parsed;
    }
}
