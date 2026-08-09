namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityCreationResult
{
    Created,
    MissingBatchId,
    InvalidBatchId,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity(
            Guid batchId)
    {
        BatchId = batchId;
    }

    public Guid BatchId { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityFactory
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityCreationResult
            TryCreate(
                Guid? batchId,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity?
                        identity)
    {
        identity = null;
        if (batchId is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityCreationResult
                    .MissingBatchId;
        }

        if (batchId == Guid.Empty)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityCreationResult
                    .InvalidBatchId;
        }

        identity = new(batchId.Value);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentityCreationResult
                .Created;
    }
}
