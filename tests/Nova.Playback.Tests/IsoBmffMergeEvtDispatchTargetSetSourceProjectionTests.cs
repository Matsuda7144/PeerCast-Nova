using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjectionTests
{
    [Fact]
    public void Project_RetainsCandidateSourceAndOpaqueTargetIds()
    {
        var source = CandidateSelection();
        var videoId = Guid.NewGuid();
        var audioId = Guid.NewGuid();

        var projection = Project(source, videoId, audioId);
        Assert.Same(source, projection.Source);
        Assert.Equal(videoId, projection.TargetSet.VideoTarget!.TargetId);
        Assert.Equal(audioId, projection.TargetSet.AudioTarget!.TargetId);
        Assert.Equal(2, projection.TargetSet.Count);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndInvalidTargets()
    {
        AssertResult(
            Result.MissingCandidateSelectionSourceProjection,
            null,
            Guid.NewGuid(),
            null);
        AssertResult(Result.MissingTarget, CandidateSelection(), null, null);
        AssertResult(
            Result.InvalidTargetId,
            CandidateSelection(),
            Guid.Empty,
            null);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection
                    source,
                Guid? videoTargetId,
                Guid? audioTargetId)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjector
                .Project(
                    source,
                    videoTargetId,
                    audioTargetId,
                    out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection
            CandidateSelection() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjectionTests
                    .Eligibility(1_000, Policy.ReadyOrExpired),
                16);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionSourceProjection?
            source,
        Guid? videoTargetId,
        Guid? audioTargetId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjector
                .Project(
                    source,
                    videoTargetId,
                    audioTargetId,
                    out var projection));
        Assert.Null(projection);
    }
}
