using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackFragmentDecodeContinuityTests
{
    [Fact]
    public void Classify_ExactCursorMatchIsContinuous()
    {
        var continuity = Classify(Run(1, 10, 20), Run(1, 20, 30));

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Continuous,
            continuity.Kind);
        Assert.Equal(new BigInteger(20), continuity.ExpectedDecodeTime);
        Assert.Equal(new BigInteger(20), continuity.ActualDecodeTime);
        Assert.Equal(BigInteger.Zero, continuity.DecodeDelta);
        Assert.True(continuity.IsContinuous);
    }

    [Fact]
    public void Classify_PositiveDeltaIsGap()
    {
        var continuity = Classify(Run(1, 10, 20), Run(1, 25, 30));

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Gap,
            continuity.Kind);
        Assert.Equal(new BigInteger(5), continuity.DecodeDelta);
        Assert.False(continuity.IsContinuous);
    }

    [Fact]
    public void Classify_NegativeDeltaIsOverlap()
    {
        var continuity = Classify(Run(1, 10, 20), Run(1, 17, 30));

        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Overlap,
            continuity.Kind);
        Assert.Equal(new BigInteger(-3), continuity.DecodeDelta);
    }

    [Fact]
    public void Classify_ArbitraryPrecisionCursorIsPreserved()
    {
        var cursor = BigInteger.Pow(2, 100);
        Assert.Equal(
            BigInteger.Zero,
            Classify(
                Run(1, cursor - 1, cursor),
                Run(1, cursor, cursor + 1)).DecodeDelta);
    }

    [Fact]
    public void Classify_TrackMismatchIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityResult.TrackIdMismatch,
            PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
                Run(1, 0, 1),
                Run(2, 1, 2),
                out var continuity));
        Assert.Null(continuity);
    }

    [Fact]
    public void Classify_InvalidRunTimingIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityResult.InvalidRunTiming,
            PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
                Run(1, 2, 1),
                Run(1, 1, 2),
                out var continuity));
        Assert.Null(continuity);
    }

    [Fact]
    public void Classify_NullInputIsRejectedWithoutObservation()
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityResult.InvalidInput,
            PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
                null,
                null,
                out var continuity));
        Assert.Null(continuity);
    }

    private static PlaybackIsoBmffTrackFragmentDecodeContinuity Classify(
        PlaybackIsoBmffTimedFragmentRun previous,
        PlaybackIsoBmffTimedFragmentRun current)
    {
        Assert.Equal(
            PlaybackIsoBmffTrackFragmentDecodeContinuityResult.Classified,
            PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
                previous,
                current,
                out var continuity));
        return Assert.IsType<PlaybackIsoBmffTrackFragmentDecodeContinuity>(
            continuity);
    }

    internal static PlaybackIsoBmffTimedFragmentRun Run(
        uint trackId,
        BigInteger start,
        BigInteger end) =>
        new(
            trackId,
            sampleDescriptionIndex: 1,
            start,
            end,
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample>([]));
}
