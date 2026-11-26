using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishingLoopbackAndViewingShareCountsBudgetAndActualCleanup(bool pacing)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        await using var state = await PublicChannelTestFixture.CreateAsync(new()
        {
            MaxRelaysTotal = 2,
            MaxRelaysPerBroadcastChannel = 1,
            MaxRelaysPerViewingChannel = 1,
            TotalUpstreamIpv4Kbps = 1000,
        }, pacing: pacing);
        var options = new ObsPublishChannelOptions { Ingest = new() { Port = 0 }, BitrateKbps = 500 };
        await using var host = new ObsPublishChannelHost(options, useLoopbackAdmission: true,
            registerRelay: (id, session, fanout) => state.State.DownstreamRelays.Register(id, session,
                RelayChannelKind.Broadcast, fanout, () => options.BitrateKbps));
        host.Start(token);
        await UntilAsync(() => host.PublishingStatus.ListenerReady, token);
        var policy = state.State.NetworkPolicies.Relays;
        Assert.Equal(0, policy.ReservedCount); // The internal HTTP/media child is not a PCP relay.
        Assert.False(policy.Traffic.Snapshot.HasObservation);
        await using var publishing = await WirePeer.Connect(host.RelayEndPoint.Port, host.ChannelId, token);
        Assert.Equal(200, publishing.Handshake.HttpStatusCode);
        // The immutable publishing CHAN metadata follows OK and must also be read.
        Assert.Equal(PcpKnownAtomNames.Channel, (await publishing.ReadAtom(token)).Name);
        await using var broadcastFull = await WirePeer.Connect(host.RelayEndPoint.Port, host.ChannelId, token);
        Assert.Equal(503, broadcastFull.Handshake.HttpStatusCode);
        Assert.Equal(1003U, broadcastFull.Handshake.TerminalCode);
        Assert.Equal(1, policy.ReservedCount);
        await using var viewing = await state.ViewingAsync(token, bitrateKbps: 500);
        await using var child = await WirePeer.Connect(viewing.Owner.LocalEndPoint.Port, viewing.Owner.ChannelId, token);
        Assert.Equal(200, child.Handshake.HttpStatusCode);
        Assert.Equal(PcpKnownAtomNames.Channel, (await child.ReadAtom(token)).Name);
        Assert.Equal(2, policy.ReservedCount);
        Assert.Equal(1000, policy.Snapshot.ReservedIpv4Kbps);
        await using var other = await state.ViewingAsync(token, bitrateKbps: 1);
        await using var full = await WirePeer.Connect(other.Owner.LocalEndPoint.Port, other.Owner.ChannelId, token);
        Assert.Equal(503, full.Handshake.HttpStatusCode);
        Assert.Equal(1003U, full.Handshake.TerminalCode);
        await UntilAsync(() => policy.Traffic.Snapshot.CompletedWriteBytes ==
            publishing.ReceivedBytes + broadcastFull.ReceivedBytes + child.ReceivedBytes + full.ReceivedBytes, token);
        var completed = policy.Traffic.Snapshot.CompletedWriteBytes;
        Assert.True(completed > 0);
        await publishing.Quit(token);
        await UntilAsync(() => policy.ReservedCount == 1, token);
        await host.DisposeAsync();
        Assert.True((await host.Completion).ShutdownCompleted);
        Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
        await child.Quit(token);
        await UntilAsync(() => policy.ReservedCount == 0, token);
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
        Assert.True(policy.Traffic.Snapshot.CompletedWriteBytes >= completed);
    }

    [Fact]
    public async Task ApplicationPublishingFactoryRegistersTheLoopbackOwnerWithoutEnablingPublicModes()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var test = await PublicChannelTestFixture.CreateAsync(new() { TotalUpstreamIpv4Kbps = 0 });
        await using var runtime = new SystemNovaObsPublishingRuntimeFactory(test.State).Create(
            new ObsPublishChannelOptions { Ingest = new() { Port = 0 } });
        runtime.Start(timeout.Token);
        await UntilAsync(() => runtime.Snapshot.ListenerReady, timeout.Token);
        var snapshot = runtime.Snapshot;
        Assert.Throws<InvalidOperationException>(() => test.State.NetworkPolicies.Relays.OpenChannel(
            snapshot.ChannelId, snapshot.SessionId, RelayChannelKind.Broadcast));
        Assert.False(snapshot.LanRelay?.Enabled == true);
        Assert.Null(snapshot.WanRelay);
        Assert.Equal(0, test.State.NetworkPolicies.Relays.ReservedCount);
        await runtime.DisposeAsync();
        Assert.True((await runtime.Completion).ShutdownCompleted);
    }

    [Theory]
    [InlineData(null, null, 200)]
    [InlineData(0, null, 503)]
    [InlineData(1000, null, 503)]
    [InlineData(499, 500, 503)]
    [InlineData(500, 500, 200)]
    public async Task StartupBandwidthGatesActualPcpWhilePreservingHeadDataAndFullResponse(
        int? budget, int? bitrate, int expectedStatus)
    {
        await using var test = await Fixture.Create(ipv4Kbps: budget, pacing: true);
        await using var channel = await test.Base.ViewingAsync(test.Token, bitrateKbps: bitrate);
        await channel.Owner.PublicReady;
        await using var peer = await WirePeer.Connect(test.Port, channel.Owner.ChannelId, test.Token);
        Assert.Equal(expectedStatus, peer.Handshake.HttpStatusCode);
        var policy = test.Base.State.NetworkPolicies.Relays;
        if (expectedStatus == 503)
        {
            Assert.Equal(1003U, peer.Handshake.TerminalCode);
            Assert.NotNull(peer.Handshake.RemotePeer);
            Assert.Empty(peer.Handshake.RelayHostCandidates);
            Assert.Equal(0, policy.Snapshot.ReservedRelays);
            Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
            Assert.False(channel.Owner.CanReserve);
            return;
        }
        Assert.Equal(1, policy.Snapshot.ReservedRelays);
        Assert.Equal(bitrate, policy.Snapshot.ReservedIpv4Kbps);
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x64616568, 0, [11, 12]), test.Token);
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x61746164, 100, [21, 22]), test.Token);
        Assert.Equal(new byte[] { 11, 12 }, await peer.Payload(test.Token));
        Assert.Equal(new byte[] { 21, 22 }, await peer.Payload(test.Token));
        await peer.Quit(test.Token);
        await UntilAsync(() => policy.ReservedCount == 0, test.Token);
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
    }

    [Fact]
    public async Task LocalAndSharedPublicUseOneBandwidthBudgetAndExistingHostReferrals()
    {
        await using var test = await Fixture.Create(ipv4Kbps: 1000);
        await using var first = await test.Base.ViewingAsync(test.Token, bitrateKbps: 500);
        await using var second = await test.Base.ViewingAsync(test.Token, bitrateKbps: 1);
        await first.Owner.PublicReady;
        await second.Owner.PublicReady;
        var policy = test.Base.State.NetworkPolicies.Relays;
        await using var local = await WirePeer.Connect(first.Owner.LocalEndPoint.Port, first.Owner.ChannelId, test.Token);
        await using var shared = await WirePeer.Connect(test.Port, first.Owner.ChannelId, test.Token);
        Assert.Equal(200, local.Handshake.HttpStatusCode);
        Assert.Equal(200, shared.Handshake.HttpStatusCode);
        Assert.Equal(2, policy.Snapshot.ReservedRelays);
        Assert.Equal(1000, policy.Snapshot.ReservedIpv4Kbps);
        Assert.False(first.Owner.CanReserve);
        Assert.False(second.Owner.CanReserve);
        Assert.Contains("IPv4帯域予約 1000/1000", first.Owner.CapacityStatusText);
        Assert.Contains("下流PCP書込完了", first.Owner.CapacityStatusText);
        var alternative = Host(second.Owner, Guid.NewGuid(), "8.8.8.8", 7145);
        second.Owner.ViewingRuntime!.DownstreamServer.Peers.Observe(alternative, new());
        await using var full = await WirePeer.Connect(test.Port, second.Owner.ChannelId, test.Token);
        Assert.Equal(503, full.Handshake.HttpStatusCode);
        Assert.Equal(1003U, full.Handshake.TerminalCode);
        Assert.Equal(alternative.PeerSessionId, Assert.Single(full.Handshake.RelayHostCandidates).PeerSessionId);
        Assert.Equal(2, policy.ReservedCount);
        await local.Quit(test.Token);
        await UntilAsync(() => policy.ReservedCount == 1, test.Token);
        Assert.Equal(500, policy.Snapshot.ReservedIpv4Kbps);
        await using var afterRelease = await WirePeer.Connect(test.Port, second.Owner.ChannelId, test.Token);
        Assert.Equal(200, afterRelease.Handshake.HttpStatusCode);
        Assert.Equal(501, policy.Snapshot.ReservedIpv4Kbps);
        await shared.Quit(test.Token);
        await afterRelease.Quit(test.Token);
        await UntilAsync(() => policy.ReservedCount == 0, test.Token);
        Assert.Equal(0, policy.Snapshot.ReservedIpv4Kbps);
    }

    [Fact]
    public async Task CapacityDisplayAndAdmissionUpdateImmediatelyOnSave()
    {
        await using var test = await Fixture.Create(wan: false, total: 1, ipv4Kbps: 500);
        await using var activation = new GenesisGuiNextRelayActivation(test.Base.Channels);
        Assert.Contains("リレー予約 0/1", activation.Snapshot.Text);
        Assert.Contains("IPv4帯域予約 0/500", activation.Snapshot.Text);
        await using var channel = await test.Base.ViewingAsync(test.Token, bitrateKbps: 500);
        var saved = GenesisGuiNextConnectionInput.From(test.Base.State.Connection.Saved) with
        { MaxRelaysTotal = "0", TotalUpstreamIpv4Kbps = "0" };
        Assert.True((await test.Base.State.Connection.SaveAsync(saved)).Saved);
        Assert.False(test.Base.State.Connection.HasPendingRestart);
        Assert.Contains("リレー予約 0/0", activation.Snapshot.Text);
        Assert.Contains("IPv4帯域予約 0/0", activation.Snapshot.Text);
        await using var peer = await WirePeer.Connect(channel.Owner.LocalEndPoint.Port, channel.Owner.ChannelId, test.Token);
        Assert.Equal(503, peer.Handshake.HttpStatusCode);
        Assert.Contains("新規予約不可", channel.Owner.CapacityStatusText);
    }
}
