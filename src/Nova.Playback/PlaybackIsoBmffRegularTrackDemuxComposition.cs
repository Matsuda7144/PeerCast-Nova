namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularTrackDemuxCompositionResult
{
    Composed,
    MissingSources,
    InvalidConfiguration,
    SamplePlanFailed,
    EditAdmissionFailed,
    AdmittedSampleSelectionFailed,
    NoAdmittedSamples,
    ByteRangeProjectionFailed,
    MediaDataProjectionFailed,
    OwnedPayloadProjectionFailed,
    PacketCandidateProjectionFailed,
    SampleDescriptionSelectionFailed,
    SampleEntryParseFailed,
    CodecConfigurationProjectionFailed,
    PacketCompositionFailed,
    TrackBindingFailed,
    MovieBindingFailed,
    PacketSequenceProjectionFailed,
}

public sealed record PlaybackIsoBmffRegularTrackDemuxCompositionSources(
    PlaybackIsoBmffReconciledSampleCount? SampleReconciliation,
    PlaybackIsoBmffTimeToSampleBox? TimeToSample,
    PlaybackIsoBmffCompositionTimeToSampleBox? CompositionTimeToSample,
    PlaybackIsoBmffEditListBox? EditList,
    PlaybackIsoBmffMovieHeaderTimingBox? MovieTiming,
    PlaybackIsoBmffMediaHeaderTimingBox? MediaTiming,
    PlaybackIsoBmffSampleToChunkBox? SampleToChunk,
    PlaybackIsoBmffReconciledChunkTable? ChunkReconciliation,
    PlaybackIsoBmffChunkOffsetBox? ChunkOffset,
    PlaybackIsoBmffChunkLargeOffsetBox? ChunkLargeOffset,
    PlaybackIsoBmffSampleSizeBox? SampleSize,
    PlaybackIsoBmffCompactSampleSizeBox? CompactSampleSize,
    PlaybackIsoBmffSampleDescriptionBox? SampleDescription,
    PlaybackIsoBmffHandlerReferenceBox? Handler,
    PlaybackIsoBmffTrackHeaderIdentityBox? TrackIdentity);

public sealed record PlaybackIsoBmffRegularTrackDemuxCompositionOptions
{
    public const int DefaultMaximumSampleCount = 4_096;

    public ulong FirstSampleNumber { get; init; } = 1;

    public int MaximumSampleCount { get; init; } = DefaultMaximumSampleCount;

    public PlaybackIsoBmffAvcPayloadRequirements? AvcRequirements { get; init; }

    public PlaybackIsoBmffSampleEditOverlapLookupOptions? EditOverlap
    { get; init; } = new();

    public PlaybackIsoBmffSampleMediaDataLookupOptions? MediaDataLookup
    { get; init; } = new();

    public PlaybackIsoBmffOwnedEditedSamplePayloadOptions? OwnedPayload
    { get; init; } = new();

    public PlaybackIsoBmffTrackSampleEntryParseProjectionOptions? SampleEntry
    { get; init; } = new();

    public PlaybackIsoBmffTrackCodecConfigurationProjectionOptions?
        CodecConfiguration
    { get; init; } = new();

    public PlaybackIsoBmffTrackCodecPacketCompositionProjectionOptions?
        PacketComposition
    { get; init; } = new();
}

public sealed class PlaybackIsoBmffRegularTrackDemuxComposition
{
    internal PlaybackIsoBmffRegularTrackDemuxComposition(
        PlaybackIsoBmffRegularTrackDemuxCompositionSources sources,
        PlaybackIsoBmffRegularTrackDemuxCompositionOptions options,
        PlaybackIsoBmffTrackSampleSequencePlan plan,
        PlaybackIsoBmffTrackSampleEditAdmissionProjection editAdmissions,
        PlaybackIsoBmffTrackAdmittedSampleSelection admittedSamples,
        PlaybackIsoBmffTrackOwnedSamplePayloadProjection ownedPayloads,
        PlaybackIsoBmffTrackCodecPacketCompositionProjection packetComposition,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection sequenceProjection)
    {
        Sources = sources;
        Options = options;
        Plan = plan;
        EditAdmissions = editAdmissions;
        AdmittedSamples = admittedSamples;
        OwnedPayloads = ownedPayloads;
        PacketComposition = packetComposition;
        SequenceProjection = sequenceProjection;
    }

    public PlaybackIsoBmffRegularTrackDemuxCompositionSources Sources
    { get; }

    public PlaybackIsoBmffRegularTrackDemuxCompositionOptions Options
    { get; }

    public PlaybackIsoBmffTrackSampleSequencePlan Plan { get; }

    public PlaybackIsoBmffTrackSampleEditAdmissionProjection EditAdmissions
    { get; }

    public PlaybackIsoBmffTrackAdmittedSampleSelection AdmittedSamples
    { get; }

    public PlaybackIsoBmffTrackOwnedSamplePayloadProjection OwnedPayloads
    { get; }

    public PlaybackIsoBmffTrackCodecPacketCompositionProjection
        PacketComposition
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection
        SequenceProjection
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequence Sequence =>
        SequenceProjection.Sequence;

    public bool IsSampleWindowTruncated => Plan.IsTruncated;

    public ulong TotalPayloadBytes => PacketComposition.TotalPayloadBytes;
}

