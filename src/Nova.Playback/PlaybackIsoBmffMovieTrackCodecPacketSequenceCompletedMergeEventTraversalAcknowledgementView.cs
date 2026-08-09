using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
{
    Projected,
    MissingCompletion,
    UnsupportedTraversalSource,
    EventCountMismatch,
    InvalidAcknowledgementChain,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement(
            int globalTraversalIndex,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement
                acknowledgement)
    {
        GlobalTraversalIndex = globalTraversalIndex;
        Acknowledgement = acknowledgement;
    }

    public int GlobalTraversalIndex { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement
            Acknowledgement
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent Event
        => Acknowledgement.Event;

    public int EventIndex => Acknowledgement.EventIndex;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
                completion,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement>
                acknowledgements)
    {
        Completion = completion;
        Acknowledgements = acknowledgements;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
            Completion
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement>
        Acknowledgements
    { get; }

    public int EventCount => Acknowledgements.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion?
                    completion,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView?
                    view)
    {
        view = null;
        if (completion is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
                    .MissingCompletion;
        }

        var segments = new Stack<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal>();
        IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
            source = completion.Source;
        while (source is
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalContinuation
                continuation)
        {
            segments.Push(continuation.Segment);
            source = continuation.Source;
        }

        if (source is not
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
                initial)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
                    .UnsupportedTraversalSource;
        }

        var projected = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement[
                completion.ActualEventCount];
        var globalIndex = 0;
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement?
            prior = null;
        if (!TryAppend(
                initial,
                completion,
                projected,
                ref globalIndex,
                ref prior))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
                    .InvalidAcknowledgementChain;
        }

        while (segments.Count > 0)
        {
            if (!TryAppend(
                    segments.Pop(),
                    completion,
                    projected,
                    ref globalIndex,
                    ref prior))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
                        .InvalidAcknowledgementChain;
            }
        }

        if (globalIndex != projected.Length ||
            projected.Length > 0 &&
            !ReferenceEquals(prior!.SuccessorCursor, completion.FinalCursor) ||
            projected.Length == 0 &&
            !ReferenceEquals(
                completion.InitialCursor,
                completion.FinalCursor))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
                    .EventCountMismatch;
        }

        view = new(completion, Array.AsReadOnly(projected));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementViewProjectionResult
                .Projected;
    }

    private static bool TryAppend(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal
            segment,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalCompletion
            completion,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement[]
            output,
        ref int globalIndex,
        ref PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement?
            prior)
    {
        if (prior is null &&
            !ReferenceEquals(segment.InitialCursor, completion.InitialCursor) ||
            prior is not null &&
            !ReferenceEquals(
                prior.SuccessorCursor,
                segment.InitialCursor))
        {
            return false;
        }

        var localIndex = 0;
        foreach (var acknowledgement in segment.Acknowledgements)
        {
            if (globalIndex >= output.Length ||
                acknowledgement.EventIndex !=
                    segment.InitialCursor.NextEventIndex + localIndex ||
                prior is not null &&
                !ReferenceEquals(
                    prior.SuccessorCursor,
                    acknowledgement.SourceCursor))
            {
                return false;
            }

            output[globalIndex] = new(globalIndex, acknowledgement);
            globalIndex++;
            localIndex++;
            prior = acknowledgement;
        }

        return localIndex == segment.EventCount &&
            (segment.EventCount > 0 || output.Length == 0);
    }
}
