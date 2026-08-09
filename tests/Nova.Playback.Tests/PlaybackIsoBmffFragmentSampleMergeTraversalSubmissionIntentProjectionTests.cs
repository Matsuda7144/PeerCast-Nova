using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionTests
{
    [Fact]
    public void Project_RetainsExactBatchEntriesAndOpaqueIdentities()
    {
        var source = Batch();

        var projection = Project(source);

        Assert.Same(source, projection.Source);
        Assert.Equal(source.EntryCount, projection.IntentCount);
        for (var index = 0; index < projection.IntentCount; index++)
        {
            var intent = projection.Intents[index];
            var entry = source.Entries[index];
            Assert.Equal(index, intent.SubmissionIndex);
            Assert.Same(entry, intent.Entry);
            Assert.Same(entry.Binding.Request, intent.Request);
            Assert.Equal(entry.Binding.Consumer.ConsumerId, intent.ConsumerId);
            Assert.Equal(entry.BatchIdentity.BatchId, intent.BatchId);
        }
    }

    [Fact]
    public void Project_AcceptsEmptyBatch()
    {
        Assert.Empty(Project(Batch(-91, 1)).Intents);
    }

    [Fact]
    public void Project_RejectsMissingAndReorderedEntries()
    {
        AssertResult(Result.InvalidInput, null);
        var source = Batch();
        var reordered = new
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection(
                source.Source,
                source.BatchIdentity,
                Array.AsReadOnly(source.Entries.Reverse().ToArray()));
        AssertResult(Result.SourceMismatch, reordered);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection
            Projection() => Project(Batch());

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
                    source)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection>(
                projection);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
            Batch(int observedTick = -30, int maximumCount = 2) =>
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionTests
            .Projection(observedTick, maximumCount);

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection?
            source)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjector
                .Project(source, out var projection));
        Assert.Null(projection);
    }
}
