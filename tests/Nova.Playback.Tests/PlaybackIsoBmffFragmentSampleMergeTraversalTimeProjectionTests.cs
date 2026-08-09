using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionTests
{
    [Fact]
    public void Project_RetainsExactAcknowledgementsAndRationalTimes()
    {
        var view = View(4, 1);

        var projection = Project(view);

        Assert.Same(view, projection.AcknowledgementView);
        Assert.Equal(48_000u, projection.MediaTimescale);
        Assert.Equal(3, projection.SampleCount);
        for (var index = 0; index < projection.SampleCount; index++)
        {
            var timing = projection.Samples[index];
            var acknowledgement = view.Acknowledgements[index];
            var sample = acknowledgement.Sample.Sample;
            Assert.Same(acknowledgement, timing.Acknowledgement);
            Assert.Equal(48_000u, timing.SecondsDenominator);
            Assert.Equal(
                sample.DecodeSecondsNumerator,
                timing.DecodeSecondsNumerator);
            Assert.Equal(
                sample.DecodeEndSecondsNumeratorExclusive,
                timing.DecodeEndSecondsNumeratorExclusive);
            Assert.Equal(
                sample.PresentationSecondsNumerator,
                timing.PresentationSecondsNumerator);
            Assert.Equal(
                sample.DurationSecondsNumerator,
                timing.DurationSecondsNumerator);
        }
    }

    [Fact]
    public void Project_AcceptsEmptyAcknowledgementView()
    {
        var projection = Project(View(0, 0));

        Assert.Empty(projection.Samples);
        Assert.Equal(48_000u, projection.MediaTimescale);
    }

    [Fact]
    public void Project_RejectsMissingViewAndMediaTimescaleMismatch()
    {
        AssertResult(Result.InvalidInput, null);

        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(1);
        var original = source.Samples[0];
        var mismatchedSample = original with
        {
            Sample = original.Sample with { MediaTimescale = 44_100 },
        };
        var merge = new PlaybackIsoBmffFragmentSampleBatchPresentationMerge(
            source.Source,
            Array.AsReadOnly(new[] { mismatchedSample }));
        AssertResult(Result.MediaTimescaleMismatch, View(merge));
    }

    [Fact]
    public void Project_RejectsInconsistentExactDuration()
    {
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(1);
        var original = source.Samples[0];
        var invalidSample = original with
        {
            Sample = original.Sample with
            {
                DecodeEndSecondsNumeratorExclusive = new BigInteger(1),
            },
        };
        var merge = new PlaybackIsoBmffFragmentSampleBatchPresentationMerge(
            source.Source,
            Array.AsReadOnly(new[] { invalidSample }));
        AssertResult(Result.InvalidSampleTime, View(merge));
    }

    internal static PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection
        Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView view)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjector.Project(
                view,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView View(
            int sampleCount,
            int nextIndex) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
            .Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
                    .Completion(sampleCount, nextIndex));

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView View(
            PlaybackIsoBmffFragmentSampleBatchPresentationMerge merge)
    {
        var cursor = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            merge,
            0);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalResult.Traversed,
            PlaybackIsoBmffFragmentSampleMergeTraverser.Traverse(
                cursor,
                out var traversal));
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletionProjector
                .Project(traversal, out var completion));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewTests
                .Project(Assert.IsType<
                    PlaybackIsoBmffFragmentSampleMergeTraversalCompletion>(
                        completion));
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView? view)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjector.Project(
                view,
                out var projection));
        Assert.Null(projection);
    }
}
