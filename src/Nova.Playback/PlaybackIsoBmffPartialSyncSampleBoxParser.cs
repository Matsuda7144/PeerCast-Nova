using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffPartialSyncSampleBoxParseResult
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

public sealed record PlaybackIsoBmffPartialSyncSampleBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffPartialSyncSampleBox
{
    internal PlaybackIsoBmffPartialSyncSampleBox(
        ReadOnlyCollection<uint> sampleNumbers)
    {
        SampleNumbers = sampleNumbers;
    }

    public IReadOnlyList<uint> SampleNumbers { get; }
}

public static class PlaybackIsoBmffPartialSyncSampleBoxParser
{
    private const uint PartialSyncSampleBoxType = 0x73747073;
    private const int FixedPayloadSize = 8;
    private const int EntrySize = 4;

    public static PlaybackIsoBmffPartialSyncSampleBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffPartialSyncSampleBox? partialSyncSample,
        PlaybackIsoBmffPartialSyncSampleBoxParserOptions? options = null)
    {
        partialSyncSample = null;
        options ??= new PlaybackIsoBmffPartialSyncSampleBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffPartialSyncSampleBoxParserOptions
                .MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult
                .InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult.InvalidPayload;
        }

        if (header.Type != PartialSyncSampleBoxType)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffPartialSyncSampleBoxParseResult.NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffPartialSyncSampleBoxParseResult
                    .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult
                .UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult.NonzeroFlags;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult
                .EntryLimitExceeded;
        }

        var expectedPayloadLength =
            FixedPayloadSize + (checked((int)entryCount) * EntrySize);
        if (payload.Length != expectedPayloadLength)
        {
            return PlaybackIsoBmffPartialSyncSampleBoxParseResult.InvalidPayload;
        }

        var sampleNumbers = new uint[entryCount];
        uint previousSampleNumber = 0;
        for (var index = 0; index < sampleNumbers.Length; index++)
        {
            var sampleNumber = BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(FixedPayloadSize + (index * EntrySize), EntrySize));
            if (sampleNumber == 0)
            {
                return PlaybackIsoBmffPartialSyncSampleBoxParseResult
                    .InvalidSampleNumber;
            }

            if (sampleNumber <= previousSampleNumber)
            {
                return PlaybackIsoBmffPartialSyncSampleBoxParseResult
                    .NonIncreasingSampleNumber;
            }

            sampleNumbers[index] = sampleNumber;
            previousSampleNumber = sampleNumber;
        }

        partialSyncSample = new(Array.AsReadOnly(sampleNumbers));
        return PlaybackIsoBmffPartialSyncSampleBoxParseResult.Parsed;
    }
}
