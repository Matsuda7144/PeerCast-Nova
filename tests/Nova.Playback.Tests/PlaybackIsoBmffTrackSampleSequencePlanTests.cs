using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackSampleSequencePlanCreationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackSampleSequencePlanTests
{
    [Fact]
    public void TryCreate_OwnsCallerBoundedContiguousSampleNumbers()
    {
        var reconciliation = Reconciliation(4);

        Assert.Equal(
            Result.Created,
            PlaybackIsoBmffTrackSampleSequencePlanFactory.TryCreate(
                reconciliation,
                2,
                2,
                out var plan));
        var created = Assert.IsType<PlaybackIsoBmffTrackSampleSequencePlan>(
            plan);
        Assert.Same(reconciliation, created.Reconciliation);
        Assert.Equal(2UL, created.FirstSampleNumber);
        Assert.Equal(2, created.MaximumSampleCount);
        Assert.Equal(3UL, created.RemainingSampleCount);
        Assert.True(created.IsTruncated);
        Assert.Collection(
            created.Entries,
            first =>
            {
                Assert.Equal(0, first.SequenceIndex);
                Assert.Equal(2UL, first.SampleNumber);
            },
            second =>
            {
                Assert.Equal(1, second.SequenceIndex);
                Assert.Equal(3UL, second.SampleNumber);
            });
    }

    [Fact]
    public void TryCreate_OwnsExactTailWithoutTruncation()
    {
        Assert.Equal(
            Result.Created,
            PlaybackIsoBmffTrackSampleSequencePlanFactory.TryCreate(
                Reconciliation(4),
                3,
                4,
                out var plan));
        var created = Assert.IsType<PlaybackIsoBmffTrackSampleSequencePlan>(
            plan);
        Assert.False(created.IsTruncated);
        Assert.Equal([3UL, 4UL],
            created.Entries.Select(entry => entry.SampleNumber));
    }

    [Fact]
    public void TryCreate_RejectsMissingAndEmptyReconciliation()
    {
        AssertResult(Result.MissingReconciliation, null, 1, 1);
        AssertResult(Result.EmptySampleTable, Reconciliation(0), 1, 1);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(1, 0)]
    public void TryCreate_RejectsInvalidCallerBounds(
        ulong firstSampleNumber,
        int maximumSampleCount)
    {
        AssertResult(
            firstSampleNumber is 0 or 5
                ? Result.InvalidFirstSampleNumber
                : Result.InvalidMaximumSampleCount,
            Reconciliation(4),
            firstSampleNumber,
            maximumSampleCount);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffReconciledSampleCount? reconciliation,
        ulong firstSampleNumber,
        int maximumSampleCount)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackSampleSequencePlanFactory.TryCreate(
                reconciliation,
                firstSampleNumber,
                maximumSampleCount,
                out var plan));
        Assert.Null(plan);
    }

    private static PlaybackIsoBmffReconciledSampleCount Reconciliation(
        uint sampleCount) =>
        new(sampleCount, sampleCount, PlaybackIsoBmffSampleSizeTableKind.Standard);
}
