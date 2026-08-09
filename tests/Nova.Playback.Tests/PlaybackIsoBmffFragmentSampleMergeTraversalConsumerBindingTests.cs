using Combination = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalEligibilityCombinationPolicy;
using Policy = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalDeadlineEligibilityPolicy;
using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingTests
{
    [Fact]
    public void Bind_RetainsExactRequestsAndOpaqueConsumerIdentity()
    {
        var source = RequestSource();
        var consumer =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityTests
                .Identity(Guid.NewGuid());

        var projection = Bind(source, consumer);

        Assert.Same(source, projection.Source);
        Assert.Same(consumer, projection.Consumer);
        Assert.Equal(source.Requests.RequestCount, projection.BindingCount);
        for (var index = 0; index < projection.BindingCount; index++)
        {
            Assert.Same(
                source.Requests.Requests[index],
                projection.Bindings[index].Request);
            Assert.Same(consumer, projection.Bindings[index].Consumer);
        }
    }

    [Fact]
    public void Bind_AcceptsEmptyRequestSet()
    {
        var source = RequestSource(-91, 1);
        var consumer =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityTests
                .Identity(Guid.NewGuid());

        Assert.Empty(Bind(source, consumer).Bindings);
    }

    [Fact]
    public void Bind_RejectsMissingAndForeignSources()
    {
        var source = RequestSource();
        var consumer =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityTests
                .Identity(Guid.NewGuid());
        AssertResult(Result.MissingRequestSourceProjection, null, consumer);
        AssertResult(Result.MissingConsumerIdentity, source, null);

        var foreign = RequestSource();
        var mismatched = new
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection(
                source.Source,
                foreign.Requests);
        AssertResult(Result.SourceMismatch, mismatched, consumer);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection
            Bind(
                PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity
                    consumer)
    {
        Assert.Equal(
            Result.Bound,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinder.Bind(
                source,
                consumer,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection>(
                projection);
    }

    internal static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection
            Projection(int observedTick = -30, int maximumCount = 2)
    {
        var consumer =
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentityTests
                .Identity(Guid.NewGuid());
        return Bind(RequestSource(observedTick, maximumCount), consumer);
    }

    private static
        PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection
            RequestSource(int observedTick = -30, int maximumCount = 2)
    {
        var source =
            PlaybackIsoBmffFragmentSampleMergeTraversalEligibleSelectionSourceProjectionTests
                .Projection(
                    observedTick,
                    -100,
                    Policy.ReadyOrExpired,
                    Policy.ReadyOrExpired,
                    Combination.RequireBoth,
                    maximumCount);
        Assert.Equal(
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjectionResult
                .Projected,
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjector
                .Project(source, out var projection));
        return Assert.IsType<
            PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection>(
                projection);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffFragmentSampleMergeTraversalRequestSourceProjection?
            source,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerIdentity? consumer)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinder.Bind(
                source,
                consumer,
                out var projection));
        Assert.Null(projection);
    }
}
