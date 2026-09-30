namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
{
    Prepared,
    MissingMovieComposition,
    InvalidConfiguration,
    SourceMismatch,
    MissingSelectedTrack,
    VideoPreparationRejected,
    AudioDecoderConfigRejected,
    AudioSpecificConfigRejected,
    AudioGaPrefixRejected,
    AudioGaConfigRejected,
    AudioCoreFactsRejected,
    AudioExtensionFactsRejected,
    AudioFormatIntentRejected,
    AudioPreparationRejected,
}

public sealed record PlaybackIsoBmffRegularMovieManagedDecoderPreparationOptions
{
    public IReadOnlyCollection<string>? DeclaredAvcSymbols { get; init; }

    public PlaybackIsoBmffAvcDecoderCapabilityDeclaration
        AvcCapabilityDeclaration
    { get; init; }

    public IReadOnlyCollection<string>? DeclaredAacSymbols { get; init; }

    public PlaybackIsoBmffAacDecoderCapabilityDeclaration
        AacCapabilityDeclaration
    { get; init; }

    public PlaybackIsoBmffAacSampleEntryConsistencyPolicy AacConsistencyPolicy
    { get; init; } =
        PlaybackIsoBmffAacSampleEntryConsistencyPolicy.RequireExactAgreement;

    public PlaybackIsoBmffDecoderConfigDescriptorParserOptions AacDecoderConfig
    { get; init; } = new();

    public PlaybackIsoBmffAudioSpecificConfigHeaderParserOptions
        AacAudioSpecificConfig
    { get; init; } = new();

    public PlaybackIsoBmffAacDecoderExtradataMaterializerOptions AacExtradata
    { get; init; } = new();
}

public sealed class PlaybackIsoBmffRegularAvcDecoderPreparationInput
{
    private readonly byte[] _configurationRecord;

    internal PlaybackIsoBmffRegularAvcDecoderPreparationInput(
        PlaybackIsoBmffRegularTrackDemuxComposition source,
        PlaybackIsoBmffTrackCodecConfiguration codecConfiguration,
        PlaybackIsoBmffAvcPayloadRequirements requirements)
    {
        Source = source;
        CodecConfiguration = codecConfiguration;
        Requirements = requirements;
        _configurationRecord = codecConfiguration.AvcConfiguration!
            .ConfigurationRecord.ToArray();
    }

    public PlaybackIsoBmffRegularTrackDemuxComposition Source { get; }

    public PlaybackIsoBmffTrackCodecConfiguration CodecConfiguration { get; }

    public PlaybackIsoBmffAvcDecoderConfiguration AvcConfiguration =>
        CodecConfiguration.AvcConfiguration!;

    public PlaybackIsoBmffAvcPayloadRequirements Requirements { get; }

    public ReadOnlyMemory<byte> ConfigurationRecord =>
        _configurationRecord.ToArray();

    internal ReadOnlySpan<byte> ConfigurationRecordSpan =>
        _configurationRecord;
}

