using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSyncSampleBoxParseResult
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
    InvalidSampleNumber,
    NonIncreasingSampleNumber,
}

public sealed record PlaybackIsoBmffSyncSampleBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffSyncSampleBox
{
    internal PlaybackIsoBmffSyncSampleBox(
        ReadOnlyCollection<uint> sampleNumbers)
    {
        SampleNumbers = sampleNumbers;
    }

    public IReadOnlyList<uint> SampleNumbers { get; }
}

public static class PlaybackIsoBmffSyncSampleBoxParser
{
    private const uint SyncSampleBoxType = 0x73747373;
    private const int FixedPayloadSize = 8;
    private const int EntrySize = 4;

    public static PlaybackIsoBmffSyncSampleBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffSyncSampleBox? syncSample,
        PlaybackIsoBmffSyncSampleBoxParserOptions? options = null)
    {
        syncSample = null;
        options ??= new PlaybackIsoBmffSyncSampleBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffSyncSampleBoxParserOptions.MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.InvalidPayload;
        }

        if (header.Type != SyncSampleBoxType)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffSyncSampleBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffSyncSampleBoxParseResult.BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffSyncSampleBoxParseResult.InvalidPayload;
        }

        var sampleNumbers = new uint[entryCount];
        uint previousSampleNumber = 0;
        for (var index = 0; index < sampleNumbers.Length; index++)
        {
            var sampleNumber = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(FixedPayloadSize + (index * EntrySize), EntrySize));
            if (sampleNumber == 0)
            {
                return PlaybackIsoBmffSyncSampleBoxParseResult.InvalidSampleNumber;
            }

            if (sampleNumber <= previousSampleNumber)
            {
                return PlaybackIsoBmffSyncSampleBoxParseResult
                    .NonIncreasingSampleNumber;
            }

            sampleNumbers[index] = sampleNumber;
            previousSampleNumber = sampleNumber;
        }

        syncSample = new(Array.AsReadOnly(sampleNumbers));
        return PlaybackIsoBmffSyncSampleBoxParseResult.Parsed;
    }
}
