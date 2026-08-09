using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackCodecConfigurationProjectionResult
{
    Projected,
    MissingSampleEntryProjection,
    EntryLimitExceeded,
    SourceMismatch,
    InvalidConfiguration,
    UnsupportedCodec,
    MissingCodecConfiguration,
    DuplicateCodecConfiguration,
    ConfigurationLocationMismatch,
    ConfigurationLimitExceeded,
    ParameterSetLimitExceeded,
    DescriptorLimitExceeded,
    InvalidConfigurationPayload,
}

public sealed record PlaybackIsoBmffTrackCodecConfigurationProjectionOptions
{
    public int MaximumParameterSetCount { get; init; } =
        PlaybackIsoBmffAvcDecoderConfigurationParserOptions
            .DefaultMaximumParameterSetCount;

    public int MaximumDescriptorCount { get; init; } =
        PlaybackIsoBmffElementaryStreamDescriptorParserOptions
            .DefaultMaximumDescriptorCount;

    public int MaximumConfigurationBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumCodecExtradataBytes;
}

public sealed class PlaybackIsoBmffTrackCodecConfiguration
{
    internal PlaybackIsoBmffTrackCodecConfiguration(
        PlaybackIsoBmffTrackSampleEntryParse source,
        PlaybackIsoBmffAvcDecoderConfiguration? avcConfiguration,
        PlaybackIsoBmffElementaryStreamDescriptor? elementaryStreamDescriptor)
    {
        Source = source;
        AvcConfiguration = avcConfiguration;
        ElementaryStreamDescriptor = elementaryStreamDescriptor;
    }

    public PlaybackIsoBmffTrackSampleEntryParse Source { get; }

    public PlaybackIsoBmffAvcDecoderConfiguration? AvcConfiguration { get; }

    public PlaybackIsoBmffElementaryStreamDescriptor?
        ElementaryStreamDescriptor
    { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate =>
        Source.CodecCandidate;
}

public sealed class PlaybackIsoBmffTrackCodecConfigurationProjection
{
    internal PlaybackIsoBmffTrackCodecConfigurationProjection(
        PlaybackIsoBmffTrackSampleEntryParseProjection source,
        int sampleDescriptionBoxLength,
        PlaybackIsoBmffTrackCodecConfigurationProjectionOptions options,
        ReadOnlyCollection<PlaybackIsoBmffTrackCodecConfiguration> entries)
    {
        Source = source;
        SampleDescriptionBoxLength = sampleDescriptionBoxLength;
        Options = options;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackSampleEntryParseProjection Source { get; }

    public int SampleDescriptionBoxLength { get; }

    public PlaybackIsoBmffTrackCodecConfigurationProjectionOptions Options
    { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackCodecConfiguration> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackCodecConfigurationProjector
{
    public static PlaybackIsoBmffTrackCodecConfigurationProjectionResult
        Project(
            ReadOnlySpan<byte> sampleDescriptionBox,
            PlaybackIsoBmffTrackSampleEntryParseProjection? source,
            out PlaybackIsoBmffTrackCodecConfigurationProjection? projection,
            PlaybackIsoBmffTrackCodecConfigurationProjectionOptions? options =
                null)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                .MissingSampleEntryProjection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                .EntryLimitExceeded;
        }

        options ??= new();
        if (options.MaximumParameterSetCount is < 1 or
                > PlaybackIsoBmffAvcDecoderConfigurationParserOptions
                    .MaximumParameterSetCountHardLimit ||
            options.MaximumDescriptorCount is < 2 or
                > PlaybackIsoBmffElementaryStreamDescriptorParserOptions
                    .MaximumDescriptorCountHardLimit ||
            options.MaximumConfigurationBytes is < 1 or
                > PlaybackBoundedDemuxOptions
                    .MaximumCodecExtradataBytesHardLimit)
        {
            return PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                .InvalidConfiguration;
        }

        var entries = new PlaybackIsoBmffTrackCodecConfiguration[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]))
            {
                return PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .SourceMismatch;
            }

