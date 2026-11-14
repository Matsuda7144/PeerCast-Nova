using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ManualDisconnectOnlyAWhileBAndReconnectedCKeepReceiving(bool shared, bool broadcast)
    {
        await using var test = await Fixture.Create(wan: shared, ipv4Kbps: broadcast ? null : 1000, pacing: true);
        await using var channel = broadcast ? await test.Base.BroadcastAsync(test.Token) : await test.Base.ViewingAsync(test.Token, bitrateKbps: 500);
        await channel.Owner.PublicReady;
        var registry = test.Base.State.DownstreamRelays;
        var endpoint = shared ? test.Port : channel.Owner.LocalEndPoint.Port;
        var sid = Guid.NewGuid();
        await using var a = await WirePeer.Connect(endpoint, channel.Owner.ChannelId, test.Token, sid);
        await UntilAsync(() => Connected().Length == 1, test.Token);
        var old = Connected()[0];
        var selection = GenesisRelayTreeSelection.Key(Tree(), old);
        var target = GenesisRelayTreeSelection.DisconnectTarget(Tree(), selection)!;
        await using var b = await WirePeer.Connect(endpoint, channel.Owner.ChannelId, test.Token);
        await UntilAsync(() => Connected().Length == 2, test.Token);
        var survivor = Connected().Single(n => n.ConnectionId != old.ConnectionId);
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x64616568, 0, [1, 2]), test.Token);
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x61746164, 2, [3, 4]), test.Token);
        foreach (var peer in new[] { a, b })
        {
            Assert.Equal(new byte[] { 1, 2 }, await peer.Payload(test.Token));
            Assert.Equal(new byte[] { 3, 4 }, await peer.Payload(test.Token));
        }
        var wrong = registry.RequestDisconnect(target with { ChildInstanceId = Guid.NewGuid() });
        Assert.Equal(PcpDownstreamDisconnectOutcome.StaleSelection, wrong.Outcome);
        Assert.Equal(2, registry.ActiveRelayCount);
        var request = registry.RequestDisconnect(target);
        Assert.Equal(PcpDownstreamDisconnectOutcome.Accepted, request.Outcome);
        Assert.NotNull(request.Completion);
        var duplicate = registry.RequestDisconnect(target);
        Assert.Contains(duplicate.Outcome, new[] { PcpDownstreamDisconnectOutcome.AlreadyStopping, PcpDownstreamDisconnectOutcome.AlreadyDisconnected });
        Assert.Equal(PcpDownstreamDisconnectOutcome.Completed, await request.Completion.WaitAsync(test.Token));
        Assert.Equal(1, registry.ActiveRelayCount);
        Assert.Equal(1, Tree().Capacity!.ChannelReservedRelays);
        Assert.Equal(survivor.ConnectionId, Assert.Single(Connected()).ConnectionId);
        Assert.DoesNotContain(Tree().Nodes, n => n.ConnectionId == old.ConnectionId);
        Assert.False(registry.ContainsConnection(target.ConnectionId));
        Assert.True(registry.ContainsConnection(survivor.ConnectionId!.Value));
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x61746164, 4, [5, 6]), test.Token);
        Assert.Equal(new byte[] { 5, 6 }, await b.Payload(test.Token));
        await using var c = await WirePeer.Connect(endpoint, channel.Owner.ChannelId, test.Token, sid);
        await UntilAsync(() => Connected().Length == 2, test.Token);
        Assert.Null(GenesisRelayTreeSelection.DisconnectTarget(Tree(), selection));
        Assert.Equal(PcpDownstreamDisconnectOutcome.AlreadyDisconnected, registry.RequestDisconnect(target).Outcome);
        Assert.All(Connected(), n => Assert.NotEqual(target.ChildInstanceId, n.ChildInstanceId));
        var reconnected = Connected().Single(n => n.ConnectionId != survivor.ConnectionId);
        Assert.Equal(PcpDownstreamDisconnectOutcome.StaleSelection,
            registry.RequestDisconnect(target with { ConnectionId = reconnected.ConnectionId!.Value }).Outcome);
        if (broadcast)
        {
            // Generic producer fan-outs do not enable viewing's HEAD cache.
            await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x64616568, 6, [9, 10]), test.Token);
            Assert.Equal(new byte[] { 9, 10 }, await b.Payload(test.Token));
            Assert.Equal(new byte[] { 9, 10 }, await c.Payload(test.Token));
        }
        else Assert.Equal(new byte[] { 1, 2 }, await c.Payload(test.Token));
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x61746164, 6, [7, 8]), test.Token);
        Assert.Equal(new byte[] { 7, 8 }, await b.Payload(test.Token));
        Assert.Equal(new byte[] { 7, 8 }, await c.Payload(test.Token));
        Assert.Equal(0, channel.Owner.DownstreamSnapshot.WriteTimedOutCount);
        Assert.Equal(0, channel.Owner.DownstreamSnapshot.InternalFailureCount);
        if (channel.Owner.ViewingRuntime is { } runtime)
        {
            Assert.Equal(2, runtime.DownstreamFanout.ActiveChildCount);
            Assert.DoesNotContain(((IPcpLocalRelayTreeSource)runtime).ObserveLocalRelayTree().Connections,
                n => n.ConnectionId == target.ConnectionId);
        }
        await b.Quit(test.Token); await c.Quit(test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0 && channel.Owner.DownstreamSnapshot.ActiveConnectionCount == 0, test.Token);
        Assert.Equal(0, Tree().Capacity!.ChannelReservedRelays);
        Assert.Equal(0, channel.Owner.DownstreamSnapshot.AdmissionCleanupPendingCount);
        Assert.Equal(0, channel.Owner.DownstreamSnapshot.AdmissionCleanupFailedCount);
        Assert.True(Tree().Traffic!.CompletedWriteBytes > 0);

        GenesisRelayTreeSnapshot Tree() => registry.ReadRelayTree(channel.Owner.ChannelId);
        GenesisRelayTreeNode[] Connected() => Tree().Nodes.Where(n => n.Kind == GenesisRelayTreeNodeKind.Downstream &&
            n.Phase == PcpRelayConnectionPhase.Connected).ToArray();
    }

    [Fact]
    public async Task OldOwnerCommandCannotCutReplacementAndRecoveryAndStopRejectNewCommands()
    {
        await using var test = await Fixture.Create();
        await using var old = await test.Base.ViewingAsync(test.Token);
        await old.Owner.PublicReady;
        var registry = test.Base.State.DownstreamRelays;
        await using var a = await WirePeer.Connect(old.Owner.LocalEndPoint.Port, old.Owner.ChannelId, test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 1, test.Token);
        var tree = registry.ReadRelayTree(old.Owner.ChannelId);
        var node = Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        var target = GenesisRelayTreeSelection.DisconnectTarget(tree, GenesisRelayTreeSelection.Key(tree, node))!;
        var server = old.Owner.ViewingRuntime!.DownstreamServer;
        server.ObserveRecoveryForDisconnect(PcpSmartRelayState.Recovering);
        Assert.Equal(PcpDownstreamDisconnectOutcome.AlreadyStopping, registry.RequestDisconnect(target).Outcome);
        Assert.Equal(1, registry.ActiveRelayCount);
        server.ObserveRecoveryForDisconnect(PcpSmartRelayState.Receiving);
        await using var next = await test.Base.ViewingAsync(test.Token, old.Owner.ChannelId);
        await next.Owner.PublicReady;
        await old.Run.WaitAsync(test.Token);
        await using var b = await WirePeer.Connect(next.Owner.LocalEndPoint.Port, next.Owner.ChannelId, test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 1, test.Token);
        Assert.Equal(PcpDownstreamDisconnectOutcome.OwnerChanged, registry.RequestDisconnect(target).Outcome);
        Assert.Equal(PcpDownstreamDisconnectOutcome.OwnerChanged, server.RequestDisconnect(target).Outcome);
        Assert.Equal(1, registry.ActiveRelayCount);
        await next.ProducerFanout.ConsumeAsync(Packet(next.Owner, 0x64616568, 0, [9, 10]), test.Token);
        Assert.Equal(new byte[] { 9, 10 }, await b.Payload(test.Token));
        var current = registry.ReadRelayTree(next.Owner.ChannelId);
        var now = Assert.Single(current.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        var nowTarget = GenesisRelayTreeSelection.DisconnectTarget(current, GenesisRelayTreeSelection.Key(current, now))!;
        next.Owner.ViewingRuntime!.DownstreamServer.RequestStop();
        Assert.Equal(PcpDownstreamDisconnectOutcome.AlreadyStopping, registry.RequestDisconnect(nowTarget).Outcome);
    }

    [Fact]
    public async Task ManualDisconnectOfNoncooperativeWriterKeepsReservationUntilActualCleanup()
    {
        await using var test = await Fixture.Create(wan: false, total: 1);
        var id = new ChannelId(Guid.NewGuid());
        var registry = test.Base.State.DownstreamRelays;
        await using var fanout = new PcpRelayFanout(new() { ChildStopTimeout = TimeSpan.FromMilliseconds(50) });
        using var owner = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Broadcast, fanout);
        var writer = new ManualDisconnectHeldWriter();
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout, id, owner.SessionId.Value, Transport, admission: owner)
        { PacketWriterFactory = _ => writer, ManualDisconnectTimeout = TimeSpan.FromMilliseconds(250) };
        owner.ObserveSource(server);
        var run = server.RunAsync(test.Token);
        try
        {
            await using var a = await WirePeer.Connect(server.LocalEndPoint.Port, id, test.Token);
            await UntilAsync(() => fanout.ActiveChildCount == 1, test.Token);
            await fanout.ConsumeAsync(new(id, owner.SessionId, 0, 0x64616568, 0, 0, new byte[] { 1 }, null), test.Token);
            await writer.Entered.Task.WaitAsync(test.Token);
            var tree = registry.ReadRelayTree(id);
            var node = Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
            var target = GenesisRelayTreeSelection.DisconnectTarget(tree, GenesisRelayTreeSelection.Key(tree, node))!;
            var request = registry.RequestDisconnect(target);
            Assert.Equal(PcpDownstreamDisconnectOutcome.Accepted, request.Outcome);
            Assert.False(request.Completion!.IsCompleted);
            Assert.Equal(PcpDownstreamDisconnectOutcome.CleanupUnconfirmed, await request.Completion.WaitAsync(test.Token));
            Assert.Equal(1, registry.ActiveRelayCount);
            Assert.False(owner.TryReserve(999, out _));
            Assert.Equal(a.ReceivedBytes, owner.Traffic.Snapshot.CompletedWriteBytes); // Handshake only; held write is not counted.
            writer.Release.TrySetResult();
            await UntilAsync(() => registry.ActiveRelayCount == 0 && server.Snapshot.AdmissionCleanupPendingCount == 0, test.Token);
            Assert.Equal(PcpDownstreamDisconnectOutcome.CleanupUnconfirmed, await request.Completion); // No retroactive success.
            Assert.True(owner.TryReserve(999, out var reservation));
            reservation.Dispose();
        }
        finally { writer.Release.TrySetResult(); await server.StopAsync(test.Token); await run.WaitAsync(test.Token); }
    }

    [Fact]
    public async Task ManualDisconnectCancelsPacingWaitWithoutCountingPartialAtomAsDelivered()
    {
        await using var test = await Fixture.Create(wan: false, ipv4Kbps: 1, pacing: true);
        var id = new ChannelId(Guid.NewGuid());
        var registry = test.Base.State.DownstreamRelays;
        await using var fanout = new PcpRelayFanout();
        using var owner = registry.Register(id, new(Guid.NewGuid()), RelayChannelKind.Broadcast, fanout, () => 1);
        await using var server = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout, id, owner.SessionId.Value, Transport, admission: owner);
        owner.ObserveSource(server);
        var run = server.RunAsync(test.Token);
        await using var a = await WirePeer.Connect(server.LocalEndPoint.Port, id, test.Token);
        await UntilAsync(() => fanout.ActiveChildCount == 1, test.Token);
        await fanout.ConsumeAsync(new(id, owner.SessionId, 0, 0x64616568, 0, 0, new byte[4096], null), test.Token);
        await UntilAsync(() => owner.Pacing!.WaitingRequests == 1, test.Token);
        var tree = registry.ReadRelayTree(id);
        var node = Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        var request = registry.RequestDisconnect(GenesisRelayTreeSelection.DisconnectTarget(tree, GenesisRelayTreeSelection.Key(tree, node))!);
        Assert.Equal(PcpDownstreamDisconnectOutcome.Accepted, request.Outcome);
        Assert.Equal(PcpDownstreamDisconnectOutcome.Completed, await request.Completion!.WaitAsync(test.Token));
        Assert.Equal(0, owner.Pacing!.WaitingRequests);
        Assert.Equal(0, registry.ActiveRelayCount);
        Assert.Equal(0, fanout.ActiveChildCount);
        Assert.Equal(0, server.Snapshot.DeliveredPacketCount);
        Assert.Equal(1, server.Snapshot.DiscardedPacketCount);
        Assert.Equal(0, server.Snapshot.WriteTimedOutCount);
        await server.StopAsync(test.Token);
        await run.WaitAsync(test.Token);
    }

    [Fact]
    public async Task ObsPublishingAdmissionForwardsManualCommandWithoutStoppingPublisher()
    {
        await using var test = await Fixture.Create(wan: false);
        var registry = test.Base.State.DownstreamRelays;
        var factory = new SystemNovaObsPublishingRuntimeFactory(test.Base.State);
        await using var host = new PeerCast.Nova.Network.Publishing.ObsPublishChannelHost(new()
        { Ingest = new() { Port = 0 }, Http = new() { Port = 0 }, RelayPort = 0 },
            registerRelay: factory.Register, useLoopbackAdmission: true);
        host.Start(test.Token);
        await using var a = await WirePeer.Connect(host.RelayEndPoint.Port, host.ChannelId, test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 1, test.Token);
        var tree = registry.ReadRelayTree(host.ChannelId);
        var node = Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        var target = GenesisRelayTreeSelection.DisconnectTarget(tree, GenesisRelayTreeSelection.Key(tree, node))!;
        await using var b = await WirePeer.Connect(host.RelayEndPoint.Port, host.ChannelId, test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 2, test.Token);
        var request = registry.RequestDisconnect(target);
        Assert.Equal(PcpDownstreamDisconnectOutcome.Accepted, request.Outcome);
        Assert.Equal(PcpDownstreamDisconnectOutcome.Completed, await request.Completion!.WaitAsync(test.Token));
        Assert.Equal(1, registry.ActiveRelayCount);
        Assert.False(host.Completion.IsCompleted);
        // This fixture has no RTMP producer. Do not inject into host.Fanout:
        // publishing requires PublishConsumer to arm its mandatory HTTP sink.
        // Media continuation is covered above through the generic broadcast owner.
        var received = await b.ReadAtom(test.Token);
        Assert.Equal(PcpKnownAtomNames.Channel, received.Name); // Existing publishing metadata, not media proof.
        Assert.Null(received.FindFirst(PcpKnownAtomNames.ChannelPacket));
        Assert.Equal(2, host.Fanout.ActiveChildCount); // Mandatory HTTP child plus B.
        Assert.False(host.Completion.IsCompleted);
        Assert.Equal(PcpDownstreamDisconnectOutcome.ChannelMismatch, host.RequestDisconnect(target with { ChannelId = new(Guid.NewGuid()) }).Outcome);
        await b.Quit(test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, test.Token);
    }

    private sealed class ManualDisconnectHeldWriter : IPcpDownstreamPacketWriter
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        { Entered.TrySetResult(); await Release.Task; cancellationToken.ThrowIfCancellationRequested(); }
    }
}
