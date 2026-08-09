using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackSampleEntryParseProjectionResult
{
    Projected,
    MissingDescriptionSelectionProjection,
    EntryLimitExceeded,
    SourceMismatch,
    InvalidConfiguration,
    UnsupportedCodec,
    EntryLocationMismatch,
    InvalidFixedFields,
    ChildLimitExceeded,
    InvalidChildSequence,
}

public sealed record PlaybackIsoBmffTrackSampleEntryParseProjectionOptions
{
    public int MaximumChildBoxCount { get; init; } =
        PlaybackIsoBmffAudioSampleEntryParserOptions
            .DefaultMaximumChildBoxCount;
}

public sealed class PlaybackIsoBmffTrackSampleEntryParse
{
    internal PlaybackIsoBmffTrackSampleEntryParse(
        PlaybackIsoBmffTrackSampleDescriptionSelection source,
        PlaybackIsoBmffVisualSampleEntry? visualEntry,
        PlaybackIsoBmffAudioSampleEntry? audioEntry)
    {
        Source = source;
        VisualEntry = visualEntry;
        AudioEntry = audioEntry;
    }

    public PlaybackIsoBmffTrackSampleDescriptionSelection Source { get; }

    public PlaybackIsoBmffVisualSampleEntry? VisualEntry { get; }

    public PlaybackIsoBmffAudioSampleEntry? AudioEntry { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate =>
        Source.CodecCandidate;
}

public sealed class PlaybackIsoBmffTrackSampleEntryParseProjection
{
    internal PlaybackIsoBmffTrackSampleEntryParseProjection(
        PlaybackIsoBmffTrackSampleDescriptionSelectionProjection source,
        int sampleDescriptionBoxLength,
        int maximumChildBoxCount,
        ReadOnlyCollection<PlaybackIsoBmffTrackSampleEntryParse> entries)
    {
        Source = source;
        SampleDescriptionBoxLength = sampleDescriptionBoxLength;
        MaximumChildBoxCount = maximumChildBoxCount;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackSampleDescriptionSelectionProjection Source
    { get; }

    public int SampleDescriptionBoxLength { get; }

    public int MaximumChildBoxCount { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackSampleEntryParse> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackSampleEntryParseProjector
{
    public static PlaybackIsoBmffTrackSampleEntryParseProjectionResult Project(
        ReadOnlySpan<byte> sampleDescriptionBox,
        PlaybackIsoBmffTrackSampleDescriptionSelectionProjection? source,
        out PlaybackIsoBmffTrackSampleEntryParseProjection? projection,
        PlaybackIsoBmffTrackSampleEntryParseProjectionOptions? options = null)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                .MissingDescriptionSelectionProjection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                .EntryLimitExceeded;
        }

        options ??= new PlaybackIsoBmffTrackSampleEntryParseProjectionOptions();
        if (options.MaximumChildBoxCount is < 0 or
            > PlaybackIsoBmffAudioSampleEntryParserOptions
                .MaximumChildBoxCountHardLimit)
        {
            return PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                .InvalidConfiguration;
        }

        var entries = new PlaybackIsoBmffTrackSampleEntryParse[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]))
            {
                return PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .SourceMismatch;
            }

            PlaybackIsoBmffVisualSampleEntry? visualEntry = null;
            PlaybackIsoBmffAudioSampleEntry? audioEntry = null;
            PlaybackIsoBmffTrackSampleEntryParseProjectionResult parseResult;
            if (entry.CodecCandidate ==
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a)
            {
                var result = PlaybackIsoBmffAudioSampleEntryParser.Parse(
                    sampleDescriptionBox,
                    entry.Selection,
                    out audioEntry,
                    new()
                    {
                        MaximumChildBoxCount = options.MaximumChildBoxCount,
                    });
                parseResult = MapAudioResult(result);
            }
            else if (entry.CodecCandidate is
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 or
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3)
            {
                var result = PlaybackIsoBmffVisualSampleEntryParser.Parse(
                    sampleDescriptionBox,
                    entry.Selection,
                    out visualEntry,
                    new()
                    {
                        MaximumChildBoxCount = options.MaximumChildBoxCount,
                    });
                parseResult = MapVisualResult(result);
            }
            else
            {
                return PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .UnsupportedCodec;
            }

            if (parseResult !=
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult.Projected)
            {
                return parseResult;
            }

            entries[index] = new(entry, visualEntry, audioEntry);
        }

        projection = new(
            source,
            sampleDescriptionBox.Length,
            options.MaximumChildBoxCount,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackSampleEntryParseProjectionResult.Projected;
    }

    private static PlaybackIsoBmffTrackSampleEntryParseProjectionResult
        MapAudioResult(PlaybackIsoBmffAudioSampleEntryParseResult result) =>
        result switch
        {
            PlaybackIsoBmffAudioSampleEntryParseResult.Parsed =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult.Projected,
            PlaybackIsoBmffAudioSampleEntryParseResult.InvalidConfiguration =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .InvalidConfiguration,
            PlaybackIsoBmffAudioSampleEntryParseResult.EntryLocationMismatch =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .EntryLocationMismatch,
            PlaybackIsoBmffAudioSampleEntryParseResult.InvalidFixedFields =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .InvalidFixedFields,
            PlaybackIsoBmffAudioSampleEntryParseResult.ChildLimitExceeded =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .ChildLimitExceeded,
            PlaybackIsoBmffAudioSampleEntryParseResult.InvalidChildSequence =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .InvalidChildSequence,
            _ => PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                .UnsupportedCodec,
        };

    private static PlaybackIsoBmffTrackSampleEntryParseProjectionResult
        MapVisualResult(PlaybackIsoBmffVisualSampleEntryParseResult result) =>
        result switch
        {
            PlaybackIsoBmffVisualSampleEntryParseResult.Parsed =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult.Projected,
            PlaybackIsoBmffVisualSampleEntryParseResult.InvalidConfiguration =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .InvalidConfiguration,
            PlaybackIsoBmffVisualSampleEntryParseResult.EntryLocationMismatch =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .EntryLocationMismatch,
            PlaybackIsoBmffVisualSampleEntryParseResult.InvalidFixedFields =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .InvalidFixedFields,
            PlaybackIsoBmffVisualSampleEntryParseResult.ChildLimitExceeded =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .ChildLimitExceeded,
            PlaybackIsoBmffVisualSampleEntryParseResult.InvalidChildSequence =>
                PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                    .InvalidChildSequence,
            _ => PlaybackIsoBmffTrackSampleEntryParseProjectionResult
                .UnsupportedCodec,
        };
}
