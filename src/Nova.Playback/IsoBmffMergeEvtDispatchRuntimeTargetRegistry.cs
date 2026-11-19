using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public readonly record struct
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration(
        Guid TargetId,
        Guid RuntimeInstanceId,
        PlaybackIsoBmffHandlerKind MediaKind,
        ulong Generation);

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
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
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistration
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistration(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration
                declaration)
    {
        Declaration = declaration;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration
            Declaration
    { get; }

    public Guid TargetId => Declaration.TargetId;

    public Guid RuntimeInstanceId => Declaration.RuntimeInstanceId;

    public PlaybackIsoBmffHandlerKind MediaKind => Declaration.MediaKind;

    public ulong Generation => Declaration.Generation;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry(
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistration>
                registrations)
    {
        Registrations = registrations;
    }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistration>
        Registrations
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryFactory
{
    public const int MaximumRegistrationCount = 2;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
            TryCreate(
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration>?
                    declarations,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry?
                    registry)
    {
        registry = null;
        if (declarations is null || declarations.Count == 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                    .MissingDeclaration;
        }

        if (declarations.Count > MaximumRegistrationCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                    .DeclarationLimitExceeded;
        }

        var registrations = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistration[
                declarations.Count];
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            if (declaration.TargetId == Guid.Empty)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                        .InvalidTargetId;
            }

            if (declaration.RuntimeInstanceId == Guid.Empty)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                        .InvalidRuntimeInstanceId;
            }

            if (declaration.MediaKind is not
                PlaybackIsoBmffHandlerKind.Video and not
                PlaybackIsoBmffHandlerKind.Audio)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                        .UnsupportedMediaKind;
            }

            if (declaration.Generation == 0)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                        .InvalidGeneration;
            }

            for (var priorIndex = 0; priorIndex < index; priorIndex++)
            {
                var prior = declarations[priorIndex];
                if (prior.TargetId == declaration.TargetId)
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                            .DuplicateTargetId;
                }

                if (prior.RuntimeInstanceId == declaration.RuntimeInstanceId)
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                            .DuplicateRuntimeInstanceId;
                }

                if (prior.MediaKind == declaration.MediaKind)
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                            .DuplicateMediaKind;
                }
            }

            registrations[index] = new(declaration);
        }

        registry = new(Array.AsReadOnly(registrations));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult
                .Created;
    }
}
