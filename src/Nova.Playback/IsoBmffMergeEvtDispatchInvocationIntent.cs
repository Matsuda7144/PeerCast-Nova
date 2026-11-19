using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
{
    Projected,
    MissingRuntimeTargetLookupProjection,
    InvalidInvocationId,
    LookupLimitExceeded,
    SourceMismatch,
    InvalidRuntimeIdentity,
    EmptyPayload,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntent
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntent(
            Guid invocationId,
            int sequenceIndex,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookup
                lookup)
    {
        InvocationId = invocationId;
        SequenceIndex = sequenceIndex;
        Lookup = lookup;
    }

    public Guid InvocationId { get; }

    public int SequenceIndex { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookup
            Lookup
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest
            Request => Lookup.Resolution.Request;

    public Guid RuntimeInstanceId => Lookup.RuntimeInstanceId;

    public ulong RuntimeGeneration => Lookup.Generation;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection
                source,
            Guid invocationId,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntent>
                intents)
    {
        Source = source;
        InvocationId = invocationId;
        Intents = intents;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection
            Source
    { get; }

    public Guid InvocationId { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntent>
        Intents
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetLookupProjection?
                    source,
                Guid invocationId,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .MissingRuntimeTargetLookupProjection;
        }

        if (invocationId == Guid.Empty)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .InvalidInvocationId;
        }

        if (source.Lookups.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .LookupLimitExceeded;
        }

        if (source.Lookups.Count != source.Source.Resolutions.Count)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                    .SourceMismatch;
        }

        var intents = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntent[
                source.Lookups.Count];
        for (var index = 0; index < source.Lookups.Count; index++)
        {
            var lookup = source.Lookups[index];
            if (!ReferenceEquals(
                    lookup.Resolution,
                    source.Source.Resolutions[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                        .SourceMismatch;
            }

            if (lookup.RuntimeInstanceId == Guid.Empty || lookup.Generation == 0)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                        .InvalidRuntimeIdentity;
            }

            if (lookup.Resolution.Request.Payload.IsEmpty)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                        .EmptyPayload;
            }

            intents[index] = new(invocationId, index, lookup);
        }

        projection = new(source, invocationId, Array.AsReadOnly(intents));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchInvocationIntentResult
                .Projected;
    }
}
