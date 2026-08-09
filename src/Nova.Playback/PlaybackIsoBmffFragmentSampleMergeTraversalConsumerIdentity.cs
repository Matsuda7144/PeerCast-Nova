namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityCreationResult
{
    Created,
    MissingConsumerId,
    InvalidConsumerId,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity(
        Guid consumerId)
    {
        ConsumerId = consumerId;
    }

    public Guid ConsumerId { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityFactory
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityCreationResult
            TryCreate(
                Guid? consumerId,
                out PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity?
                    identity)
    {
        identity = null;
        if (consumerId is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityCreationResult
                    .MissingConsumerId;
        }

        if (consumerId == Guid.Empty)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityCreationResult
                    .InvalidConsumerId;
        }

        identity = new(consumerId.Value);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityCreationResult
                .Created;
    }
}
