using System.Numerics;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult;
using Role = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole;
using TieBreak = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceEqualIntervalTieBreak;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionTests
{
    [Fact]
    public void TryProject_OrdersEveryAppearanceAndRetainsExactMergeSteps()
    {
        var videoAppearances = new[]
        {
            Appearance(90, 180, 90_000),
            Appearance(45_000, 54_000, 90_000),
        };
        var audioAppearances = new[]
        {
            Appearance(24, 48, 48_000),
            Appearance(24_000, 28_800, 48_000),
        };
        var view = View(videoAppearances, audioAppearances);

        var projection = Project(view, 16);
        Assert.Same(view, projection.StepView);
        Assert.Equal(8, projection.EventCount);
        foreach (var step in view.Steps)
        {
            var expected = step.Packet.Appearances;
            var actual = projection.Events
                .Where(candidate => ReferenceEquals(candidate.Step, step))
                .OrderBy(candidate => candidate.AppearanceIndex)
                .ToArray();
            Assert.Equal(expected.Count, actual.Length);
            for (var appearanceIndex = 0;
                appearanceIndex < expected.Count;
                appearanceIndex++)
            {
                Assert.Equal(appearanceIndex,
                    actual[appearanceIndex].AppearanceIndex);
                Assert.Equal(expected[appearanceIndex],
                    actual[appearanceIndex].Appearance);
                Assert.Same(step.Packet, actual[appearanceIndex].Packet);
            }
        }

        AssertOrdered(projection.Events);
    }

    [Fact]
    public void TryProject_BreaksEqualIntervalsByGlobalMergeIndex()
    {
        var view = View(
            [Appearance(45_000, 54_000, 90_000)],
            [Appearance(24_000, 28_800, 48_000)]);

        var tied = Project(view, 16).Events
            .Where(candidate =>
                CompareRational(
                    candidate.MovieStartNumerator,
                    candidate.Denominator,
                    1,
                    2) == 0)
            .ToArray();
        Assert.Equal(2, tied.Length);
        Assert.Equal(Role.Video, tied[0].Role);
        Assert.Equal(Role.Audio, tied[1].Role);
        Assert.True(tied[0].GlobalMergeIndex < tied[1].GlobalMergeIndex);
    }

    [Fact]
    public void TryProject_UsesOverflowSafeRationalOrdering()
    {
        var nearMaximum = Int128.MaxValue - 2;
        var view = View(
            [Appearance(nearMaximum - 1, nearMaximum, 90_000)],
            [Appearance(nearMaximum, nearMaximum + 1, 48_000)],
            new(0, 0, 0, 1_000, null));

        var events = Project(view, 16).Events;
        Assert.Equal(Role.Video, events[^2].Role);
        Assert.Equal(Role.Audio, events[^1].Role);
        AssertOrdered(events);
    }

    [Fact]
    public void TryProject_AcceptsCertifiedZeroStepViewAsEmpty()
    {
        var full = View(
            [Appearance(10, 12, 90_000)],
            [Appearance(20, 22, 48_000)]);
        var zeroTraversal =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(
                    full.Completion.FinalCursorSet,
                    TieBreak.PreferVideo,
                    1);
        var zero = ProjectView(Completion(zeroTraversal));

        var projection = Project(zero, 1);
        Assert.Equal(0, projection.EventCount);
        Assert.Empty(projection.Events);
    }

    [Fact]
    public void TryProject_RejectsMissingInvalidAndExceededBounds()
    {
        AssertResult(Result.MissingStepView, null, 1);
        var view = View(
            [Appearance(10, 12, 90_000)],
            [Appearance(20, 22, 48_000)]);
        AssertResult(Result.InvalidEventLimit, view, 0);
        AssertResult(
            Result.InvalidEventLimit,
            view,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount + 1);
        AssertResult(Result.EventLimitExceeded, view, 5);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView?
            view,
        int maximumEventCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .TryProject(view, maximumEventCount, out var projection));
        Assert.Null(projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
                    view,
                int maximumEventCount)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .TryProject(view, maximumEventCount, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView View(
            IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>
                videoAppearances,
            IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>
                audioAppearances,
            PlaybackIsoBmffMovieHeaderTimingBox? movieTiming = null)
    {
        movieTiming ??=
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        var sequences = new[]
        {
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                7,
                PlaybackIsoBmffHandlerKind.Video,
                movieTiming,
                videoAppearances),
            PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                9,
                PlaybackIsoBmffHandlerKind.Audio,
                movieTiming,
                audioAppearances),
        };
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory.TryCreate(
                sequences,
                out var sequenceSet));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                .Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                sequenceSet,
                new(0, 1),
                out var selection));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCursorSetFactory
                .TryCreate(selection, out var cursorSet));
        var traversal =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalTests
                .Traverse(cursorSet!, TieBreak.PreferVideo, 16);
        return ProjectView(Completion(traversal));
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
            Projection() =>
        Project(
            View(
                [Appearance(10, 12, 90_000)],
                [Appearance(20, 22, 48_000)]),
            16);

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion Completion(
            IPlaybackIsoBmffMovieTrackCodecPacketSequenceMergeTraversalSource
                source)
    {
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionResult
                .Certified,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletionFactory
                .TryCreate(source, out var completion));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion>(
                completion);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
            ProjectView(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceMergeCompletion
                    completion) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepViewTests
            .Project(completion);

    private static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 start,
        Int128 end,
        uint denominator) => new(0, start, end, denominator, true);

    private static void AssertOrdered(
        IReadOnlyList<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent>
            events)
    {
        for (var index = 1; index < events.Count; index++)
        {
            Assert.True(Compare(events[index - 1], events[index]) <= 0);
        }
    }

    private static int Compare(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent left,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent right)
    {
        var result = CompareRational(
            left.MovieStartNumerator,
            left.Denominator,
            right.MovieStartNumerator,
            right.Denominator);
        if (result != 0)
        {
            return result;
        }

        result = CompareRational(
            left.MovieEndNumeratorExclusive,
            left.Denominator,
            right.MovieEndNumeratorExclusive,
            right.Denominator);
        if (result != 0)
        {
            return result;
        }

        result = left.GlobalMergeIndex.CompareTo(right.GlobalMergeIndex);
        return result != 0
            ? result
            : left.AppearanceIndex.CompareTo(right.AppearanceIndex);
    }

    private static int CompareRational(
        Int128 leftNumerator,
        uint leftDenominator,
        Int128 rightNumerator,
        uint rightDenominator) =>
        ((BigInteger)leftNumerator * rightDenominator).CompareTo(
            (BigInteger)rightNumerator * leftDenominator);
}