public static class PlaybackIsoBmffRegularTrackDemuxComposer
{
    public static PlaybackIsoBmffRegularTrackDemuxCompositionResult Compose(
        ReadOnlySpan<byte> file,
        ReadOnlySpan<byte> sampleDescriptionBox,
        PlaybackIsoBmffRegularTrackDemuxCompositionSources? sources,
        out PlaybackIsoBmffRegularTrackDemuxComposition? composition,
        PlaybackIsoBmffRegularTrackDemuxCompositionOptions? options = null)
    {
        composition = null;
        if (sources is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .MissingSources;
        }

        options ??= new();
        if (options.FirstSampleNumber == 0 ||
            options.MaximumSampleCount is < 1 or
                > PlaybackIsoBmffTrackSampleSequencePlanFactory
                    .MaximumPlannedSampleCount ||
            options.EditOverlap is null ||
            options.MediaDataLookup is null ||
            options.OwnedPayload is null ||
            options.SampleEntry is null ||
            options.CodecConfiguration is null ||
            options.PacketComposition is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .InvalidConfiguration;
        }

        if (PlaybackIsoBmffTrackSampleSequencePlanFactory.TryCreate(
                sources.SampleReconciliation,
                options.FirstSampleNumber,
                options.MaximumSampleCount,
                out var plan) !=
            PlaybackIsoBmffTrackSampleSequencePlanCreationResult.Created ||
            plan is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .SamplePlanFailed;
        }

        if (PlaybackIsoBmffTrackSampleEditAdmissionProjector.Project(
                plan,
                sources.TimeToSample,
                sources.CompositionTimeToSample,
                sources.EditList,
                sources.MovieTiming,
                sources.MediaTiming,
                out var editAdmissions,
                options.EditOverlap) !=
            PlaybackIsoBmffTrackSampleEditAdmissionProjectionResult.Projected ||
            editAdmissions is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .EditAdmissionFailed;
        }

        if (PlaybackIsoBmffTrackAdmittedSampleSelector.Select(
                editAdmissions,
                options.MaximumSampleCount,
                out var admittedSamples) !=
            PlaybackIsoBmffTrackAdmittedSampleSelectionResult.Selected ||
            admittedSamples is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .AdmittedSampleSelectionFailed;
        }

        if (admittedSamples.Entries.Count == 0)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .NoAdmittedSamples;
        }

        if (PlaybackIsoBmffTrackAdmittedSampleByteRangeProjector.Project(
                admittedSamples,
                sources.SampleToChunk,
                sources.ChunkReconciliation,
                sources.ChunkOffset,
                sources.ChunkLargeOffset,
                sources.SampleSize,
                sources.CompactSampleSize,
                out var byteRanges) !=
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                .Projected ||
            byteRanges is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .ByteRangeProjectionFailed;
        }

        if (PlaybackIsoBmffTrackAdmittedSampleMediaDataProjector.Project(
                file,
                byteRanges,
                out var mediaData,
                options.MediaDataLookup) !=
            PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                .Projected ||
            mediaData is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .MediaDataProjectionFailed;
        }

        if (PlaybackIsoBmffTrackOwnedSamplePayloadProjector.Project(
                file,
                mediaData,
                out var ownedPayloads,
                options.OwnedPayload) !=
            PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult.Projected ||
            ownedPayloads is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .OwnedPayloadProjectionFailed;
        }

        if (PlaybackIsoBmffTrackEditedSamplePacketCandidateProjector.Project(
                ownedPayloads,
                sources.MediaTiming,
                out var packetCandidates) !=
            PlaybackIsoBmffTrackEditedSamplePacketCandidateProjectionResult
                .Projected ||
            packetCandidates is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .PacketCandidateProjectionFailed;
        }

        if (PlaybackIsoBmffTrackSampleDescriptionSelectionProjector.Project(
                packetCandidates,
                sources.SampleDescription,
                sources.Handler,
                out var descriptionSelection) !=
            PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                .Projected ||
            descriptionSelection is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .SampleDescriptionSelectionFailed;
        }

        if (PlaybackIsoBmffTrackSampleEntryParseProjector.Project(
                sampleDescriptionBox,
                descriptionSelection,
                out var sampleEntries,
                options.SampleEntry) !=
            PlaybackIsoBmffTrackSampleEntryParseProjectionResult.Projected ||
            sampleEntries is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .SampleEntryParseFailed;
        }

        if (PlaybackIsoBmffTrackCodecConfigurationProjector.Project(
                sampleDescriptionBox,
                sampleEntries,
                out var codecConfigurations,
                options.CodecConfiguration) !=
            PlaybackIsoBmffTrackCodecConfigurationProjectionResult.Projected ||
            codecConfigurations is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .CodecConfigurationProjectionFailed;
        }

        if (PlaybackIsoBmffTrackCodecPacketCompositionProjector.Compose(
                codecConfigurations,
                options.AvcRequirements,
                out var packetComposition,
                options.PacketComposition) !=
            PlaybackIsoBmffTrackCodecPacketCompositionProjectionResult
                .Composed ||
            packetComposition is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .PacketCompositionFailed;
        }

        if (PlaybackIsoBmffTrackCodecPacketBindingProjector.Bind(
                packetComposition,
                sources.TrackIdentity,
                out var trackBindings) !=
            PlaybackIsoBmffTrackCodecPacketBindingProjectionResult.Bound ||
            trackBindings is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .TrackBindingFailed;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketBindingProjector.Bind(
                trackBindings,
                sources.MovieTiming,
                out var movieBindings) !=
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult.Bound ||
            movieBindings is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .MovieBindingFailed;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceProjector.Project(
                movieBindings,
                out var sequenceProjection) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                .Projected ||
            sequenceProjection is null)
        {
            return PlaybackIsoBmffRegularTrackDemuxCompositionResult
                .PacketSequenceProjectionFailed;
        }

        composition = new(
            sources,
            options,
            plan,
            editAdmissions,
            admittedSamples,
            ownedPayloads,
            packetComposition,
            sequenceProjection);
        return PlaybackIsoBmffRegularTrackDemuxCompositionResult.Composed;
    }
}
