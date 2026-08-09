namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentContinuityBindingTests
{
    [Theory]
    [InlineData(true, true, PlaybackIsoBmffFragmentContinuityKind.Continuous)]
    [InlineData(
        false,
        true,
        PlaybackIsoBmffFragmentContinuityKind.SequenceDiscontinuity)]
    [InlineData(
        true,
        false,
        PlaybackIsoBmffFragmentContinuityKind.DecodeDiscontinuity)]
    [InlineData(
        false,
        false,
        PlaybackIsoBmffFragmentContinuityKind
            .SequenceAndDecodeDiscontinuity)]
    public void Bind_ClassifiesIndependentContinuityDimensions(
        bool sequenceContinuous,
        bool decodeContinuous,
        PlaybackIsoBmffFragmentContinuityKind expected)
    {
        var sequence = Sequence(sequenceContinuous ? 2U : 3U);
        var decode = Decode(decodeContinuous ? 10 : 11);

        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityBindingResult.Bound,
            PlaybackIsoBmffFragmentContinuityBinder.Bind(
                sequence,
                decode,
                out var binding));
        var bound = Assert.IsType<PlaybackIsoBmffFragmentContinuityBinding>(
            binding);
        Assert.Same(sequence, bound.Sequence);
        Assert.Same(decode, bound.Decode);
        Assert.Equal(7U, bound.TrackId);
        Assert.Equal(expected, bound.Kind);
        Assert.Equal(
            expected == PlaybackIsoBmffFragmentContinuityKind.Continuous,
            bound.IsContinuous);
    }

    [Fact]
    public void Bind_NullInputsAreRejectedWithoutBinding()
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentContinuityBindingResult.InvalidInput,
            PlaybackIsoBmffFragmentContinuityBinder.Bind(
                null,
                null,
                out var binding));
        Assert.Null(binding);
    }

    private static PlaybackIsoBmffMovieFragmentSequenceContinuity Sequence(
        uint current)
    {
        PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
            new(1),
            new(current),
            out var continuity);
        return Assert.IsType<PlaybackIsoBmffMovieFragmentSequenceContinuity>(
            continuity);
    }

    private static PlaybackIsoBmffTrackFragmentDecodeContinuity Decode(
        int currentStart)
    {
        PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
                7,
                0,
                10),
            PlaybackIsoBmffTrackFragmentDecodeContinuityTests.Run(
                7,
                currentStart,
                20),
            out var continuity);
        return Assert.IsType<PlaybackIsoBmffTrackFragmentDecodeContinuity>(
            continuity);
    }
}
