namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionTests
{
    [Fact]
    public void Select_RetainsEligibleSourceOrderWithinCallerLimit()
    {
        var source = Eligibility(
            1_000,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                .ReadyOrExpired);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                .Selected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelector
                .Select(source, 1, out var selection));
        var selected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection>(
                selection);
        Assert.Same(source, selected.Source);
        Assert.Equal(1, selected.MaximumCandidateCount);
        Assert.Equal(2, selected.EligibleCount);
        Assert.True(selected.HasMoreEligible);
        var candidate = Assert.Single(selected.Candidates);
        Assert.Equal(0, candidate.SourceIndex);
        Assert.Same(source.Entries[0], candidate.Eligibility);
        Assert.Same(
            source.Entries[0].Observation.Deadline.Time.Time.Time.Entry,
            candidate.Entry);
    }

    [Fact]
    public void Select_ReturnsEmptySnapshotWhenNoEntryIsEligible()
    {
        var source = Eligibility(
            99,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                .ReadyOnly);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                .Selected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelector
                .Select(source, 10, out var selection));
        var selected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelection>(
                selection);
        Assert.Empty(selected.Candidates);
        Assert.Equal(0, selected.EligibleCount);
        Assert.False(selected.HasMoreEligible);
    }

    [Fact]
    public void Select_RejectsMissingSourceAndInvalidLimit()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                .MissingEligibilityProjection,
            null,
            1);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                .InvalidMaximumCandidateCount,
            Eligibility(
                100,
                PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                    .ReadyOnly),
            0);
    }

    [Fact]
    public void Select_RejectsMismatchedEligibilityEntries()
    {
        var source = Eligibility(
            100,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                .ReadyOnly);
        var entries = source.Entries.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection(
                source.Source,
                source.Policy,
                Array.AsReadOnly(entries));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
                .SourceMismatch,
            mismatched,
            1);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelectionResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection?
            source,
        int maximumCandidateCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchCandidateSelector
                .Select(source, maximumCandidateCount, out var selection));
        Assert.Null(selection);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection
        Eligibility(
            int observationTick,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityPolicy
                policy)
    {
        var observation =
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityTests
                .Observation(observationTick);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjector
                .Project(observation, policy, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchEligibilityProjection>(
                projection);
    }
}
