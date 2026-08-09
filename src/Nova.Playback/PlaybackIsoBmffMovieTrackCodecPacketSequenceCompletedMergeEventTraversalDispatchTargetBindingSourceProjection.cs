namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
{
    Projected,
    MissingTargetSetSourceProjection,
    SourceMismatch,
    CandidateLimitExceeded,
    MissingMediaTarget,
    TargetMediaMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
                bindingProjection)
    {
        Source = source;
        BindingProjection = bindingProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
            BindingProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection?
                    source,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                    .MissingTargetSetSourceProjection;
        }

        var selection = source.Source.CandidateSelection;
        if (!ReferenceEquals(selection.Source, source.Source.Source.EligibilityProjection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinder
                .Bind(
                    selection,
                    source.TargetSet,
                    out var bindingProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                    .Bound ||
            bindingProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                    .CandidateLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                        .CandidateLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                    .MissingMediaTarget =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                        .MissingMediaTarget,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                    .TargetMediaMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                        .TargetMediaMismatch,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(bindingProjection.Selection, selection) ||
            !ReferenceEquals(bindingProjection.TargetSet, source.TargetSet))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, bindingProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult
                .Projected;
    }
}
