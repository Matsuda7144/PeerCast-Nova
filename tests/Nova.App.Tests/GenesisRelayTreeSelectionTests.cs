using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRelayTreeSelectionTests
{
    private static GenesisRelayTreeSnapshot Tree(GenesisRelayTreeNodeKind kind = GenesisRelayTreeNodeKind.Downstream) =>
        new(new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()), GenesisRelayTreeState.Active,
            Array.AsReadOnly(new[] { new GenesisRelayTreeNode("display", "nova", kind, PcpRelayConnectionPhase.Connected,
                GenesisRelayTreePath.SharedPublicListener, 1, 42, ChildInstanceId: Guid.NewGuid()) }));

    [Theory]
    [InlineData((int)GenesisRelayTreeNodeKind.LocalNova)]
    [InlineData((int)GenesisRelayTreeNodeKind.Upstream)]
    [InlineData((int)GenesisRelayTreeNodeKind.Downstream)]
    [InlineData((int)GenesisRelayTreeNodeKind.LocalInput)]
    [InlineData((int)GenesisRelayTreeNodeKind.ReportedPeer)]
    public void TypedSelectionResolvesCurrentNodeWithoutParsingLabels(int value)
    {
        var kind = (GenesisRelayTreeNodeKind)value;
        var tree = Tree(kind);
        var key = GenesisRelayTreeSelection.Key(tree, tree.Nodes[0]);
        Assert.Same(tree.Nodes[0], GenesisRelayTreeSelection.Resolve(tree, key));
        var renamed = tree with { Nodes = Array.AsReadOnly(new[] { tree.Nodes[0] with { Id = "changed label", WriteKbps = 99 } }) };
        Assert.Equal("changed label", GenesisRelayTreeSelection.Resolve(renamed, key)!.Id);
        Assert.Equal(kind == GenesisRelayTreeNodeKind.Downstream, GenesisRelayTreeSelection.HasDownstreamIdentity(tree, key));
    }

    [Fact]
    public void StaleAndForgedKeysCannotResolveOrBecomeFutureTargets()
    {
        var tree = Tree();
        var key = GenesisRelayTreeSelection.Key(tree, tree.Nodes[0]);
        foreach (var invalid in new[] { key with { OwnerId = new(Guid.NewGuid()) }, key with { ChannelId = new(Guid.NewGuid()) },
            key with { ConnectionId = 43 }, key with { Path = GenesisRelayTreePath.ViewingLoopback }, key with { Kind = GenesisRelayTreeNodeKind.Upstream } })
        {
            Assert.Null(GenesisRelayTreeSelection.Resolve(tree, invalid));
            Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree, invalid));
        }
        Assert.Null(GenesisRelayTreeSelection.Resolve(tree with { Nodes = Array.Empty<GenesisRelayTreeNode>() }, key));
        Assert.Null(GenesisRelayTreeSelection.Resolve(tree with { OwnerId = new(Guid.NewGuid()) }, key));
        Assert.Null(GenesisRelayTreeSelection.Resolve(tree with { Nodes = Array.AsReadOnly(new[] { tree.Nodes[0], tree.Nodes[0] }) }, key));
        foreach (var state in new[] { GenesisRelayTreeState.CleanupUnconfirmed, GenesisRelayTreeState.Recovering, GenesisRelayTreeState.Stopped })
            Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree with { State = state }, key));
        Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree with { OwnerId = null }, key with { OwnerId = null }));
        Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree with { ChannelId = null }, key with { ChannelId = null }));
        Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree with { Nodes = Array.AsReadOnly(new[] { tree.Nodes[0] with { ConnectionId = null } }) }, key with { ConnectionId = null }));
        Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree with { Nodes = Array.AsReadOnly(new[] { tree.Nodes[0] with { Phase = PcpRelayConnectionPhase.Closing } }) }, key));
        Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree with { Nodes = new[] { tree.Nodes[0] with { ChildInstanceId = null } } }, key with { ChildInstanceId = null }));
        Assert.False(GenesisRelayTreeSelection.HasDownstreamIdentity(tree with { Nodes = new[] { tree.Nodes[0] with { ChildInstanceId = Guid.Empty } } }, key with { ChildInstanceId = Guid.Empty }));
    }
}
