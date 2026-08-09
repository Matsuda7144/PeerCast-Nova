namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentContinuityChainTests
{
    [Fact]
    public void Project_ExactEndpointsProduceImmutableContinuousChain()
    {
        var first = Binding(1, 2, 0, 10, 10, 20);
        var second = Binding(2, 3, 10, 20, 20, 30);

        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult.Projected,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                [first, second],
                out var chain));
        var projected = Assert.IsType<PlaybackIsoBmffFragmentContinuityChain>(
            chain);
        Assert.Equal(7U, projected.TrackId);
        Assert.Equal([first, second], projected.Transitions);
        Assert.IsNotType<PlaybackIsoBmffFragmentContinuityBinding[]>(
            projected.Transitions);
        Assert.True(projected.IsContinuous);
    }

    [Fact]
    public void Project_DiscontinuousTransitionCanRemainExactlyChained()
    {
        var first = Binding(1, 3, 0, 10, 12, 20);

        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult.Projected,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                [first],
                out var chain));
        Assert.False(Assert.IsType<PlaybackIsoBmffFragmentContinuityChain>(
            chain).IsContinuous);
    }

    [Fact]
    public void Project_EmptyChainIsValidWithoutTrackIdentity()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult.Projected,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                [],
                out var chain));
        var projected = Assert.IsType<PlaybackIsoBmffFragmentContinuityChain>(
            chain);
        Assert.Null(projected.TrackId);
        Assert.Empty(projected.Transitions);
        Assert.True(projected.IsContinuous);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Project_InvalidLimitRejectsBeforeInputAccess(int maximum)
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult.InvalidConfiguration,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                null,
                out var chain,
                new() { MaximumTransitionCount = maximum }));
        Assert.Null(chain);
    }

    [Fact]
    public void Project_LimitRejectsBeforeCopy()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult
                .TransitionLimitExceeded,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                [
                    Binding(1, 2, 0, 1, 1, 2),
                    Binding(2, 3, 1, 2, 2, 3),
                ],
                out var chain,
                new() { MaximumTransitionCount = 1 }));
        Assert.Null(chain);
    }

    [Fact]
    public void Project_SequenceEndpointMismatchIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult.SequenceChainMismatch,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                [
                    Binding(1, 2, 0, 1, 1, 2),
                    Binding(3, 4, 1, 2, 2, 3),
                ],
                out var chain));
        Assert.Null(chain);
    }

    [Fact]
    public void Project_DecodeEndpointMismatchIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult.DecodeChainMismatch,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                [
                    Binding(1, 2, 0, 1, 1, 2),
                    Binding(2, 3, 9, 10, 10, 11),
                ],
                out var chain));
        Assert.Null(chain);
    }

    [Fact]
    public void Project_TrackMismatchIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityChainResult.TrackIdMismatch,
            PlaybackIsoBmffFragmentContinuityChainProjector.Project(
                [
                    Binding(1, 2, 0, 1, 1, 2, trackId: 7),
                    Binding(2, 3, 1, 2, 2, 3, trackId: 8),
                ],
                out var chain));
        Assert.Null(chain);
    }

    private static PlaybackIsoBmffFragmentContinuityBinding Binding(
        uint previousSequence,
        uint currentSequence,
        int previousStart,
        int previousEnd,
        int currentStart,
        int currentEnd,
        uint trackId = 7)
    {
        PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
            new(previousSequence),
            new(currentSequence),
            out var sequence);
        PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
                trackId,
                previousStart,
                previousEnd),
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
                trackId,
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
