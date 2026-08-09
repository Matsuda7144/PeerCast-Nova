using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
{
    Bound,
    MissingCandidateSelection,
    MissingTargetSet,
    CandidateLimitExceeded,
    SourceMismatch,
    MissingMediaTarget,
    TargetMediaMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinding
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinding(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidate
                candidate,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget
                target)
    {
        Candidate = candidate;
        Target = target;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidate
            Candidate
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget
            Target
    { get; }
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
                selection,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet
                targetSet,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinding>
                bindings)
    {
        Selection = selection;
        TargetSet = targetSet;
        Bindings = bindings;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
            Selection
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet
            TargetSet
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinding>
        Bindings
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinder
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
            Bind(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection?
                    selection,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet?
                    targetSet,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingProjection?
                    projection)
    {
        projection = null;
        if (selection is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                    .MissingCandidateSelection;
        }

        if (targetSet is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                    .MissingTargetSet;
        }

        if (selection.Candidates.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                    .CandidateLimitExceeded;
        }

        var bindings = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBinding[
                selection.Candidates.Count];
        for (var index = 0; index < selection.Candidates.Count; index++)
        {
            var candidate = selection.Candidates[index];
            if ((uint)candidate.SourceIndex >=
                    (uint)selection.Source.Entries.Count ||
                !ReferenceEquals(
                    candidate.Eligibility,
                    selection.Source.Entries[candidate.SourceIndex]) ||
                !candidate.Eligibility.IsEligible)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                        .SourceMismatch;
            }

            var mediaKind = candidate.Event.Packet.TrackPacket.MediaKind;
            var target = mediaKind switch
            {
                PlaybackIsoBmffHandlerKind.Video => targetSet.VideoTarget,
                PlaybackIsoBmffHandlerKind.Audio => targetSet.AudioTarget,
                _ => null,
            };
            if (target is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                        .MissingMediaTarget;
            }

            if (target.MediaKind != mediaKind)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                        .TargetMediaMismatch;
            }

            bindings[index] = new(candidate, target);
        }

        projection = new(selection, targetSet, Array.AsReadOnly(bindings));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingResult
                .Bound;
    }
}
