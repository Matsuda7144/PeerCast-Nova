using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffCompactSampleSizeBoxParseResult
{
    Parsed,
    InvalidConfiguration,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    InvalidPayload,
    NonzeroReservedFields,
    UnsupportedFieldSize,
    EntryLimitExceeded,
}

public sealed record PlaybackIsoBmffCompactSampleSizeBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffCompactSampleSizeBox
{
    internal PlaybackIsoBmffCompactSampleSizeBox(
        byte fieldSize,
        ReadOnlyCollection<ushort> sampleSizes,
        ulong totalSampleBytes)
    {
        FieldSize = fieldSize;
        SampleSizes = sampleSizes;
        TotalSampleBytes = totalSampleBytes;
    }

    public byte FieldSize { get; }

    public uint SampleCount => (uint)SampleSizes.Count;

    public IReadOnlyList<ushort> SampleSizes { get; }

    public ulong TotalSampleBytes { get; }
}

public static class PlaybackIsoBmffCompactSampleSizeBoxParser
{
    private const uint CompactSampleSizeBoxType = 0x73747A32;
    private const int FixedPayloadSize = 12;

    public static PlaybackIsoBmffCompactSampleSizeBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        PlaybackIsoBmffCompactSampleSizeBoxParserOptions? options = null)
    {
        compactSampleSize = null;
        options ??= new PlaybackIsoBmffCompactSampleSizeBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffCompactSampleSizeBoxParserOptions
                .MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult
                .InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult.InvalidPayload;
        }

        if (header.Type != CompactSampleSizeBoxType)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffCompactSampleSizeBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffCompactSampleSizeBoxParseResult
                    .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult.NonzeroFlags;
        }

        if (payload[4] != 0 || payload[5] != 0 || payload[6] != 0)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult
                .NonzeroReservedFields;
        }

        var fieldSize = payload[7];
        if (fieldSize is not (4 or 8 or 16))
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult
                .UnsupportedFieldSize;
        }

        var sampleCount =
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4));
        if (sampleCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult
                .EntryLimitExceeded;
        }

        var entryByteCount = fieldSize switch
        {
            4 => (checked((int)sampleCount) + 1) / 2,
            8 => checked((int)sampleCount),
            _ => checked((int)sampleCount) * 2,
        };
        if (payload.Length != FixedPayloadSize + entryByteCount)
        {
            return PlaybackIsoBmffCompactSampleSizeBoxParseResult.InvalidPayload;
        }

        var sampleSizes = new ushort[sampleCount];
        ulong totalSampleBytes = 0;
        for (var index = 0; index < sampleSizes.Length; index++)
        {
            ushort sampleSize = fieldSize switch
            {
                4 => (ushort)((payload[FixedPayloadSize + (index / 2)] >>
                    (index % 2 == 0 ? 4 : 0)) & 0x0f),
                8 => payload[FixedPayloadSize + index],
                _ => BinaryPrimitives.ReadUInt16BigEndian(
                    payload.Slice(FixedPayloadSize + (index * 2), 2)),
            };
            sampleSizes[index] = sampleSize;
            totalSampleBytes += sampleSize;
        }

        compactSampleSize = new(
            fieldSize,
            Array.AsReadOnly(sampleSizes),
            totalSampleBytes);
        return PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed;
    }
}
