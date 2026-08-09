namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetTests
{
    [Fact]
    public void TryCreate_OwnsExplicitVideoAndAudioTargetIdentities()
    {
        var videoId = Guid.NewGuid();
        var audioId = Guid.NewGuid();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetFactory
                .TryCreate(videoId, audioId, out var targetSet));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet>(
                targetSet);
        Assert.Equal(2, created.Count);
        var video = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget>(
                created.VideoTarget);
        Assert.Equal(videoId, video.TargetId);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Video, video.MediaKind);
        var audio = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTarget>(
                created.AudioTarget);
        Assert.Equal(audioId, audio.TargetId);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Audio, audio.MediaKind);
    }

    [Fact]
    public void TryCreate_AcceptsOneExplicitTarget()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetFactory
                .TryCreate(Guid.NewGuid(), null, out var targetSet));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSet>(
                targetSet);
        Assert.Equal(1, created.Count);
        Assert.NotNull(created.VideoTarget);
        Assert.Null(created.AudioTarget);
    }

    [Fact]
    public void TryCreate_RejectsMissingAndEmptyTargetIdentities()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .MissingTarget,
            null,
            null);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .InvalidTargetId,
            Guid.Empty,
            null);
    }

    [Fact]
    public void TryCreate_RejectsDuplicateTargetIdentities()
    {
        var targetId = Guid.NewGuid();
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
                .DuplicateTargetId,
            targetId,
            targetId);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetCreationResult
            expected,
        Guid? videoTargetId,
        Guid? audioTargetId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetSetFactory
                .TryCreate(videoTargetId, audioTargetId, out var targetSet));
        Assert.Null(targetSet);
    }
}
