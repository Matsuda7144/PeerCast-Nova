using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
{
    Resolved,
    MissingRequestProjection,
    MissingCapabilitySet,
    RequestLimitExceeded,
    SourceMismatch,
    MissingCapability,
    MediaMismatch,
    PayloadLimitExceeded,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolution
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolution(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest
                request,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability
                capability)
    {
        Request = request;
        Capability = capability;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequest
            Request
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability
            Capability
    { get; }
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection
                source,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet
                capabilitySet,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolution>
                resolutions)
    {
        Source = source;
        CapabilitySet = capabilitySet;
        Resolutions = resolutions;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection
            Source
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet
            CapabilitySet
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolution>
        Resolutions
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolver
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
            Resolve(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRequestProjection?
                    source,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet?
                    capabilitySet,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                    .MissingRequestProjection;
        }

        if (capabilitySet is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                    .MissingCapabilitySet;
        }

        if (source.Requests.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                    .RequestLimitExceeded;
        }

        if (source.Requests.Count != source.Source.Bindings.Count)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                    .SourceMismatch;
        }

        var resolutions = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolution[
                source.Requests.Count];
        for (var index = 0; index < source.Requests.Count; index++)
        {
            var request = source.Requests[index];
            if (!ReferenceEquals(request.Binding, source.Source.Bindings[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                        .SourceMismatch;
            }

            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability?
                capability = null;
            foreach (var candidate in capabilitySet.Capabilities)
            {
                if (candidate.TargetId == request.TargetId)
                {
                    capability = candidate;
                    break;
                }
            }

            if (capability is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                        .MissingCapability;
            }

            if (capability.MediaKind != request.MediaKind)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                        .MediaMismatch;
            }

            if (request.Payload.Length > capability.MaximumPayloadByteCount)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                        .PayloadLimitExceeded;
            }

            resolutions[index] = new(request, capability);
        }

        projection = new(source, capabilitySet, Array.AsReadOnly(resolutions));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetResolutionResult
                .Resolved;
    }
}
