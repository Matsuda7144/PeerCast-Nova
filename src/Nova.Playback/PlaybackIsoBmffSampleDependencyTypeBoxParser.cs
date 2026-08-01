using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleDependsOn
{
    Unknown = 0,
    DependsOnOthers = 1,
    Independent = 2,
}

public enum PlaybackIsoBmffSampleIsDependedOn
{
    Unknown = 0,
    NotDependedOn = 1,
    DependedOn = 2,
}

public enum PlaybackIsoBmffSampleHasRedundancy
{
    Unknown = 0,
    HasRedundancy = 1,
    NoRedundancy = 2,
}

public readonly record struct PlaybackIsoBmffSampleDependencyTypeEntry(
    PlaybackIsoBmffSampleDependsOn DependsOn,
    PlaybackIsoBmffSampleIsDependedOn IsDependedOn,
    PlaybackIsoBmffSampleHasRedundancy HasRedundancy);

public enum PlaybackIsoBmffSampleDependencyTypeBoxParseResult
{
    Parsed,
    InvalidConfiguration,
    NeedMoreData,
    BoxLengthMismatch,
    WrongBoxType,
    UnsupportedVersion,
    NonzeroFlags,
    EntryLimitExceeded,
    NonzeroReservedBits,
    ReservedDependencyValue,
    InvalidPayload,
}

public sealed record PlaybackIsoBmffSampleDependencyTypeBoxParserOptions
{
    public const int DefaultMaximumEntryCount = 4_096;
    public const int MaximumEntryCountHardLimit = 65_536;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
}

public sealed class PlaybackIsoBmffSampleDependencyTypeBox
{
    internal PlaybackIsoBmffSampleDependencyTypeBox(
        ReadOnlyCollection<PlaybackIsoBmffSampleDependencyTypeEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<PlaybackIsoBmffSampleDependencyTypeEntry> Entries
    {
        get;
    }
}

public static class PlaybackIsoBmffSampleDependencyTypeBoxParser
{
    private const uint SampleDependencyTypeBoxType = 0x73647470;
    private const int FixedPayloadSize = 4;

    public static PlaybackIsoBmffSampleDependencyTypeBoxParseResult Parse(
        ReadOnlySpan<byte> box,
        out PlaybackIsoBmffSampleDependencyTypeBox? sampleDependencyType,
        PlaybackIsoBmffSampleDependencyTypeBoxParserOptions? options = null)
    {
        sampleDependencyType = null;
        options ??= new PlaybackIsoBmffSampleDependencyTypeBoxParserOptions();
        if (options.MaximumEntryCount is < 0 or
            > PlaybackIsoBmffSampleDependencyTypeBoxParserOptions
                .MaximumEntryCountHardLimit)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                .InvalidConfiguration;
        }

        var headerResult = PlaybackIsoBmffBoxHeaderParser.Parse(box, out var header);
        if (headerResult == PlaybackIsoBmffBoxHeaderParseResult.NeedMoreData)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult.NeedMoreData;
        }

        if (headerResult != PlaybackIsoBmffBoxHeaderParseResult.Parsed ||
            header is null)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult.InvalidPayload;
        }

        if (header.Type != SampleDependencyTypeBoxType)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult.WrongBoxType;
        }

        if (header.BoxSize is { } declaredSize)
        {
            if (declaredSize > (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                    .NeedMoreData;
            }

            if (declaredSize < (ulong)box.Length)
            {
                return PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                    .BoxLengthMismatch;
            }
        }

        var payload = box[header.HeaderSize..];
        if (payload.Length < FixedPayloadSize ||
            PlaybackIsoBmffFullBoxHeaderParser.Parse(payload, out var fullBox) !=
                PlaybackIsoBmffFullBoxHeaderParseResult.Parsed ||
            fullBox is null)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult.InvalidPayload;
        }

        if (fullBox.Version != 0)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                .UnsupportedVersion;
        }

        if (fullBox.Flags != 0)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult.NonzeroFlags;
        }

        var entryCount = payload.Length - FixedPayloadSize;
        if (entryCount > options.MaximumEntryCount)
        {
            return PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                .EntryLimitExceeded;
        }

        var entries = new PlaybackIsoBmffSampleDependencyTypeEntry[entryCount];
        for (var index = 0; index < entries.Length; index++)
        {
            var value = payload[FixedPayloadSize + index];
            if ((value & 0xc0) != 0)
            {
                return PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                    .NonzeroReservedBits;
            }

            var dependsOn = (value >> 4) & 0x03;
            var isDependedOn = (value >> 2) & 0x03;
            var hasRedundancy = value & 0x03;
            if (dependsOn == 3 || isDependedOn == 3 || hasRedundancy == 3)
            {
                return PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                    .ReservedDependencyValue;
            }

            entries[index] = new(
                (PlaybackIsoBmffSampleDependsOn)dependsOn,
                (PlaybackIsoBmffSampleIsDependedOn)isDependedOn,
                (PlaybackIsoBmffSampleHasRedundancy)hasRedundancy);
        }

        sampleDependencyType = new(Array.AsReadOnly(entries));
        return PlaybackIsoBmffSampleDependencyTypeBoxParseResult.Parsed;
    }
}
