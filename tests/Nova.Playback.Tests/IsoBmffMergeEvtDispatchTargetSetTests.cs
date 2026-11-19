using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetCreationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetTests
{
    [Fact]
    public void TryCreate_OwnsExplicitOpaqueMediaIdentities()
    {
        var videoId = Guid.NewGuid();
        var audioId = Guid.NewGuid();

        var targetSet = TargetSet(videoId, audioId);
        Assert.Equal(2, targetSet.Count);
        Assert.Equal(videoId, targetSet.VideoTarget!.TargetId);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Video,
            targetSet.VideoTarget.MediaKind);
        Assert.Equal(audioId, targetSet.AudioTarget!.TargetId);
        Assert.Equal(PlaybackIsoBmffHandlerKind.Audio,
            targetSet.AudioTarget.MediaKind);
    }

    [Fact]
    public void TryCreate_AcceptsOneTargetAndRejectsInvalidSets()
    {
        Assert.Equal(1, TargetSet(Guid.NewGuid(), null).Count);
        AssertResult(Result.MissingTarget, null, null);
        AssertResult(Result.InvalidTargetId, Guid.Empty, null);
        var duplicate = Guid.NewGuid();
        AssertResult(Result.DuplicateTargetId, duplicate, duplicate);
    }

    private static void AssertResult(
        Result expected,
        Guid? videoTargetId,
        Guid? audioTargetId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetFactory
                .TryCreate(videoTargetId, audioTargetId, out var targetSet));
        Assert.Null(targetSet);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet
            TargetSet(Guid? videoTargetId, Guid? audioTargetId)
    {
        Assert.Equal(
            Result.Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetFactory
                .TryCreate(videoTargetId, audioTargetId, out var targetSet));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSet>(
                targetSet);
    }
}
