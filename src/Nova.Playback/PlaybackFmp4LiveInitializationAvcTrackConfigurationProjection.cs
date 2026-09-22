namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
{
    Projected,
    MissingSampleDescriptionProjection,
    UnsupportedCodec,
    InvalidSampleDescriptionShape,
    InvalidSampleEntry,
    MissingConfigurationBox,
    DuplicateConfigurationBox,
    ConfigurationLocationMismatch,
    ConfigurationLimitExceeded,
    ParameterSetLimitExceeded,
    InvalidConfigurationPayload,
}

public sealed class PlaybackFmp4LiveInitializationAvcTrackConfiguration
{
    internal PlaybackFmp4LiveInitializationAvcTrackConfiguration(
        PlaybackFmp4LiveInitializationTrackSampleDescription source,
        PlaybackIsoBmffVisualSampleEntry visualEntry,
        PlaybackIsoBmffAvcDecoderConfiguration configuration)
    {
        Source = source;
        VisualEntry = visualEntry;
        Configuration = configuration;
    }

    public PlaybackFmp4LiveInitializationTrackSampleDescription Source { get; }

    public PlaybackIsoBmffVisualSampleEntry VisualEntry { get; }

    public PlaybackIsoBmffAvcDecoderConfiguration Configuration { get; }

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate =>
        Source.CodecCandidate;
}

public static class PlaybackFmp4LiveInitializationAvcTrackConfigurationProjector
{
    public static
        PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult Project(
            ReadOnlySpan<byte> sampleDescriptionBox,
            PlaybackFmp4LiveInitializationTrackSampleDescription? source,
            out PlaybackFmp4LiveInitializationAvcTrackConfiguration? projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                    .MissingSampleDescriptionProjection;
        }

        if (source.CodecCandidate is not
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 and not
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3 ||
            source.Kind != PlaybackIsoBmffHandlerKind.Video)
        {
            return PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .UnsupportedCodec;
        }

        if (source.SampleDescription.Entries.Count != 1)
        {
            return PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .InvalidSampleDescriptionShape;
        }

        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            source.SampleDescription.Entries[0],
            source.CodecCandidate,
            source.Kind);
        if (PlaybackIsoBmffVisualSampleEntryParser.Parse(
                sampleDescriptionBox,
                selection,
                out var visualEntry) != PlaybackIsoBmffVisualSampleEntryParseResult.Parsed ||
            visualEntry is null)
        {
            return PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .InvalidSampleEntry;
        }

        var parseResult = PlaybackIsoBmffAvcDecoderConfigurationParser.Parse(
            sampleDescriptionBox,
            visualEntry,
            out var configuration);
        if (parseResult != PlaybackIsoBmffAvcDecoderConfigurationParseResult.Parsed ||
            configuration is null)
        {
            return MapConfigurationResult(parseResult);
        }

        projection = new(source, visualEntry, configuration);
        return PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
            .Projected;
    }

    private static
        PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
        MapConfigurationResult(
            PlaybackIsoBmffAvcDecoderConfigurationParseResult result) =>
        result switch
        {
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .MissingConfigurationBox =>
                PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                    .MissingConfigurationBox,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .DuplicateConfigurationBox =>
                PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                    .DuplicateConfigurationBox,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLocationMismatch =>
                PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                    .ConfigurationLocationMismatch,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ConfigurationLimitExceeded =>
                PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                    .ConfigurationLimitExceeded,
            PlaybackIsoBmffAvcDecoderConfigurationParseResult
                .ParameterSetLimitExceeded =>
                PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                    .ParameterSetLimitExceeded,
            _ => PlaybackFmp4LiveInitializationAvcTrackConfigurationProjectionResult
                .InvalidConfigurationPayload,
        };
}
