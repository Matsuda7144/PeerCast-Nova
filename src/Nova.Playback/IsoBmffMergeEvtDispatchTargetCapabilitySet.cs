using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public readonly record struct
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration(
        Guid TargetId,
        PlaybackIsoBmffHandlerKind MediaKind,
        int MaximumPayloadByteCount);

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
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
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration
                declaration)
    {
        Declaration = declaration;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration
            Declaration
    { get; }

    public Guid TargetId => Declaration.TargetId;

    public PlaybackIsoBmffHandlerKind MediaKind => Declaration.MediaKind;

    public int MaximumPayloadByteCount => Declaration.MaximumPayloadByteCount;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet(
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability>
                capabilities)
    {
        Capabilities = capabilities;
    }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability>
        Capabilities
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetFactory
{
    public const int MaximumCapabilityCount = 2;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
            TryCreate(
                IReadOnlyList<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration>?
                    declarations,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet?
                    capabilitySet)
    {
        capabilitySet = null;
        if (declarations is null || declarations.Count == 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                    .MissingDeclaration;
        }

        if (declarations.Count > MaximumCapabilityCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                    .DeclarationLimitExceeded;
        }

        var capabilities = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapability[
                declarations.Count];
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            if (declaration.TargetId == Guid.Empty)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                        .InvalidTargetId;
            }

            if (declaration.MediaKind is not
                PlaybackIsoBmffHandlerKind.Video and not
                PlaybackIsoBmffHandlerKind.Audio)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                        .UnsupportedMediaKind;
            }

            if (declaration.MaximumPayloadByteCount < 1)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                        .InvalidMaximumPayloadByteCount;
            }

            for (var priorIndex = 0; priorIndex < index; priorIndex++)
            {
                if (declarations[priorIndex].TargetId == declaration.TargetId)
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                            .DuplicateTargetId;
                }

                if (declarations[priorIndex].MediaKind == declaration.MediaKind)
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                            .DuplicateMediaKind;
                }
            }

            capabilities[index] = new(declaration);
        }

        capabilitySet = new(Array.AsReadOnly(capabilities));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult
                .Created;
    }
}
