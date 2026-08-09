using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionTests
{
    [Fact]
    public void Project_RetainsExactBindingsAndBatchIdentity()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingTests
                .Projection();
        var batchIdentity =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityTests
                .Identity(Guid.NewGuid());

        var projection = Project(source, batchIdentity);

        Assert.Same(source, projection.Source);
        Assert.Same(batchIdentity, projection.BatchIdentity);
        Assert.Equal(source.BindingCount, projection.EntryCount);
        for (var index = 0; index < projection.EntryCount; index++)
        {
            Assert.Equal(index, projection.Entries[index].BatchEntryIndex);
            Assert.Same(source.Bindings[index], projection.Entries[index].Binding);
            Assert.Same(batchIdentity,
                projection.Entries[index].BatchIdentity);
        }
    }

    [Fact]
    public void Project_AcceptsEmptyBindingSet()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingTests
                .Projection(-91, 1);
        var batchIdentity =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityTests
                .Identity(Guid.NewGuid());

        Assert.Empty(Project(source, batchIdentity).Entries);
    }

    [Fact]
    public void Project_RejectsMissingInputs()
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingTests
                .Projection();
        var batchIdentity =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityTests
                .Identity(Guid.NewGuid());
        AssertResult(
            Result.MissingConsumerBindingProjection,
            null,
            batchIdentity);
        AssertResult(Result.MissingBatchIdentity, source, null);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity
                    batchIdentity)
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjector
                .Project(source, batchIdentity, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
            Projection(int observedTick = -30, int maximumCount = 2)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingTests
                .Projection(observedTick, maximumCount);
        var batchIdentity =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityTests
                .Identity(Guid.NewGuid());
        return Project(source, batchIdentity);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection?
            source,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity?
            batchIdentity)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjector
                .Project(source, batchIdentity, out var projection));
        Assert.Null(projection);
    }
}
