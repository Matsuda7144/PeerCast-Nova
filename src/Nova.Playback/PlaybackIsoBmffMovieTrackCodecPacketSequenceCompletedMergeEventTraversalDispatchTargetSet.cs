namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetCreationResult
{
    Created,
    MissingTarget,
    InvalidTargetId,
    DuplicateTargetId,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget(
            Guid targetId,
            PlaybackIsoBmffHandlerKind mediaKind)
    {
        TargetId = targetId;
        MediaKind = mediaKind;
    }

    public Guid TargetId { get; }

    public PlaybackIsoBmffHandlerKind MediaKind { get; }
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget?
                videoTarget,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget?
                audioTarget)
    {
        VideoTarget = videoTarget;
        AudioTarget = audioTarget;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget?
            VideoTarget
    { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget?
            AudioTarget
    { get; }

    public int Count => (VideoTarget is null ? 0 : 1) +
        (AudioTarget is null ? 0 : 1);
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetFactory
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetCreationResult
            TryCreate(
                Guid? videoTargetId,
                Guid? audioTargetId,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet?
                    targetSet)
    {
        targetSet = null;
        if (videoTargetId is null && audioTargetId is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetCreationResult
                    .MissingTarget;
        }

        if (videoTargetId == Guid.Empty || audioTargetId == Guid.Empty)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetCreationResult
                    .InvalidTargetId;
        }

        if (videoTargetId is not null && videoTargetId == audioTargetId)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetCreationResult
                    .DuplicateTargetId;
        }

        var videoTarget = videoTargetId is Guid videoId
            ? new PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget(
                videoId,
                PlaybackIsoBmffHandlerKind.Video)
            : null;
        var audioTarget = audioTargetId is Guid audioId
            ? new PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTarget(
                audioId,
                PlaybackIsoBmffHandlerKind.Audio)
            : null;
        targetSet = new(videoTarget, audioTarget);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetCreationResult
                .Created;
    }
}
