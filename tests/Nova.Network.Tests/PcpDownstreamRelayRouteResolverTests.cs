using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamRelayRouteResolverTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531dc8dfc7fb42928ac2c0a626517a87", "N"));

    [Fact]
    public async Task ResolveReturnsLeasedExistingFanoutForRegisteredChannel()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var fanout = new PcpRelayFanout();
        var registration = registry.Register(ChannelId, fanout);
        var resolution = PcpDownstreamRelayRouteResolver.Resolve(
            CreateAcceptedValidation(ChannelId.Value, streamPosition: 25),
            registry);

        Assert.True(resolution.IsResolved);
        Assert.Equal(200, resolution.StatusCode);
        Assert.NotNull(resolution.Request);
        Assert.Equal(ChannelId.Value, resolution.Request.ChannelId);
        Assert.Equal(25, resolution.Request.StreamPosition);
        Assert.Same(fanout, resolution.Fanout);

        var unregisterTask = registration.DisposeAsync().AsTask();
        Assert.False(unregisterTask.IsCompleted);

        resolution.Dispose();
        resolution.Dispose();
        await unregisterTask;
        await registration.DisposeAsync();
    }

    [Fact]
    public async Task ResolveReturnsNotFoundForValidUnregisteredChannel()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var fanout = new PcpRelayFanout();
        await using var registration = registry.Register(ChannelId, fanout);
        var otherChannelId = Guid.ParseExact(
            "102132435465768798a9babbdcddedef",
            "N");

        using var resolution = PcpDownstreamRelayRouteResolver.Resolve(
            CreateAcceptedValidation(otherChannelId, streamPosition: 0),
            registry);

        Assert.False(resolution.IsResolved);
        Assert.Equal(404, resolution.StatusCode);
        Assert.Null(resolution.Request);
        Assert.Null(resolution.Fanout);
    }

    [Fact]
    public async Task ResolvePreservesProtocolRejectionWithoutAcquiringRoute()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var fanout = new PcpRelayFanout();
        await using var registration = registry.Register(ChannelId, fanout);
        var rejected = new PcpDownstreamRelayRequestValidation(
            400,
            Request: null);

        using var resolution = PcpDownstreamRelayRouteResolver.Resolve(
            rejected,
            registry);

        Assert.False(resolution.IsResolved);
        Assert.Equal(400, resolution.StatusCode);
        Assert.Null(resolution.Request);
        Assert.Null(resolution.Fanout);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void ResolveNormalizesInconsistentAcceptedShapeToBadRequest()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        var inconsistent = new PcpDownstreamRelayRequestValidation(
            200,
            Request: null);

        using var resolution = PcpDownstreamRelayRouteResolver.Resolve(
            inconsistent,
            registry);

        Assert.False(resolution.IsResolved);
        Assert.Equal(400, resolution.StatusCode);
        Assert.Null(resolution.Request);
        Assert.Null(resolution.Fanout);
    }

    private static PcpDownstreamRelayRequestValidation CreateAcceptedValidation(
        Guid channelId,
        long streamPosition)
    {
        return new PcpDownstreamRelayRequestValidation(
            200,
            new PcpDownstreamRelayRequest(channelId, streamPosition));
    }
}
