using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelectionTests
{
    [Fact]
    public void Select_RetainsEligibleSourceOrderWithinCallerLimit()
    {
        var source = Eligibility(1_000, Policy.ReadyOrExpired);

        var selection = Select(source, 1);
        Assert.Same(source, selection.Source);
        Assert.Equal(1, selection.MaximumCandidateCount);
        Assert.Equal(source.Entries.Count, selection.EligibleCount);
        Assert.True(selection.HasMoreEligible);
        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal(0, candidate.SourceIndex);
        Assert.Same(source.Entries[0], candidate.Eligibility);
        Assert.Same(
            source.Entries[0].Observation.Deadline.Time.Time.Time.Time.Event,
            candidate.Event);
        Assert.Same(
            source.Entries[0].Observation.Deadline.Time.Time.Time.Time
                .Acknowledgement,
            candidate.Acknowledgement);
    }

    [Fact]
    public void Select_ReturnsEmptyWhenNoEventIsEligible()
    {
        var selection = Select(Eligibility(99, Policy.ReadyOnly), 10);

        Assert.Empty(selection.Candidates);
        Assert.Equal(0, selection.EligibleCount);
        Assert.False(selection.HasMoreEligible);
    }

    [Fact]
    public void Select_RejectsMissingInvalidLimitAndSourceOrder()
    {
        AssertResult(Result.MissingEligibilityProjection, null, 1);
        var source = Eligibility(100, Policy.ReadyOnly);
        AssertResult(Result.InvalidMaximumCandidateCount, source, 0);
        var mismatched = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection(
                source.Source,
                source.Policy,
                Array.AsReadOnly(source.Entries.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, mismatched, 1);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection?
            source,
        int maximumCandidateCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelector
                .Select(source, maximumCandidateCount, out var selection));
        Assert.Null(selection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection
            Select(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
                    source,
                int maximumCandidateCount)
    {
        Assert.Equal(
            Result.Selected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelector
                .Select(source, maximumCandidateCount, out var selection));
        return Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchCandidateSelection>(
                selection);
    }

    internal static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityProjection
            Eligibility(int observationTick, Policy policy) =>
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityTests
            .Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchEligibilityTests
                    .Observation(observationTick),
                policy);
}
