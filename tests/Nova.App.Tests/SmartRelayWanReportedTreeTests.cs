using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Fact]
    public async Task LoopbackBcstHostReceptionProjectsMultipleLevelsAndPreservesMediaAdmissionAndOwnerCleanup()
    {
        await using var test = await Fixture.Create(wan: false);
        await using var channel = await test.Base.ViewingAsync(test.Token);
        await using var peer = await WirePeer.Connect(channel.Owner.LocalEndPoint.Port, channel.Owner.ChannelId, test.Token);
        var registry = test.Base.State.DownstreamRelays;
        var cid = channel.Owner.ChannelId.Value;
        var host = RelayTreeHostTestPackets.Host(cid, Guid.NewGuid(), 17101, channel.Owner.LocalEndPoint.Port);
        await peer.Send(host, test.Token);
        await peer.Send(RelayTreeHostTestPackets.Broadcast(cid,
            RelayTreeHostTestPackets.Host(cid, Guid.NewGuid(), 17102, 17101),
            RelayTreeHostTestPackets.Host(cid, Guid.NewGuid(), 17103, 17102),
            RelayTreeHostTestPackets.Host(cid, Guid.NewGuid(), 17104, 17101)), test.Token);
        await UntilAsync(() => registry.ReadRelayTree(channel.Owner.ChannelId).Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer) == 4, test.Token);
        var tree = registry.ReadRelayTree(channel.Owner.ChannelId);
        var root = tree.Nodes.Single(n => n.Details?.Endpoint?.Port == 17101);
        var middle = tree.Nodes.Single(n => n.Details?.Endpoint?.Port == 17102);
        var bottom = tree.Nodes.Single(n => n.Details?.Endpoint?.Port == 17103);
        Assert.Equal("nova", root.ParentId); Assert.Equal(root.Id, middle.ParentId); Assert.Equal(middle.Id, bottom.ParentId);
        Assert.Contains("DownstreamControl / BCST内HOST", GenesisRelayTreeNodePresentation.Details(tree, middle, true));
        Assert.All(tree.Nodes.Where(n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer), n => Assert.Null(n.ConnectionId));
        Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        Assert.Equal(1, registry.ActiveRelayCount);
        Assert.Empty(channel.Owner.ViewingRuntime!.DownstreamServer.Peers.Snapshot()); // local HOST is display-only; BCST never feeds referrals.
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x64616568, 0, [1, 2]), test.Token);
        await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x61746164, 2, [3, 4]), test.Token);
        Assert.Equal(new byte[] { 1, 2 }, await peer.Payload(test.Token));
        Assert.Equal(new byte[] { 3, 4 }, await peer.Payload(test.Token));
        var key = GenesisRelayTreeSelection.Key(tree, middle);
        await peer.Quit(test.Token);
        await UntilAsync(() => registry.ActiveRelayCount == 0, test.Token);
        // With WAN off, the caller owns the old runtime's stop contract.
        await channel.Owner.DisposeAsync();
        await channel.Run.WaitAsync(test.Token);
        await using var replacement = await test.Base.ViewingAsync(test.Token, channel.Owner.ChannelId);
        var next = registry.ReadRelayTree(channel.Owner.ChannelId);
        Assert.Equal(replacement.Owner.SessionId, next.OwnerId);
        Assert.Null(GenesisRelayTreeSelection.Resolve(next, key));
        Assert.DoesNotContain(next.Nodes, n => n.Kind is GenesisRelayTreeNodeKind.ReportedPeer or GenesisRelayTreeNodeKind.Downstream);
        Assert.Null(test.Host); Assert.Empty(test.Router.Calls); // no public listener/NAT operations.
    }
}
