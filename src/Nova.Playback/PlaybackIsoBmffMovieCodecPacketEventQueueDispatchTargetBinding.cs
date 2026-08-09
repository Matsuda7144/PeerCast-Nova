using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
{
    Bound,
    MissingCandidateSelection,
    MissingTargetSet,
    CandidateLimitExceeded,
    SourceMismatch,
    MissingMediaTarget,
    TargetMediaMismatch,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate candidate,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget target)
    {
        Candidate = candidate;
        Target = target;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidate Candidate
    {
        get;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget Target
    {
        get;
    }
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection
                selection,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet targetSet,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding>
                bindings)
    {
        Selection = selection;
        TargetSet = targetSet;
        Bindings = bindings;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection
        Selection
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet TargetSet
    {
        get;
    }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding> Bindings
    {
        get;
    }
}

public static class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinder
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
        Bind(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection?
                selection,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet? targetSet,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingProjection?
                projection)
    {
        projection = null;
        if (selection is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                    .MissingCandidateSelection;
        }

        if (targetSet is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                    .MissingTargetSet;
        }

        if (selection.Candidates.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                    .CandidateLimitExceeded;
        }

        var bindings = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBinding[
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
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                        .SourceMismatch;
            }

            var target = candidate.Entry.Event.MediaKind switch
            {
                PlaybackIsoBmffHandlerKind.Video => targetSet.VideoTarget,
                PlaybackIsoBmffHandlerKind.Audio => targetSet.AudioTarget,
                _ => null,
            };
            if (target is null)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                        .MissingMediaTarget;
            }

            if (target.MediaKind != candidate.Entry.Event.MediaKind)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                        .TargetMediaMismatch;
            }

            bindings[index] = new(candidate, target);
        }

        projection = new(selection, targetSet, Array.AsReadOnly(bindings));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetBindingResult
                .Bound;
    }
}
