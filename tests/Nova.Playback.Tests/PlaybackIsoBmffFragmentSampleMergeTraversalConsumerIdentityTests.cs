using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityCreationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityTests
{
    [Fact]
    public void TryCreate_OwnsCallerExplicitOpaqueIdentity()
    {
        var consumerId = Guid.NewGuid();

        var identity = Identity(consumerId);

        Assert.Equal(consumerId, identity.ConsumerId);
    }

    [Fact]
    public void TryCreate_RejectsMissingAndEmptyIdentity()
    {
        AssertResult(Result.MissingConsumerId, null);
        AssertResult(Result.InvalidConsumerId, Guid.Empty);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity Identity(
            Guid consumerId)
    {
        Assert.Equal(
            Result.Created,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityFactory
                .TryCreate(consumerId, out var identity));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity>(
                identity);
    }

    private static void AssertResult(Result expected, Guid? consumerId)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityFactory
                .TryCreate(consumerId, out var identity));
        Assert.Null(identity);
    }
}
