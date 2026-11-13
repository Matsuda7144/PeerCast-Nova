using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpChannelSessionRedirectTests
{
    [Fact]
    public async Task SmartRecovery_Http503HostChainDoesNotResetEightAttemptBudget()
    {
        using var clock = new AnnounceManualTimeProvider();
        await using var transport = new FakeTransport();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var endpoints = Enumerable.Range(0, 8).Select(i => Endpoint("1.1.1.1", 7144 + i)).ToArray();
        var listeners = endpoints.Select(transport.AddRedirect).ToArray();
        var hosts = endpoints.Select(e => PcpReferralPlanner.CreateHost(PcpPeerDirectoryTests.Host(e.Port) with
        { ChannelId = ChannelGuid, GlobalEndpoint = e })).ToArray();
        var peers = new List<Task> { RunRelayFullPeerAsync(transport.OriginalListener, "original.example:7144", 0, [hosts[0]], timeout.Token) };
        for (var i = 0; i < 7; i++) peers.Add(RunRelayFullPeerAsync(listeners[i], $"1.1.1.1:{7144 + i}", 0, [hosts[i + 1]], timeout.Token));
        await using var session = new PcpChannelSession(transport, CreateTracker(), new CollectingPacketConsumer(),
            new() { SmartRelay = new() { TimeProvider = clock, JitterSample = () => .5 } });
        var run = session.RunAsync("original.example", 7144, timeout.Token);
        await PcpSmartRelayRecoveryTests.Until(() => session.SmartRelaySnapshot is { State: PcpSmartRelayState.Backoff, AttemptCount: 8 });
        Assert.Equal(endpoints.Take(7), transport.LogicalRedirectEndpoints);
        Assert.Equal(8, session.SmartRelaySnapshot.WaveAttemptCount);
        await session.StopAsync(); await run; await Task.WhenAll(peers);
    }

    [Fact]
    public async Task SmartRecovery_Http503ReferralReachesAcceptedAlternativeWithoutChangingWireIdentity()
    {
        await using var transport = new FakeTransport();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var endpoint = Endpoint("1.1.1.1", 7144);
        var listener = transport.AddRedirect(endpoint);
        var host = PcpReferralPlanner.CreateHost(PcpPeerDirectoryTests.Host() with { ChannelId = ChannelGuid, GlobalEndpoint = endpoint });
        var refused = RunRelayFullPeerAsync(transport.OriginalListener, "original.example:7144", 0, [host], timeout.Token);
        var accepted = RunAcceptedPeerAsync(listener, "1.1.1.1:7144", 0, [CreatePacketAtom(100, [7]), CreateQuitAtom(1000)], timeout.Token);
        var tracker = CreateTracker(); var consumer = new CollectingPacketConsumer();
        await using var session = new PcpChannelSession(transport, tracker, consumer, new() { SmartRelay = new() });
        var result = await session.RunAsync("original.example", 7144, timeout.Token);
        await refused; await accepted;
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(PcpChannelSessionEndReason.QuitReceived, result.EndReason);
        Assert.Equal(SessionId, tracker.Snapshot.SessionId);
        Assert.Single(consumer.Packets);
        Assert.Equal([endpoint], transport.LogicalRedirectEndpoints);
    }

    [Theory]
    [InlineData(1005U)]
    [InlineData(1007U)]
    [InlineData(1000U)]
    public async Task SmartRecovery_ExplicitTerminalQuitNeverRetries(uint code)
    {
        await using var transport = new FakeTransport();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var peer = RunAcceptedPeerAsync(transport.OriginalListener, "original.example:7144", 0, [CreateQuitAtom(code)], timeout.Token);
        await using var session = new PcpChannelSession(transport, CreateTracker(), new CollectingPacketConsumer(),
            new() { SmartRelay = new() { Mode = PcpSmartRelayMode.Persistent } });
        var result = await session.RunAsync("original.example", 7144, timeout.Token);
        await peer;
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(code, result.Quit?.Code);
        Assert.Empty(transport.RedirectRequests);
    }
}
