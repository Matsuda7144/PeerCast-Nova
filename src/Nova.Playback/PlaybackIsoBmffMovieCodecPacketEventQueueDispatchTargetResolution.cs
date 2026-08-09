using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
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

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolution
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolution(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest request,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability
            capability)
    {
        Request = request;
        Capability = capability;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequest Request
    {
        get;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability
        Capability
    { get; }
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection
                source,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet
                capabilitySet,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolution>
                resolutions)
    {
        Source = source;
        CapabilitySet = capabilitySet;
        Resolutions = resolutions;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection
        Source
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet
        CapabilitySet
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolution>
        Resolutions
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolver
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
        Resolve(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRequestProjection?
                source,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet?
                capabilitySet,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                    .MissingRequestProjection;
        }

        if (capabilitySet is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                    .MissingCapabilitySet;
        }

        if (source.Requests.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                    .RequestLimitExceeded;
        }

        if (source.Requests.Count != source.Source.Bindings.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                    .SourceMismatch;
        }

        var resolutions = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolution[
                source.Requests.Count];
        for (var index = 0; index < source.Requests.Count; index++)
        {
            var request = source.Requests[index];
            if (!ReferenceEquals(request.Binding, source.Source.Bindings[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                        .SourceMismatch;
            }

            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability?
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
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                        .MissingCapability;
            }

            if (capability.MediaKind != request.MediaKind)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                        .MediaMismatch;
            }

            if (request.Payload.Length > capability.MaximumPayloadByteCount)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                        .PayloadLimitExceeded;
            }

            resolutions[index] = new(request, capability);
        }

        projection = new(
            source,
            capabilitySet,
            Array.AsReadOnly(resolutions));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionResult
                .Resolved;
    }
}
