using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public readonly record struct
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration(
        Guid TargetId,
        Guid RuntimeInstanceId,
        PlaybackIsoBmffHandlerKind MediaKind,
        ulong Generation);

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
{
    Created,
    MissingDeclaration,
    DeclarationLimitExceeded,
    InvalidTargetId,
    InvalidRuntimeInstanceId,
    UnsupportedMediaKind,
    InvalidGeneration,
    DuplicateTargetId,
    DuplicateRuntimeInstanceId,
    DuplicateMediaKind,
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration
                declaration)
    {
        Declaration = declaration;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration
        Declaration
    { get; }

    public Guid TargetId => Declaration.TargetId;

    public Guid RuntimeInstanceId => Declaration.RuntimeInstanceId;

    public PlaybackIsoBmffHandlerKind MediaKind => Declaration.MediaKind;

    public ulong Generation => Declaration.Generation;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry(
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration>
                registrations)
    {
        Registrations = registrations;
    }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration>
        Registrations
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryFactory
{
    public const int MaximumRegistrationCount = 2;

    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
        TryCreate(
            IReadOnlyList<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration>?
                declarations,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry?
                registry)
    {
        registry = null;
        if (declarations is null || declarations.Count == 0)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                    .MissingDeclaration;
        }

        if (declarations.Count > MaximumRegistrationCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                    .DeclarationLimitExceeded;
        }

        var registrations = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistration[
                declarations.Count];
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            if (declaration.TargetId == Guid.Empty)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                        .InvalidTargetId;
            }

            if (declaration.RuntimeInstanceId == Guid.Empty)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                        .InvalidRuntimeInstanceId;
            }

            if (declaration.MediaKind is not
                PlaybackIsoBmffHandlerKind.Video and not
                PlaybackIsoBmffHandlerKind.Audio)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                        .UnsupportedMediaKind;
            }

            if (declaration.Generation == 0)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                        .InvalidGeneration;
            }

            for (var priorIndex = 0; priorIndex < index; priorIndex++)
            {
                var prior = declarations[priorIndex];
                if (prior.TargetId == declaration.TargetId)
                {
                    return
                        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                            .DuplicateTargetId;
                }

                if (prior.RuntimeInstanceId == declaration.RuntimeInstanceId)
                {
                    return
                        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                            .DuplicateRuntimeInstanceId;
                }

                if (prior.MediaKind == declaration.MediaKind)
                {
                    return
                        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                            .DuplicateMediaKind;
                }
            }

            registrations[index] = new(declaration);
        }

        registry = new(Array.AsReadOnly(registrations));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .Created;
    }
}
