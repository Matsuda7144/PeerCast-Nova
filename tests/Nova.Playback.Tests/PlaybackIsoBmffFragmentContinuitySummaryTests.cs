using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentContinuitySummaryTests
{
    [Fact]
    public void Project_ContinuousChainHasContinuousDisposition()
    {
        var chain = Chain(Binding(1, 2, 0, 10, 10, 20));

        Assert.Equal(
            PlaybackIsoBmffFragmentContinuitySummaryResult.Projected,
            PlaybackIsoBmffFragmentContinuitySummaryProjector.Project(
                chain,
                out var summary));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentContinuitySummary>(summary);
        Assert.Same(chain, projected.Chain);
        Assert.Equal(1, projected.TransitionCount);
        Assert.Equal(1, projected.ContinuousTransitionCount);
        Assert.Equal(0, projected.SequenceDiscontinuityCount);
        Assert.Equal(0, projected.DecodeGapCount);
        Assert.Equal(0, projected.DecodeOverlapCount);
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityDisposition.Continuous,
            projected.Disposition);
    }

    [Fact]
    public void Project_CountsIndependentDiscontinuityDimensions()
    {
        var chain = Chain(
            Binding(1, 3, 0, 10, 12, 20),
            Binding(3, 4, 12, 20, 18, 30));

        PlaybackIsoBmffFragmentContinuitySummaryProjector.Project(
            chain,
            out var summary);
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentContinuitySummary>(summary);
        Assert.Equal(0, projected.ContinuousTransitionCount);
        Assert.Equal(1, projected.SequenceDiscontinuityCount);
        Assert.Equal(1, projected.DecodeGapCount);
        Assert.Equal(1, projected.DecodeOverlapCount);
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityDisposition
                .DiscontinuityHandlingRequired,
            projected.Disposition);
    }

    [Fact]
    public void Project_EmptyChainIsVacuouslyContinuous()
    {
        PlaybackIsoBmffFragmentContinuitySummaryProjector.Project(
            Chain(),
            out var summary);
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentContinuitySummary>(summary);
        Assert.Equal(0, projected.TransitionCount);
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityDisposition.Continuous,
            projected.Disposition);
    }

    [Fact]
    public void Project_NullChainIsRejectedWithoutSummary()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuitySummaryResult.InvalidInput,
            PlaybackIsoBmffFragmentContinuitySummaryProjector.Project(
                null,
                out var summary));
        Assert.Null(summary);
    }

    private static PlaybackIsoBmffFragmentContinuityChain Chain(
        params PlaybackIsoBmffFragmentContinuityBinding[] bindings) =>
        new(
            bindings.Length == 0 ? null : bindings[0].TrackId,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>(
                bindings));

    private static PlaybackIsoBmffFragmentContinuityBinding Binding(
        uint previousSequence,
        uint currentSequence,
        int previousStart,
        int previousEnd,
        int currentStart,
        int currentEnd)
    {
        PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
            new(previousSequence),
            new(currentSequence),
            out var sequence);
        PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
                7,
                previousStart,
                previousEnd),
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
                7,
                currentStart,
                currentEnd),
            out var decode);
        PlaybackIsoBmffFragmentContinuityBinder.Bind(
            sequence,
            decode,
            out var binding);
        return Assert.IsType<PlaybackIsoBmffFragmentContinuityBinding>(binding);
    }
}
