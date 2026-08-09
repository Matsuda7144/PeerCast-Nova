using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleBatchChainTests
{
    [Fact]
    public void Project_ExactBatchAndTransitionEndpointsProduceImmutableChain()
    {
        var previous = Batch(1, 0, 10);
        var current = Batch(2, 10, 20);
        var continuity = Continuity(previous, current);

        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchChainResult.Projected,
            PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
                continuity,
                [previous, current],
                out var chain));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleBatchChain>(chain);
        Assert.Same(continuity, projected.Continuity);
        Assert.Equal(7U, projected.TrackId);
        Assert.Equal(48_000U, projected.MediaTimescale);
        Assert.Equal([previous, current], projected.Batches);
        Assert.IsNotType<PlaybackIsoBmffFragmentSampleBatch[]>(
            projected.Batches);
        Assert.Equal(0L, projected.TotalSampleCount);
    }

    [Fact]
    public void Project_EmptyBatchAndTransitionChainsAreValid()
    {
        var continuity = new PlaybackIsoBmffFragmentContinuityChain(
            null,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>([]));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchChainResult.Projected,
            PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
                continuity,
                [],
                out var chain));
        var projected = Assert.IsType<
            PlaybackIsoBmffFragmentSampleBatchChain>(chain);
        Assert.Null(projected.TrackId);
        Assert.Null(projected.MediaTimescale);
        Assert.Empty(projected.Batches);
    }

    [Fact]
    public void Project_OneBatchNeedsNoTransition()
    {
        var batch = Batch(7, 0, 1);
        var continuity = new PlaybackIsoBmffFragmentContinuityChain(
            null,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>([]));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchChainResult.Projected,
            PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
                continuity,
                [batch],
                out var chain));
        Assert.Equal(batch, Assert.Single(Assert.IsType<
            PlaybackIsoBmffFragmentSampleBatchChain>(chain).Batches));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Project_InvalidLimitRejectsBeforeInputAccess(int maximum)
    {
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchChainResult.InvalidConfiguration,
            PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
                null,
                null,
                out var chain,
                new() { MaximumBatchCount = maximum }));
        Assert.Null(chain);
    }

    [Fact]
    public void Project_BatchLimitRejectsBeforeCopy()
    {
        var continuity = new PlaybackIsoBmffFragmentContinuityChain(
            null,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>([]));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchChainResult.BatchLimitExceeded,
            PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
                continuity,
                [Batch(1, 0, 1)],
                out var chain,
                new() { MaximumBatchCount = 0 }));
        Assert.Null(chain);
    }

    [Fact]
    public void Project_BatchTransitionCountMismatchIsRejected()
    {
        var continuity = new PlaybackIsoBmffFragmentContinuityChain(
            null,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>([]));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleBatchChainResult
                .BatchTransitionCountMismatch,
            PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
                continuity,
                [Batch(1, 0, 1), Batch(2, 1, 2)],
                out var chain));
        Assert.Null(chain);
    }

    [Fact]
    public void Project_SequenceEndpointMismatchIsRejected()
    {
        var previous = Batch(1, 0, 10);
        var current = Batch(3, 10, 20);
        var wrongCurrent = Batch(2, 10, 20);
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchChainResult
                .SequenceEndpointMismatch,
            Continuity(previous, wrongCurrent),
            previous,
            current);
    }

    [Fact]
    public void Project_DecodeEndpointMismatchIsRejected()
    {
        var previous = Batch(1, 0, 10);
        var current = Batch(2, 10, 20);
        var wrongCurrent = Batch(2, 11, 20);
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchChainResult
                .DecodeEndpointMismatch,
            Continuity(previous, wrongCurrent),
            previous,
            current);
    }

    [Fact]
    public void Project_TrackMismatchIsRejected()
    {
        var previous = Batch(1, 0, 10, trackId: 7);
        var current = Batch(2, 10, 20, trackId: 8);
        var transitionCurrent = Batch(2, 10, 20, trackId: 7);
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchChainResult.TrackIdMismatch,
            Continuity(previous, transitionCurrent),
            previous,
            current);
    }

    [Fact]
    public void Project_TimescaleMismatchIsRejected()
    {
        var previous = Batch(1, 0, 10, mediaTimescale: 48_000);
        var current = Batch(2, 10, 20, mediaTimescale: 90_000);
        AssertResult(
            PlaybackIsoBmffFragmentSampleBatchChainResult
                .MediaTimescaleMismatch,
            Continuity(previous, current),
            previous,
            current);
    }

    private static void AssertResult(
        PlaybackIsoBmffFragmentSampleBatchChainResult expected,
        PlaybackIsoBmffFragmentContinuityChain continuity,
        params PlaybackIsoBmffFragmentSampleBatch[] batches)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleBatchChainProjector.Project(
                continuity,
                batches,
                out var chain));
        Assert.Null(chain);
    }

    internal static PlaybackIsoBmffFragmentSampleBatch Batch(
        uint sequence,
        int start,
        int end,
        uint trackId = 7,
        uint mediaTimescale = 48_000)
    {
        var timed = new PlaybackIsoBmffTimedFragmentRun(
            trackId,
            sampleDescriptionIndex: 1,
            start,
            end,
            new ReadOnlyCollection<PlaybackIsoBmffTimedFragmentSample>([]));
        var rational = new PlaybackIsoBmffFragmentSampleRationalTimeProjection(
            trackId,
            sampleDescriptionIndex: 1,
            mediaTimescale,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentSampleRationalTime>([]));
        PlaybackIsoBmffFragmentSamplePresentationOrderer.Order(
            rational,
            out var order);
        PlaybackIsoBmffFragmentSampleBatchBinder.Bind(
            new(sequence),
            timed,
            order,
            out var batch);
        return Assert.IsType<PlaybackIsoBmffFragmentSampleBatch>(batch);
    }

    internal static PlaybackIsoBmffFragmentContinuityChain Continuity(
        PlaybackIsoBmffFragmentSampleBatch previous,
        PlaybackIsoBmffFragmentSampleBatch current)
    {
        PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier.Classify(
            previous.FragmentHeader,
            current.FragmentHeader,
            out var sequence);
        PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier.Classify(
            previous.TimedRun,
            current.TimedRun,
            out var decode);
        PlaybackIsoBmffFragmentContinuityBinder.Bind(
            sequence,
            decode,
            out var binding);
        return new(
            previous.TrackId,
            new ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding>(
                [Assert.IsType<PlaybackIsoBmffFragmentContinuityBinding>(binding)]));
    }
}