            PlaybackIsoBmffAvcDecoderConfiguration? avcConfiguration = null;
            PlaybackIsoBmffElementaryStreamDescriptor? elementaryStream = null;
            PlaybackIsoBmffTrackCodecConfigurationProjectionResult parseResult;
            if (entry.CodecCandidate ==
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a)
            {
                if (entry.AudioEntry is null || entry.VisualEntry is not null ||
                    !ReferenceEquals(
                        entry.AudioEntry.Selection,
                        entry.Source.Selection))
                {
                    return PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                        .SourceMismatch;
                }

                var result =
                    PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
                        sampleDescriptionBox,
                        entry.AudioEntry,
                        out elementaryStream,
                        new()
                        {
                            MaximumDescriptorCount =
                                options.MaximumDescriptorCount,
                            MaximumConfigurationBytes =
                                options.MaximumConfigurationBytes,
                        });
                parseResult = MapAudioResult(result);
            }
            else if (entry.CodecCandidate is
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 or
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3)
            {
                if (entry.VisualEntry is null || entry.AudioEntry is not null ||
                    !ReferenceEquals(
                        entry.VisualEntry.Selection,
                        entry.Source.Selection))
                {
                    return PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                        .SourceMismatch;
                }

                var result =
                    PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
                        sampleDescriptionBox,
                        entry.VisualEntry,
                        out avcConfiguration,
                        new()
                        {
                            MaximumParameterSetCount =
                                options.MaximumParameterSetCount,
                            MaximumConfigurationBytes =
                                options.MaximumConfigurationBytes,
                        });
                parseResult = MapVisualResult(result);
            }
            else
            {
                return PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .UnsupportedCodec;
            }

            if (parseResult !=
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult.Projected)
            {
                return parseResult;
            }

            entries[index] = new(
                entry,
                avcConfiguration,
                elementaryStream);
        }

        projection = new(
            source,
            sampleDescriptionBox.Length,
            options,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackCodecConfigurationProjectionResult.Projected;
    }

    private static PlaybackIsoBmffTrackCodecConfigurationProjectionResult
        MapVisualResult(PlaybackIsoBmffAvcDecoderConfigurationParseResult result) =>
        result switch
        {
            PlaybackIsoBmffAvcDecoderConfigurationParseResult.Parsed =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult.Projected,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .InvalidConfiguration =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .InvalidConfiguration,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .MissingConfigurationBox =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .MissingCodecConfiguration,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .DuplicateConfigurationBox =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .DuplicateCodecConfiguration,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLocationMismatch =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .ConfigurationLocationMismatch,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLimitExceeded =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .ConfigurationLimitExceeded,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ParameterSetLimitExceeded =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .ParameterSetLimitExceeded,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult.InvalidPayload =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .InvalidConfigurationPayload,
            _ => PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                .SourceMismatch,
        };

    private static PlaybackIsoBmffTrackCodecConfigurationProjectionResult
        MapAudioResult(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult result) =>
        result switch
        {
            PlaybackIsoBmffElementaryStreamDescriptorParseResult.Parsed =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult.Projected,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .InvalidConfiguration =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .InvalidConfiguration,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingElementaryStreamBox =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .MissingCodecConfiguration,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .DuplicateElementaryStreamBox =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .DuplicateCodecConfiguration,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .BoxLocationMismatch =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .ConfigurationLocationMismatch,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .ConfigurationLimitExceeded =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .ConfigurationLimitExceeded,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .DescriptorLimitExceeded =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .DescriptorLimitExceeded,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingAudioEntry or
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .UnsupportedAudioEntry =>
                PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                    .SourceMismatch,
            _ => PlaybackIsoBmffTrackCodecConfigurationProjectionResult
                .InvalidConfigurationPayload,
        };
}
