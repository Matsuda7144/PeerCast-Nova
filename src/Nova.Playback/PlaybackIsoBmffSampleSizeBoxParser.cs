using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleSizeBoxParseResult
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

public sealed record PlaybackIsoBmffSampleSizeBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffSampleSizeBox
{
    internal PlaybackIsoBmffSampleSizeBox(
        uint defaultSampleSize,
        uint sampleCount,
        ReadOnlyCollection<uint> sampleSizes,
        ulong totalSampleBytes)
    {
        DefaultSampleSize = defaultSampleSize;
        SampleCount = sampleCount;
        SampleSizes = sampleSizes;
        TotalSampleBytes = totalSampleBytes;
    }

    public uint DefaultSampleSize { get; }

    public uint SampleCount { get; }

    public IReadOnlyList<uint> SampleSizes { get; }

    public ulong TotalSampleBytes { get; }
}

public static class PlaybackIsoBmffSampleSizeBoxParser
{
    private const uint SampleSizeBoxType = 0x7374737A;
    private const int FixedPayloadSize = 12;
    private const int EntrySize = 4;

    public static PlaybackIsoBmffSampleSizeBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffSampleSizeBoxParserOptions? options = null)
    {
        sampleSize = null;
        options ??= new PlaybackIsoBmffSampleSizeBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffSampleSizeBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.InvalidPayload;
        }

        if (header.Type != SampleSizeBoxType)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleSizeBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleSizeBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.NonzeroFlags;
        }

        var defaultSampleSize =
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        var sampleCount =
            BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4));
        if (defaultSampleSize != 0)
        {
            if (payload.Length != FixedPayloadSize)
            {
                return PlaybackIsoBmffSampleSizeBoxParseResult.InvalidPayload;
            }

            sampleSize = new(
                defaultSampleSize,
                sampleCount,
                Array.AsReadOnly(Array.Empty<uint>()),
                (ulong)defaultSampleSize * sampleCount);
            return PlaybackIsoBmffSampleSizeBoxParseResult.Parsed;
        }

        if (sampleCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)sampleCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffSampleSizeBoxParseResult.InvalidPayload;
        }

        var sampleSizes = new uint[sampleCount];
        ulong totalSampleBytes = 0;
        for (var index = 0; index < sampleSizes.Length; index++)
        {
            var entry = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(FixedPayloadSize + (index * EntrySize), EntrySize));
            sampleSizes[index] = entry;
            totalSampleBytes += entry;
        }

        sampleSize = new(
            defaultSampleSize,
            sampleCount,
            Array.AsReadOnly(sampleSizes),
            totalSampleBytes);
        return PlaybackIsoBmffSampleSizeBoxParseResult.Parsed;
    }
}
