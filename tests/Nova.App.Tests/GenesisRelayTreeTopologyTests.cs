using System.Buffers;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.App.Tests;

internal static class RelayTreeHostTestPackets
{
    internal static PcpAtom Field(string name, byte[] bytes) => PcpAtom.Leaf(PcpAtomName.FromAscii(name), bytes);
    internal static PcpAtom Host(Guid channel, Guid sid, int port, int? parent = null, string ip = "127.0.0.1")
    {
        var fields = new List<PcpAtom>
        {
            Field("cid", PcpWireValueCodec.EncodeGuid(channel)), Field("id", PcpWireValueCodec.EncodeGuid(sid)),
            Field("ip", PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse(ip))), Field("port", PcpWireValueCodec.EncodeUInt16((ushort)port)),
            Field("numl", PcpWireValueCodec.EncodeUInt32(1)), Field("numr", PcpWireValueCodec.EncodeUInt32(2)),
            Field("ver", PcpWireValueCodec.EncodeUInt32(1218)), Field("vevp", PcpWireValueCodec.EncodeUInt32(27)), Field("flg1", [0x12]),
        };
        if (parent is { } up)
        {
            fields.Add(Field("upip", PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Parse(ip))));
            fields.Add(Field("uppt", PcpWireValueCodec.EncodeUInt16((ushort)up)));
        }
        return PcpAtom.Parent(PcpKnownAtomNames.Host, fields);
    }
    internal static PcpAtom Broadcast(Guid channel, params PcpAtom[] hosts) => PcpAtom.Parent(PcpBroadcastHostReader.Broadcast,
        new[] { Field("cid", PcpWireValueCodec.EncodeGuid(channel)), Field("from", PcpWireValueCodec.EncodeGuid(Guid.NewGuid())),
            Field("ttl", [11]), Field("hops", [0]), Field("grp", [4]) }.Concat(hosts).ToArray());
    internal static void Receive(PcpPeerDirectory directory, params PcpAtom[] atoms)
    {
        var bytes = new ArrayBufferWriter<byte>();
        foreach (var atom in atoms) PcpAtomTreeCodec.Write(bytes, atom);
        var sink = new PcpRelayHostAtomSink(h => directory.Observe(h, new(PcpPeerSourceKind.UpstreamStream, 9)),
            new(directory.ChannelId, directory.LocalSessionId, h => directory.ObserveReport(h, new(PcpPeerSourceKind.UpstreamStream, 9))));
        Assert.NotEqual(PcpDecodeStatus.ProtocolError, new PcpAtomDecoder().Decode(new ReadOnlySequence<byte>(bytes.WrittenMemory), true, sink).Status);
    }
}

public sealed class GenesisRelayTreeTopologyTests
{
    private readonly ChannelId _channel = new(Guid.NewGuid());
    private readonly ChannelSessionId _owner = new(Guid.NewGuid());
    private GenesisRelayTreeSnapshot Tree(params GenesisRelayTreeNode[] nodes) => new(_channel, _owner, GenesisRelayTreeState.Active,
        Array.AsReadOnly(new[] { new GenesisRelayTreeNode("nova", null, GenesisRelayTreeNodeKind.LocalNova, PcpRelayConnectionPhase.Connected) }.Concat(nodes).ToArray()));
    private PcpPeerDirectory Directory(TimeProvider? clock = null) => new(_channel.Value, _owner.Value, clock);
    private static PcpRelayHostEndpoint Endpoint(int port) => new(IPAddress.Loopback, port);

