using System.Buffers;
using System.IO.Pipelines;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Fact]
    public async Task RelayTreeMatchesActualChannelsLocalAndSharedChildrenWithoutDirectoryCandidates()
    {
        await using var test = await Fixture.Create(ipv4Kbps: 2000, pacing: true);
        await using var one = await test.Base.ViewingAsync(test.Token, bitrateKbps: 500);
        await using var two = await test.Base.ViewingAsync(test.Token, bitrateKbps: 700);
        await one.Owner.PublicReady;
        await two.Owner.PublicReady;
        var registry = test.Base.State.DownstreamRelays;
        var initial = registry.ReadRelayTree(one.Owner.ChannelId);
        Assert.Equal(one.Owner.SessionId, initial.OwnerId);
        Assert.Equal(GenesisRelayTreeState.Active, initial.State);
        Assert.Single(initial.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Upstream && n.Phase == PcpRelayConnectionPhase.Connected);
        Assert.DoesNotContain(initial.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        await using var local = await WirePeer.Connect(one.Owner.LocalEndPoint.Port, one.Owner.ChannelId, test.Token);
        await using var shared = await WirePeer.Connect(test.Port, one.Owner.ChannelId, test.Token);
        await using var other = await WirePeer.Connect(test.Port, two.Owner.ChannelId, test.Token);
        await UntilAsync(() => registry.ReadRelayTree(one.Owner.ChannelId).Nodes.Count(n =>
            n.Kind == GenesisRelayTreeNodeKind.Downstream && n.Phase == PcpRelayConnectionPhase.Connected) == 2, test.Token);
        // Metadata only: this fixture never connects to referral endpoints.
        var referral = Host(one.Owner, Guid.NewGuid(), "8.8.8.8", 12345);
        await local.Send(PcpReferralPlanner.CreateHost(referral), test.Token);
        await UntilAsync(() => one.Owner.ViewingRuntime!.DownstreamServer.Peers.Snapshot().Any(p => p.Host.PeerSessionId == referral.PeerSessionId), test.Token);
        var tree = registry.ReadRelayTree(one.Owner.ChannelId);
        var children = tree.Nodes.Where(n => n.Kind == GenesisRelayTreeNodeKind.Downstream).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, n => Assert.Equal("nova", n.ParentId));
        Assert.Contains(children, n => n.Path == GenesisRelayTreePath.ViewingLoopback);
        Assert.Contains(children, n => n.Path == GenesisRelayTreePath.SharedPublicListener);
        Assert.Equal(GenesisRelayTreePacing.Enabled, tree.Pacing);
        Assert.Equal(2, tree.Capacity!.ChannelReservedRelays);
        Assert.Equal(children.Select(n => n.Id), registry.ReadRelayTree(one.Owner.ChannelId).Nodes
            .Where(n => n.Kind == GenesisRelayTreeNodeKind.Downstream).Select(n => n.Id));
        var second = registry.ReadRelayTree(two.Owner.ChannelId);
        Assert.Single(second.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        Assert.DoesNotContain(second.Nodes, n => children.Any(c => c.Id == n.Id));
        foreach (var channel in new[] { one, two })
        {
            await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x64616568, 0, [1, 2]), test.Token);
            await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x61746164, 2, [3, 4]), test.Token);
        }
        foreach (var peer in new[] { local, shared, other })
        {
            Assert.Equal(new byte[] { 1, 2 }, await peer.Payload(test.Token));
            Assert.Equal(new byte[] { 3, 4 }, await peer.Payload(test.Token));
        }
        await UntilAsync(() => registry.ReadRelayTree(one.Owner.ChannelId).Traffic!.CompletedWriteBytes == local.ReceivedBytes + shared.ReceivedBytes, test.Token);
        await local.Quit(test.Token);
        await shared.Quit(test.Token);
        await other.Quit(test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0 && one.Owner.DownstreamSnapshot.ActiveConnectionCount == 0, test.Token);
        Assert.DoesNotContain(registry.ReadRelayTree(one.Owner.ChannelId).Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        await using var replacement = await test.Base.ViewingAsync(test.Token, one.Owner.ChannelId);
        await replacement.Owner.PublicReady;
        await one.Run.WaitAsync(test.Token);
        var next = registry.ReadRelayTree(one.Owner.ChannelId);
        Assert.Equal(replacement.Owner.SessionId, next.OwnerId);
        Assert.DoesNotContain(next.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream ||
            n.Kind == GenesisRelayTreeNodeKind.Upstream && initial.Nodes.Any(o => o.Id == n.Id));
        await replacement.DisposeAsync();
        Assert.Equal(GenesisRelayTreeState.Unobserved, registry.ReadRelayTree(one.Owner.ChannelId).State);
    }

    [Fact]
    public async Task RelayTreeExcludesUnvalidatedTcpAndMarksHttpAttemptAsNegotiatingUntilHelo()
    {
        await using var test = await Fixture.Create(wan: false);
        await using var channel = await test.Base.ViewingAsync(test.Token);
        var registry = test.Base.State.DownstreamRelays;
        await using var client = await new TcpTransportConnector(Transport).ConnectAsync("127.0.0.1", channel.Owner.LocalEndPoint.Port, test.Token);
        var reader = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            await UntilAsync(() => channel.Owner.DownstreamSnapshot.ActiveConnectionCount == 1, test.Token);
            Assert.DoesNotContain(registry.ReadRelayTree(channel.Owner.ChannelId).Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
            await client.WriteAsync(new PcpRelayRequest { ChannelId = channel.Owner.ChannelId.Value, Host = "127.0.0.1" }.Encode(), test.Token);
            Assert.Equal(200, (await PcpHttpHeaderBlockReader.ReadResponseAsync(reader, 32768, test.Token)).StatusCode);
            var pending = Assert.Single(registry.ReadRelayTree(channel.Owner.ChannelId).Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
            Assert.Equal(PcpRelayConnectionPhase.Negotiating, pending.Phase);
            var bytes = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(bytes, PcpAtom.Parent(PcpKnownAtomNames.Helo));
            await client.WriteAsync(bytes.WrittenMemory, test.Token);
            await using var atoms = new PcpAtomPipeMessageReader(reader);
            Assert.Equal(0U, PcpQuitCodec.Parse(await atoms.ReadNextAsync(test.Token)).Code);
        }
        finally { await reader.CompleteAsync(); }
        await UntilAsync(() => channel.Owner.DownstreamSnapshot.ActiveConnectionCount == 0, test.Token);
        Assert.DoesNotContain(registry.ReadRelayTree(channel.Owner.ChannelId).Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        Assert.Equal(0, registry.ActiveRelayCount);
    }

    [Fact]
    public async Task RelayTreeOfGenericBroadcastDoesNotInventAnInputPeer()
    {
        await using var test = await Fixture.Create(wan: false);
        await using var channel = await test.Base.BroadcastAsync(test.Token);
        var registry = test.Base.State.DownstreamRelays;
        Assert.Single(registry.ReadRelayTree(channel.Owner.ChannelId).Nodes);
        await using var child = await WirePeer.Connect(channel.Owner.LocalEndPoint.Port, channel.Owner.ChannelId, test.Token);
        await UntilAsync(() => registry.ReadRelayTree(channel.Owner.ChannelId).Nodes.Any(n =>
            n.Kind == GenesisRelayTreeNodeKind.Downstream && n.Phase == PcpRelayConnectionPhase.Connected), test.Token);
        var tree = registry.ReadRelayTree(channel.Owner.ChannelId);
        Assert.Equal(GenesisRelayTreePath.PublishingLoopback, Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream).Path);
        Assert.DoesNotContain(tree.Nodes, n => n.Kind is GenesisRelayTreeNodeKind.Upstream or GenesisRelayTreeNodeKind.LocalInput);
        await child.Quit(test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, test.Token);
        await channel.DisposeAsync();
        Assert.Equal(GenesisRelayTreeState.Unobserved, registry.ReadRelayTree(channel.Owner.ChannelId).State);
    }

    [Fact]
    public async Task RelayTreeOfObsOwnerUsesItsExistingRegistrationAndLocalInputStatus()
    {
        await using var fixture = await PublicChannelTestFixture.CreateAsync();
        var factory = new SystemNovaObsPublishingRuntimeFactory(fixture.State);
        await using var runtime = factory.Create(new PeerCast.Nova.Network.Publishing.ObsPublishChannelOptions
        { Ingest = new() { Port = 0 }, Http = new() { Port = 0 }, RelayPort = 0 });
        runtime.Start(CancellationToken.None);
        var id = runtime.Snapshot.ChannelId;
        var tree = fixture.State.DownstreamRelays.ReadRelayTree(id);
        Assert.Equal(GenesisRelayTreeState.Active, tree.State);
        Assert.Equal(runtime.Snapshot.SessionId, tree.OwnerId);
        Assert.Equal(PcpRelayConnectionPhase.Disconnected, Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.LocalInput).Phase);
        Assert.DoesNotContain(tree.Nodes, n => n.Kind is GenesisRelayTreeNodeKind.Upstream or GenesisRelayTreeNodeKind.Downstream);
        await runtime.DisposeAsync();
        Assert.Equal(GenesisRelayTreeState.Unobserved, fixture.State.DownstreamRelays.ReadRelayTree(id).State);
    }
}
