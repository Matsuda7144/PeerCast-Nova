using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
{
    LookedUp,
    MissingTargetResolutionProjection,
    MissingRuntimeTargetRegistry,
    ResolutionLimitExceeded,
    SourceMismatch,
    MissingRegistration,
    MediaMismatch,
    InvalidGeneration,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolution
            resolution,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration
            registration)
    {
        Resolution = resolution;
        Registration = registration;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolution
        Resolution
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration
        Registration
    { get; }

    public Guid RuntimeInstanceId => Registration.RuntimeInstanceId;

    public ulong Generation => Registration.Generation;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection
                source,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry
                registry,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup>
                lookups)
    {
        Source = source;
        Registry = registry;
        Lookups = lookups;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection
        Source
    { get; }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry
        Registry
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup>
        Lookups
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjector
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
        Project(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetResolutionProjection?
                source,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry?
                registry,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                    .MissingTargetResolutionProjection;
        }

        if (registry is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                    .MissingRuntimeTargetRegistry;
        }

        if (source.Resolutions.Count >
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                    .ResolutionLimitExceeded;
        }

        if (source.Resolutions.Count != source.Source.Requests.Count)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                    .SourceMismatch;
        }

        var lookups = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookup[
                source.Resolutions.Count];
        for (var index = 0; index < source.Resolutions.Count; index++)
        {
            var resolution = source.Resolutions[index];
            if (!ReferenceEquals(
                    resolution.Request,
                    source.Source.Requests[index]))
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                        .SourceMismatch;
            }

            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration?
                registration = null;
            foreach (var candidate in registry.Registrations)
            {
                if (candidate.TargetId == resolution.Request.TargetId)
                {
                    registration = candidate;
                    break;
                }
            }

            if (registration is null)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                        .MissingRegistration;
            }

            if (registration.MediaKind != resolution.Request.MediaKind ||
                registration.MediaKind != resolution.Capability.MediaKind)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                        .MediaMismatch;
            }

            if (registration.Generation == 0)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                        .InvalidGeneration;
            }

            lookups[index] = new(resolution, registration);
        }

        projection = new(source, registry, Array.AsReadOnly(lookups));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetLookupResult
                .LookedUp;
    }
}
