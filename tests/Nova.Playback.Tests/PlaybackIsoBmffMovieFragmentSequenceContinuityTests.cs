using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieFragmentSequenceContinuityTests
{
    [Fact]
    public void Classify_ExactIncrementIsSequential()
    {
        var continuity = Classify(7, 8);

        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityKind.Sequential,
            continuity.Kind);
        Assert.Equal(8U, continuity.ExpectedSequenceNumber);
        Assert.Equal(BigInteger.One, continuity.RawSequenceDelta);
        Assert.True(continuity.IsContinuous);
    }

    [Fact]
    public void Classify_MaximumToZeroIsExplicitWrappedSequence()
    {
        var continuity = Classify(uint.MaxValue, 0);

        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityKind
                .WrappedSequential,
            continuity.Kind);
        Assert.Equal(0U, continuity.ExpectedSequenceNumber);
        Assert.Equal(-new BigInteger(uint.MaxValue), continuity.RawSequenceDelta);
        Assert.True(continuity.IsContinuous);
    }

    [Fact]
    public void Classify_EqualNumbersAreDuplicate()
    {
        var continuity = Classify(9, 9);

        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityKind.Duplicate,
            continuity.Kind);
        Assert.Equal(BigInteger.Zero, continuity.RawSequenceDelta);
        Assert.False(continuity.IsContinuous);
    }

    [Theory]
    [InlineData(1U, 3U)]
    [InlineData(3U, 1U)]
    public void Classify_OtherTransitionsAreDiscontinuous(
        uint previous,
        uint current)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityKind.Discontinuous,
            Classify(previous, current).Kind);
    }

    [Fact]
    public void Classify_NullInputIsRejectedWithoutObservation()
    {
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityResult.InvalidInput,
            PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
                null,
                null,
                out var continuity));
        Assert.Null(continuity);
    }

    private static PlaybackIsoBmffMovieFragmentSequenceContinuity Classify(
        uint previous,
        uint current)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieFragmentSequenceContinuityResult.Classified,
            PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
                new(previous),
                new(current),
                out var continuity));
        return Assert.IsType<PlaybackIsoBmffMovieFragmentSequenceContinuity>(
            continuity);
    }
}