public sealed class PlaybackIsoBmffRegularMovieManagedDecoderPreparation :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffRegularMovieManagedDecoderPreparation(
        PlaybackIsoBmffRegularMovieDemuxComposition source,
        PlaybackIsoBmffRegularMovieManagedDecoderPreparationOptions options,
        PlaybackIsoBmffAvcManagedDecoderPreparation? video,
        PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation? audio)
    {
        Source = source;
        Options = options;
        Video = video;
        Audio = audio;
    }

    public PlaybackIsoBmffRegularMovieDemuxComposition Source { get; }

    public PlaybackIsoBmffRegularMovieManagedDecoderPreparationOptions Options
    { get; }

    public PlaybackIsoBmffAvcManagedDecoderPreparation? Video { get; }

    public PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation? Audio
    { get; }

    public void Dispose()
    {
        Video?.Dispose();
        Audio?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Video is not null)
        {
            await Video.DisposeAsync().ConfigureAwait(false);
        }

        if (Audio is not null)
        {
            await Audio.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public static class PlaybackIsoBmffRegularMovieManagedDecoderPreparationComposer
{
    public static PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
        Compose(
            PlaybackIsoBmffRegularMovieDemuxComposition? source,
            out PlaybackIsoBmffRegularMovieManagedDecoderPreparation?
                preparation,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationOptions?
                options = null)
    {
        preparation = null;
        if (source is null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .MissingMovieComposition;
        }

        options ??= new();
        if (options.AacDecoderConfig is null ||
            options.AacAudioSpecificConfig is null ||
            options.AacExtradata is null ||
            !Enum.IsDefined(options.AacConsistencyPolicy))
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .InvalidConfiguration;
        }

        if (!ReferenceEquals(source.Selection.Source, source.SequenceSet) ||
            !ReferenceEquals(source.CursorSet.Source, source.Selection) ||
            !ReferenceEquals(source.MergeTraversal.Source, source.CursorSet) ||
            source.Tracks.Count != source.SequenceSet.Sources.Count)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .SourceMismatch;
        }

        PlaybackIsoBmffAvcManagedDecoderPreparation? video = null;
        if (source.Selection.VideoSource is { } videoSource)
        {
            var videoTrack = FindTrack(source, videoSource);
            if (videoTrack is null)
            {
                return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                    .MissingSelectedTrack;
            }

            if (!TryCreateVideoInput(videoTrack, out var videoInput) ||
                PlaybackIsoBmffAvcManagedDecoderPreparationComposer.Compose(
                    videoInput,
                    options.DeclaredAvcSymbols,
                    options.AvcCapabilityDeclaration,
                    out video) !=
                PlaybackIsoBmffAvcManagedDecoderPreparationResult.Prepared ||
                video is null)
            {
                video?.Dispose();
                return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                    .VideoPreparationRejected;
            }
        }

        PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation? audio = null;
        if (source.Selection.AudioSource is { } audioSource)
        {
            var audioTrack = FindTrack(source, audioSource);
            if (audioTrack is null)
            {
                video?.Dispose();
                return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                    .MissingSelectedTrack;
            }

            var result = TryPrepareAudio(audioTrack, options, out audio);
            if (result !=
                PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                    .Prepared)
            {
                audio?.Dispose();
                video?.Dispose();
                return result;
            }
        }

        preparation = new(source, options, video, audio);
        return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
            .Prepared;
    }

    private static PlaybackIsoBmffRegularTrackDemuxComposition? FindTrack(
        PlaybackIsoBmffRegularMovieDemuxComposition source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection selectedSource)
    {
        PlaybackIsoBmffRegularTrackDemuxComposition? match = null;
        foreach (var track in source.Tracks)
        {
            if (!ReferenceEquals(track.SequenceProjection, selectedSource))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = track;
        }

        return match;
    }

    private static bool TryCreateVideoInput(
        PlaybackIsoBmffRegularTrackDemuxComposition source,
        out PlaybackIsoBmffRegularAvcDecoderPreparationInput? input)
    {
        input = null;
        var entries = source.PacketComposition.Source.Entries;
        var requirements = source.PacketComposition.AvcRequirements;
        if (entries.Count == 0 || requirements is null)
        {
            return false;
        }

        var first = entries[0];
        if (first.AvcConfiguration is null ||
            first.ElementaryStreamDescriptor is not null ||
            first.CodecCandidate is not (
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 or
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3))
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (entry.AvcConfiguration is null ||
                entry.ElementaryStreamDescriptor is not null ||
                entry.CodecCandidate != first.CodecCandidate ||
                !entry.AvcConfiguration.ConfigurationRecord.Span.SequenceEqual(
                    first.AvcConfiguration.ConfigurationRecord.Span))
            {
                return false;
            }
        }

        input = new(source, first, requirements);
        return true;
    }

    private static PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
        TryPrepareAudio(
            PlaybackIsoBmffRegularTrackDemuxComposition source,
            PlaybackIsoBmffRegularMovieManagedDecoderPreparationOptions options,
            out PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation?
                preparation)
    {
        preparation = null;
        var entries = source.PacketComposition.Source.Entries;
        if (entries.Count == 0 ||
            source.PacketComposition.AvcRequirements is not null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioDecoderConfigRejected;
        }

        var first = entries[0];
        var elementaryStream = first.ElementaryStreamDescriptor;
        if (elementaryStream is null || first.AvcConfiguration is not null ||
            first.CodecCandidate !=
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioDecoderConfigRejected;
        }

        foreach (var entry in entries)
        {
            if (entry.ElementaryStreamDescriptor is null ||
                entry.AvcConfiguration is not null ||
                entry.CodecCandidate != first.CodecCandidate ||
                !entry.ElementaryStreamDescriptor.DescriptorBytes.Span
                    .SequenceEqual(elementaryStream.DescriptorBytes.Span))
            {
                return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                    .AudioDecoderConfigRejected;
            }
        }

        if (PlaybackIsoBmffDecoderConfigDescriptorParser.Parse(
                elementaryStream,
                out var decoderConfig,
                options.AacDecoderConfig) !=
            PlaybackIsoBmffDecoderConfigDescriptorParseResult.Parsed ||
            decoderConfig is null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioDecoderConfigRejected;
        }

        if (PlaybackIsoBmffAudioSpecificConfigHeaderParser.Parse(
                decoderConfig,
                out var audioSpecificConfig,
                options.AacAudioSpecificConfig) !=
            PlaybackIsoBmffAudioSpecificConfigHeaderParseResult.Parsed ||
            audioSpecificConfig is null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioSpecificConfigRejected;
        }

        PlaybackIsoBmffGaSpecificConfigPrefix? gaPrefix;
        var explicitResult = PlaybackIsoBmffExplicitSbrPsExtensionParser.Parse(
            audioSpecificConfig,
            out var explicitExtension);
        if (explicitResult ==
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult.Parsed)
        {
            if (PlaybackIsoBmffGaSpecificConfigPrefixParser
                    .ParseExplicitExtension(explicitExtension, out gaPrefix) !=
                PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed)
            {
                return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                    .AudioGaPrefixRejected;
            }
        }
        else if (explicitResult ==
            PlaybackIsoBmffExplicitSbrPsExtensionParseResult
                .UnsupportedAudioObjectType)
        {
            if (PlaybackIsoBmffGaSpecificConfigPrefixParser.Parse(
                    audioSpecificConfig,
                    out gaPrefix) !=
                PlaybackIsoBmffGaSpecificConfigPrefixParseResult.Parsed)
            {
                return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                    .AudioGaPrefixRejected;
            }
        }
        else
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioGaPrefixRejected;
        }

        if (PlaybackIsoBmffGaSpecificConfigComposer.Compose(
                gaPrefix,
                null,
                out var gaConfig) !=
            PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed ||
            gaConfig is null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioGaConfigRejected;
        }

        if (PlaybackIsoBmffAacCoreFormatFactsResolver.Resolve(
                gaConfig,
                null,
                out var coreFacts) !=
            PlaybackIsoBmffAacCoreFormatFactsResolutionResult.Resolved ||
            coreFacts is null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioCoreFactsRejected;
        }

        if (PlaybackIsoBmffAacExtensionSignallingFactsResolver.Resolve(
                coreFacts,
                null,
                null,
                out var extensionFacts) !=
            PlaybackIsoBmffAacExtensionSignallingFactsResolutionResult.Resolved ||
            extensionFacts is null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioExtensionFactsRejected;
        }

        if (PlaybackIsoBmffAacDecoderFormatIntentResolver.Resolve(
                extensionFacts,
                out var formatIntent) !=
            PlaybackIsoBmffAacDecoderFormatIntentResolutionResult.Resolved ||
            formatIntent is null)
        {
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioFormatIntentRejected;
        }

        if (PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationComposer
                .Compose(
                    first.Source.AudioEntry,
                    formatIntent,
                    options.AacConsistencyPolicy,
                    options.DeclaredAacSymbols,
                    options.AacCapabilityDeclaration,
                    out preparation,
                    options.AacExtradata) !=
            PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
                .Prepared ||
            preparation is null)
        {
            preparation?.Dispose();
            preparation = null;
            return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
                .AudioPreparationRejected;
        }

        return PlaybackIsoBmffRegularMovieManagedDecoderPreparationResult
            .Prepared;
    }
}
