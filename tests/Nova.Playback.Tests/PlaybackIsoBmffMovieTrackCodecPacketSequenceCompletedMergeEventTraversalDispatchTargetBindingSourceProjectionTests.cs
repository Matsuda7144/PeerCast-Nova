using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjectionTests
{
    [Fact]
    public void Project_BindsExactCandidatesToOpaqueMediaTargets()
    {
        var source = TargetSet();

        var projection = Project(source);
        Assert.Same(source, projection.Source);
        Assert.Same(
            source.Source.CandidateSelection,
            projection.BindingProjection.Selection);
        Assert.Same(source.TargetSet, projection.BindingProjection.TargetSet);
        Assert.Equal(
            source.Source.CandidateSelection.Candidates.Count,
            projection.BindingProjection.Bindings.Count);
    }

    [Fact]
    public void Project_RejectsMissingSourceAndMissingMediaTarget()
    {
        AssertResult(Result.MissingTargetSetSourceProjection, null);
        var candidateSource =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjectionTests
                .CandidateSelection();
        AssertResult(
            Result.MissingMediaTarget,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjectionTests
                .Project(candidateSource, Guid.NewGuid(), null));
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection
            TargetSet() =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjectionTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjectionTests
                    .CandidateSelection(),
                Guid.NewGuid(),
                Guid.NewGuid());

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetSetSourceProjection?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetBindingSourceProjector
                .Project(source, out var projection));
        Assert.Null(projection);
    }
}
