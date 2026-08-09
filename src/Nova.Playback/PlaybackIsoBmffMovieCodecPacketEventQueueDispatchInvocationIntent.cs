using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
{
    Projected,
    MissingRuntimeTargetLookupProjection,
    InvalidInvocationId,
    LookupLimitExceeded,
    SourceMismatch,
    InvalidRuntimeIdentity,
    EmptyPayload,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntent
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntent(
        Guid invocationId,
        int sequenceIndex,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup
            lookup)
    {
        InvocationId = invocationId;
        SequenceIndex = sequenceIndex;
        Lookup = lookup;
    }

    public Guid InvocationId { get; }

    public int SequenceIndex { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup
        Lookup
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest Request =>
        Lookup.Resolution.Request;

    public Guid RuntimeInstanceId => Lookup.RuntimeInstanceId;

    public ulong RuntimeGeneration => Lookup.Generation;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection
                source,
            Guid invocationId,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntent>
                intents)
    {
        Source = source;
        InvocationId = invocationId;
        Intents = intents;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection
        Source
    { get; }

    public Guid InvocationId { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntent>
        Intents
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection?
                source,
            Guid invocationId,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                    .MissingRuntimeTargetLookupProjection;
        }

        if (invocationId == Guid.Empty)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                    .InvalidInvocationId;
        }

        if (source.Lookups.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                    .LookupLimitExceeded;
        }

        if (source.Lookups.Count != source.Source.Resolutions.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                    .SourceMismatch;
        }

        var intents = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntent[
                source.Lookups.Count];
        for (var index = 0; index < source.Lookups.Count; index++)
        {
            var lookup = source.Lookups[index];
            if (!ReferenceEquals(
                    lookup.Resolution,
                    source.Source.Resolutions[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                        .SourceMismatch;
            }

            if (lookup.RuntimeInstanceId == Guid.Empty || lookup.Generation == 0)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                        .InvalidRuntimeIdentity;
            }

            if (lookup.Resolution.Request.Payload.IsEmpty)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                        .EmptyPayload;
            }

            intents[index] = new(invocationId, index, lookup);
        }

        projection = new(source, invocationId, Array.AsReadOnly(intents));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchInvocationIntentResult
                .Projected;
    }
}
