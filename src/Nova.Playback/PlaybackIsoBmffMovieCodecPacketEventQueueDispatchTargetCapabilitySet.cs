using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public readonly record struct
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration(
        Guid TargetId,
        PlaybackIsoBmffHandlerKind MediaKind,
        int MaximumPayloadByteCount);

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
{
    Created,
    MissingDeclaration,
    DeclarationLimitExceeded,
    InvalidTargetId,
    UnsupportedMediaKind,
    InvalidMaximumPayloadByteCount,
    DuplicateTargetId,
    DuplicateMediaKind,
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration
            declaration)
    {
        Declaration = declaration;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration
        Declaration
    { get; }

    public Guid TargetId => Declaration.TargetId;

    public PlaybackIsoBmffHandlerKind MediaKind => Declaration.MediaKind;

    public int MaximumPayloadByteCount => Declaration.MaximumPayloadByteCount;
}

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet
{
    internal
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet(
            ReadOnlyCollection<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability>
                capabilities)
    {
        Capabilities = capabilities;
    }

    public IReadOnlyList<
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability>
        Capabilities
    { get; }
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetFactory
{
    public const int MaximumCapabilityCount = 2;

    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
        TryCreate(
            IReadOnlyList<
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration>?
                declarations,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet?
                capabilitySet)
    {
        capabilitySet = null;
        if (declarations is null || declarations.Count == 0)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                    .MissingDeclaration;
        }

        if (declarations.Count > MaximumCapabilityCount)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                    .DeclarationLimitExceeded;
        }

        var capabilities = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapability[
                declarations.Count];
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            if (declaration.TargetId == Guid.Empty)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                        .InvalidTargetId;
            }

            if (declaration.MediaKind is not
                PlaybackIsoBmffHandlerKind.Video and not
                PlaybackIsoBmffHandlerKind.Audio)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                        .UnsupportedMediaKind;
            }

            if (declaration.MaximumPayloadByteCount < 1)
            {
                return
                    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                        .InvalidMaximumPayloadByteCount;
            }

            for (var priorIndex = 0; priorIndex < index; priorIndex++)
            {
                if (declarations[priorIndex].TargetId == declaration.TargetId)
                {
                    return
                        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                            .DuplicateTargetId;
                }

                if (declarations[priorIndex].MediaKind == declaration.MediaKind)
                {
                    return
                        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                            .DuplicateMediaKind;
                }
            }

            capabilities[index] = new(declaration);
        }

        capabilitySet = new(Array.AsReadOnly(capabilities));
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .Created;
    }
}
