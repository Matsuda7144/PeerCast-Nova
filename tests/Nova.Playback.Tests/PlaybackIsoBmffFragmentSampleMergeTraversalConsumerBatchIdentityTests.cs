using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityCreationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityTests
{
    [Fact]
    public void TryCreate_OwnsCallerExplicitOpaqueBatchIdentity()
    {
        var batchId = Guid.NewGuid();

        var identity = Identity(batchId);

        Assert.Equal(batchId, identity.BatchId);
    }

    [Fact]
    public void TryCreate_RejectsMissingAndEmptyIdentity()
    {
        AssertResult(Result.MissingBatchId, null);
        AssertResult(Result.InvalidBatchId, Guid.Empty);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity
            Identity(Guid batchId)
    {
        Assert.Equal(
            Result.Created,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityFactory
                .TryCreate(batchId, out var identity));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity>(
                identity);
    }

    private static void AssertResult(Result expected, Guid? batchId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityFactory
                .TryCreate(batchId, out var identity));
        Assert.Null(identity);
    }
}
