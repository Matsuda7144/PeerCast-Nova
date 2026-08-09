namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
{
    Created,
    MissingTarget,
    InvalidTargetId,
    DuplicateTargetId,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget(
        Guid targetId,
        PlaybackIsoBmffHandlerKind mediaKind)
    {
        TargetId = targetId;
        MediaKind = mediaKind;
    }

    public Guid TargetId { get; }

    public PlaybackIsoBmffHandlerKind MediaKind { get; }
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet
{
    internal PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget? videoTarget,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget? audioTarget)
    {
        VideoTarget = videoTarget;
        AudioTarget = audioTarget;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget? VideoTarget
    {
        get;
    }

    public PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget? AudioTarget
    {
        get;
    }

    public int Count => (VideoTarget is null ? 0 : 1) +
        (AudioTarget is null ? 0 : 1);
}

public static class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetFactory
{
    public static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
        TryCreate(
            Guid? videoTargetId,
            Guid? audioTargetId,
            out PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet?
                targetSet)
    {
        targetSet = null;
        if (videoTargetId is null && audioTargetId is null)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                    .MissingTarget;
        }

        if (videoTargetId == Guid.Empty || audioTargetId == Guid.Empty)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                    .InvalidTargetId;
        }

        if (videoTargetId is not null && videoTargetId == audioTargetId)
        {
            return
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                    .DuplicateTargetId;
        }

        var videoTarget = videoTargetId is Guid videoId
            ? new PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget(
                videoId,
                PlaybackIsoBmffHandlerKind.Video)
            : null;
        var audioTarget = audioTargetId is Guid audioId
            ? new PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget(
                audioId,
                PlaybackIsoBmffHandlerKind.Audio)
            : null;
        targetSet = new(videoTarget, audioTarget);
        return
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .Created;
    }
}
