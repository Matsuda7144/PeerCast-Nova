using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpPortMappingIntegrationTests
{
    [Fact]
    public async Task PublicListener_BoundPortIsMappedAndListenerStopReleasesDemand()
    {
        await using var test = new NatPortMappingTestHarness();
        await using var fanout = new PcpRelayFanout();
        await using var server = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Any, 0), fanout,
            new ChannelId(Guid.NewGuid()), Guid.NewGuid(), portMapping: test.Manager);
        var run = server.RunAsync();
        var mapping = test.Manager.ReconcileAsync();
        var map = await test.Router.NextAsync();
        Assert.Equal(server.LocalEndPoint.Port, map.Request.Identity.InternalPort);
        Assert.NotEqual(0, map.Request.Identity.InternalPort);
        map.Succeed();
        await mapping;
        await server.StopAsync();
        await run;
        var cleanup = test.Manager.ReconcileAsync();
        var unmap = await test.Router.NextAsync();
        Assert.Equal(NatMappingOperation.Unmap, unmap.Request.Operation);
        unmap.Succeed();
        await cleanup;
        Assert.Equal(NatMappingStatus.Unmapped, Assert.Single(test.Manager.Snapshot).Status);
    }

    [Fact]
    public async Task LoopbackListener_NeverCreatesPublicDemand()
    {
        await using var test = new NatPortMappingTestHarness();
        await using var fanout = new PcpRelayFanout();
        await using var server = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, 0), fanout,
            new ChannelId(Guid.NewGuid()), Guid.NewGuid(), portMapping: test.Manager);
        var run = server.RunAsync();
        await test.Manager.ReconcileAsync();
        Assert.Equal(0, test.Router.Count);
        await server.StopAsync();
        await run;
        Assert.Empty(test.Manager.Snapshot);
    }
}
