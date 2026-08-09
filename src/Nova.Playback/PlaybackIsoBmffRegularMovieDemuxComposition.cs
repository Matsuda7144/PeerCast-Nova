using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieDemuxCompositionResult
{
    Composed,
    MissingTrackRequests,
    EmptyTrackRequests,
    InvalidConfiguration,
    TrackLimitExceeded,
    MissingTrackRequest,
    TrackCompositionFailed,
    SequenceSetProjectionFailed,
    SelectionProjectionFailed,
    CursorSetProjectionFailed,
    MergeTraversalProjectionFailed,
}

public sealed record PlaybackIsoBmffRegularMovieDemuxTrackRequest(
    ReadOnlyMemory<byte> SampleDescriptionBox,
    PlaybackIsoBmffRegularTrackDemuxCompositionSources? Sources,
    PlaybackIsoBmffRegularTrackDemuxCompositionOptions? Options);

public sealed record PlaybackIsoBmffRegularMovieDemuxCompositionOptions
{
    public const int DefaultMaximumTrackCount = 16;

    public const int DefaultMaximumMergeStepCount = 4_096;

    public int MaximumTrackCount { get; init; } = DefaultMaximumTrackCount;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
        EqualIntervalTieBreak
    { get; init; } =
        PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak
            .PreferVideo;

    public int MaximumMergeStepCount { get; init; } =
        DefaultMaximumMergeStepCount;
}

public sealed class PlaybackIsoBmffRegularMovieDemuxComposition
{
    internal PlaybackIsoBmffRegularMovieDemuxComposition(
        PlaybackIsoBmffRegularMovieDemuxCompositionOptions options,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
            selectionRequest,
        ReadOnlyCollection<PlaybackIsoBmffRegularTrackDemuxComposition>
            tracks,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection sequenceSet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection
            selection,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
            cursorSet,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection
            mergeTraversal)
    {
        Options = options;
        SelectionRequest = selectionRequest;
        Tracks = tracks;
        SequenceSet = sequenceSet;
        Selection = selection;
        CursorSet = cursorSet;
        MergeTraversal = mergeTraversal;
    }

    public PlaybackIsoBmffRegularMovieDemuxCompositionOptions Options
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
        SelectionRequest
    { get; }

    public IReadOnlyList<PlaybackIsoBmffRegularTrackDemuxComposition> Tracks
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection SequenceSet
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection
        Selection
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjection
        CursorSet
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjection
        MergeTraversal
    { get; }

    public bool IsMergeExhausted => MergeTraversal.Traversal.IsExhausted;

    public bool IsMergeTruncated => MergeTraversal.Traversal.IsTruncated;
}

public static class PlaybackIsoBmffRegularMovieDemuxComposer
{
    public static PlaybackIsoBmffRegularMovieDemuxCompositionResult Compose(
        ReadOnlySpan<byte> file,
        IReadOnlyList<PlaybackIsoBmffRegularMovieDemuxTrackRequest>?
            trackRequests,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
            selectionRequest,
        out PlaybackIsoBmffRegularMovieDemuxComposition? composition,
        PlaybackIsoBmffRegularMovieDemuxCompositionOptions? options = null)
    {
        composition = null;
        if (trackRequests is null)
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .MissingTrackRequests;
        }

        if (trackRequests.Count == 0)
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .EmptyTrackRequests;
        }

        options ??= new();
        if (options.MaximumTrackCount < 1 ||
            options.MaximumMergeStepCount < 1 ||
            options.MaximumMergeStepCount >
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraverser
                    .MaximumStepCount ||
            !Enum.IsDefined(options.EqualIntervalTieBreak))
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .InvalidConfiguration;
        }

        if (trackRequests.Count > options.MaximumTrackCount ||
            trackRequests.Count >
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory
                    .MaximumSequenceCount)
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .TrackLimitExceeded;
        }

        var tracks = new PlaybackIsoBmffRegularTrackDemuxComposition[
            trackRequests.Count];
        var sequenceSources = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection[
                trackRequests.Count];
        for (var index = 0; index < trackRequests.Count; index++)
        {
            var request = trackRequests[index];
            if (request is null)
            {
                return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                    .MissingTrackRequest;
            }

            if (PlaybackIsoBmffRegularTrackDemuxComposer.Compose(
                    file,
                    request.SampleDescriptionBox.Span,
                    request.Sources,
                    out var track,
                    request.Options) !=
                PlaybackIsoBmffRegularTrackDemuxCompositionResult.Composed ||
                track is null)
            {
                return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                    .TrackCompositionFailed;
            }

            tracks[index] = track;
            sequenceSources[index] = track.SequenceProjection;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjector.Project(
                sequenceSources,
                out var sequenceSet) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                .Projected ||
            sequenceSet is null)
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .SequenceSetProjectionFailed;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjector
                .Project(
                    sequenceSet,
                    selectionRequest,
                    out var selection) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                .Projected ||
            selection is null)
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .SelectionProjectionFailed;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjector
                .Project(selection, out var cursorSet) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetProjectionResult
                .Projected ||
            cursorSet is null)
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .CursorSetProjectionFailed;
        }

        if (PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjector
                .Project(
                    cursorSet,
                    options.EqualIntervalTieBreak,
                    options.MaximumMergeStepCount,
                    out var mergeTraversal) !=
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalProjectionResult
                .Projected ||
            mergeTraversal is null)
        {
            return PlaybackIsoBmffRegularMovieDemuxCompositionResult
                .MergeTraversalProjectionFailed;
        }

        composition = new(
            options,
            selectionRequest,
            Array.AsReadOnly(tracks),
            sequenceSet,
            selection,
            cursorSet,
            mergeTraversal);
        return PlaybackIsoBmffRegularMovieDemuxCompositionResult.Composed;
    }
}