    [Fact]
    public void StandardHostFieldsBuildBranchedMultilevelReportedTopologyWithoutInventingConnections()
    {
        using var directory = Directory(); directory.SetLocalEndpoint(Endpoint(7000));
        var sid = Guid.NewGuid();
        using var owned = directory.TrackDownstream(7, new(sid, Endpoint(50000), Endpoint(7001)));
        RelayTreeHostTestPackets.Receive(directory, RelayTreeHostTestPackets.Broadcast(_channel.Value,
            RelayTreeHostTestPackets.Host(_channel.Value, sid, 7001, 7000),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7002, 7001),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7003, 7002),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7004, 7001)));
        var tree = GenesisRelayTreeTopology.Apply(Tree(new GenesisRelayTreeNode("down-7", "nova", GenesisRelayTreeNodeKind.Downstream,
            PcpRelayConnectionPhase.Connected, ConnectionId: 7)), directory.ObserveLocalTree());
        Assert.Equal(5, tree.Nodes.Count);
        var direct = Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
        Assert.Equal("nova", direct.ParentId); Assert.False(direct.ReportedParent);
        Assert.True(GenesisRelayTreeSelection.HasDownstreamIdentity(tree, GenesisRelayTreeSelection.Key(tree, direct)));
        var middle = Assert.Single(tree.Nodes, n => n.Details?.Endpoint?.Port == 7002);
        var bottom = Assert.Single(tree.Nodes, n => n.Details?.Endpoint?.Port == 7003);
        Assert.Equal(direct.Id, middle.ParentId); Assert.Equal(middle.Id, bottom.ParentId);
        Assert.All(tree.Nodes.Where(n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer), n =>
        { Assert.Null(n.ConnectionId); Assert.True(n.ReportedParent); Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree, GenesisRelayTreeSelection.Key(tree, n))); });
        Assert.Empty(directory.Snapshot()); // BCST reports are not referral candidates.
        var masked = GenesisRelayTreeNodePresentation.Row(tree, middle, true);
        Assert.DoesNotContain("127.0.0.1", masked); Assert.Contains("VP 27", masked); Assert.Contains("視聴 1 / 中継 2", masked);
        Assert.Contains("127.0.0.1", GenesisRelayTreeNodePresentation.Row(tree, middle, false));
        Assert.Contains("BCST内HOST", GenesisRelayTreeNodePresentation.Details(tree, middle, true));
    }

    [Fact]
    public void ReportedAncestorCanPrecedeOwnedUpstreamButEndpointAloneNeverIdentifiesAPeer()
    {
        using var directory = Directory(); var upstreamSid = Guid.NewGuid();
        RelayTreeHostTestPackets.Receive(directory, RelayTreeHostTestPackets.Broadcast(_channel.Value,
            RelayTreeHostTestPackets.Host(_channel.Value, upstreamSid, 7009, 7008),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7008)));
        var basis = Tree(new GenesisRelayTreeNode("up-9", null, GenesisRelayTreeNodeKind.Upstream,
            PcpRelayConnectionPhase.Connected, ConnectionId: 9));
        basis = basis with { Nodes = basis.Nodes.Select(n => n.Kind == GenesisRelayTreeNodeKind.LocalNova ? n with { ParentId = "up-9" } : n).ToArray() };
        var local = directory.ObserveLocalTree() with { Connections = new[] { new PcpLocalPeerConnection(9, new(upstreamSid, new(0x7f000001, 7009))) } };
        var tree = GenesisRelayTreeTopology.Apply(basis, local);
        var ancestor = tree.Nodes.Single(n => n.Details?.Endpoint?.Port == 7008);
        var upstream = tree.Nodes.Single(n => n.Id == "up-9");
        Assert.Equal(ancestor.Id, upstream.ParentId); Assert.True(upstream.ReportedParent);
        Assert.Equal("up-9", tree.Nodes.Single(n => n.Id == "nova").ParentId);
        RelayTreeHostTestPackets.Receive(directory, RelayTreeHostTestPackets.Broadcast(_channel.Value,
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7009),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7010, 7009)));
        tree = GenesisRelayTreeTopology.Apply(basis, directory.ObserveLocalTree() with { Connections = local.Connections });
        Assert.Equal(2, tree.Nodes.Count(n => n.Details?.Endpoint?.Port == 7009));
        Assert.Null(tree.Nodes.Single(n => n.Details?.Endpoint?.Port == 7010).ParentId);
        directory.SetLocalEndpoint(Endpoint(7000));
        RelayTreeHostTestPackets.Receive(directory, RelayTreeHostTestPackets.Host(_channel.Value, upstreamSid, 7009, 7000));
        tree = GenesisRelayTreeTopology.Apply(basis, directory.ObserveLocalTree() with { Connections = local.Connections });
        Assert.Null(tree.Nodes.Single(n => n.Id == "up-9").ParentId); // Reject reported cycle, preserve actual edge.
        Assert.Equal("up-9", tree.Nodes.Single(n => n.Id == "nova").ParentId);
    }

    [Fact]
    public void EndpointCollisionSidConflictSelfReferenceCycleAndUnknownParentNeverFabricateAnEdge()
    {
        using var directory = Directory();
        var duplicate = Guid.NewGuid();
        RelayTreeHostTestPackets.Receive(directory, RelayTreeHostTestPackets.Broadcast(_channel.Value,
            RelayTreeHostTestPackets.Host(_channel.Value, duplicate, 7100, 7101),
            RelayTreeHostTestPackets.Host(_channel.Value, duplicate, 7101, 7100),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7102, 7102),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7103, 7104),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7104, 7103),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7105, 7199),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7106),
            RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7106)));
        RelayTreeHostTestPackets.Receive(directory, RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7107, 7106));
        var tree = GenesisRelayTreeTopology.Apply(Tree(), directory.ObserveLocalTree());
        Assert.Equal(10, tree.Nodes.Count);
        Assert.All(tree.Nodes, n => Assert.Null(n.ParentId));
        Assert.Equal(9, tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer));
    }

    [Fact]
    public void RefreshExpiryRetirementAndOwnerReplacementInvalidateReportedSelection()
    {
        var clock = new TopologyClock(); using var directory = Directory(clock);
        var host = RelayTreeHostTestPackets.Host(_channel.Value, Guid.NewGuid(), 7001);
        RelayTreeHostTestPackets.Receive(directory, host);
        var tree = GenesisRelayTreeTopology.Apply(Tree(), directory.ObserveLocalTree());
        var node = Assert.Single(tree.Nodes, n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer);
        var key = GenesisRelayTreeSelection.Key(tree, node);
        clock.Advance(TimeSpan.FromSeconds(1)); RelayTreeHostTestPackets.Receive(directory, host);
        var refreshed = GenesisRelayTreeTopology.Apply(Tree(), directory.ObserveLocalTree());
        Assert.NotNull(GenesisRelayTreeSelection.Resolve(refreshed, key));
        Assert.Equal(node.ReportId, refreshed.Nodes.Single(n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer).ReportId);
        Assert.DoesNotContain(GenesisRelayTreeTopology.Apply(Tree() with { OwnerId = new(Guid.NewGuid()) }, directory.ObserveLocalTree()).Nodes,
            n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer);
        clock.Advance(PcpPeerDirectory.Lifetime);
        Assert.Null(GenesisRelayTreeSelection.Resolve(GenesisRelayTreeTopology.Apply(Tree(), directory.ObserveLocalTree()), key));
        RelayTreeHostTestPackets.Receive(directory, host);
        Assert.Null(GenesisRelayTreeSelection.Resolve(GenesisRelayTreeTopology.Apply(Tree(), directory.ObserveLocalTree()), key));
        directory.Dispose(); Assert.Empty(directory.ObserveLocalTree().Reports);
    }

    private sealed class TopologyClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}
