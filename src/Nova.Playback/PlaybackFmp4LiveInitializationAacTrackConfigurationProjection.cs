namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
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
    DescriptorLimitExceeded,
    InvalidConfigurationPayload,
}

public sealed class PlaybackFmp4LiveInitializationAacTrackConfiguration
{
    internal PlaybackFmp4LiveInitializationAacTrackConfiguration(
        PlaybackFmp4LiveInitializationTrackSampleDescription source,
        PlaybackIsoBmffAudioSampleEntry audioEntry,
        PlaybackIsoBmffElementaryStreamDescriptor elementaryStreamDescriptor)
    {
        Source = source;
        AudioEntry = audioEntry;
        ElementaryStreamDescriptor = elementaryStreamDescriptor;
    }

    public PlaybackFmp4LiveInitializationTrackSampleDescription Source { get; }

    public PlaybackIsoBmffAudioSampleEntry AudioEntry { get; }

    public PlaybackIsoBmffElementaryStreamDescriptor ElementaryStreamDescriptor
    { get; }

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate =>
        Source.CodecCandidate;
}

public static class PlaybackFmp4LiveInitializationAacTrackConfigurationProjector
{
    public static
        PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult Project(
            ReadOnlySpan<byte> sampleDescriptionBox,
            PlaybackFmp4LiveInitializationTrackSampleDescription? source,
            out PlaybackFmp4LiveInitializationAacTrackConfiguration? projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                    .MissingSampleDescriptionProjection;
        }

        if (source.CodecCandidate !=
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a ||
            source.Kind != PlaybackIsoBmffHandlerKind.Audio)
        {
            return PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .UnsupportedCodec;
        }

        if (source.SampleDescription.Entries.Count != 1)
        {
            return PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .InvalidSampleDescriptionShape;
        }

        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            source.SampleDescription.Entries[0],
            source.CodecCandidate,
            source.Kind);
        if (PlaybackIsoBmffAudioSampleEntryParser.Parse(
                sampleDescriptionBox,
                selection,
                out var audioEntry) != PlaybackIsoBmffAudioSampleEntryParseResult.Parsed ||
            audioEntry is null)
        {
            return PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .InvalidSampleEntry;
        }

        var parseResult = PlaybackIsoBmffElementaryStreamDescriptorParser.Parse(
            sampleDescriptionBox,
            audioEntry,
            out var elementaryStreamDescriptor);
        if (parseResult != PlaybackIsoBmffElementaryStreamDescriptorParseResult.Parsed ||
            elementaryStreamDescriptor is null)
        {
            return MapConfigurationResult(parseResult);
        }

        projection = new(source, audioEntry, elementaryStreamDescriptor);
        return PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
            .Projected;
    }

    private static
        PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
        MapConfigurationResult(
            PlaybackIsoBmffElementaryStreamDescriptorParseResult result) =>
        result switch
        {
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .MissingElementaryStreamBox =>
                PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                    .MissingConfigurationBox,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .DuplicateElementaryStreamBox =>
                PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                    .DuplicateConfigurationBox,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .BoxLocationMismatch =>
                PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                    .ConfigurationLocationMismatch,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .ConfigurationLimitExceeded =>
                PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                    .ConfigurationLimitExceeded,
            PlaybackIsoBmffElementaryStreamDescriptorParseResult
                .DescriptorLimitExceeded =>
                PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                    .DescriptorLimitExceeded,
            _ => PlaybackFmp4LiveInitializationAacTrackConfigurationProjectionResult
                .InvalidConfigurationPayload,
        };
}
