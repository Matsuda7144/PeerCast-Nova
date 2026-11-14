using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRelayTreeDisconnectTests
{
    [Theory]
    [InlineData("accept")]
    [InlineData("cancel")]
    [InlineData("owner")]
    [InlineData("channel")]
    [InlineData("reconnect")]
    [InlineData("disconnected")]
    [InlineData("recovery")]
    [InlineData("close")]
    public void DialogAlwaysKeepsOriginalChildAndRechecksAfterConfirmation(string change)
    {
        var node = new GenesisRelayTreeNode("down-2A", "nova", GenesisRelayTreeNodeKind.Downstream,
            PcpRelayConnectionPhase.Connected, ConnectionId: 42, ChildInstanceId: Guid.NewGuid());
        var tree = new GenesisRelayTreeSnapshot(new(Guid.NewGuid()), new(Guid.NewGuid()), GenesisRelayTreeState.Active, new[] { node });
        var key = GenesisRelayTreeSelection.Key(tree, node);
        var open = true;
        var target = GenesisRelayTreeDisconnect.Confirm(key, () => tree, () => open, id =>
        {
            Assert.Equal(node.Id, id); // Only the display ID is passed to the dialog.
            tree = change switch
            {
                "owner" => tree with { OwnerId = new(Guid.NewGuid()) },
                "channel" => tree with { ChannelId = new(Guid.NewGuid()) },
                "reconnect" => tree with { Nodes = new[] { node with { ChildInstanceId = Guid.NewGuid() } } },
                "disconnected" => tree with { Nodes = Array.Empty<GenesisRelayTreeNode>() },
                "recovery" => tree with { State = GenesisRelayTreeState.Recovering },
                _ => tree,
            };
            if (change == "close") open = false;
            return change != "cancel";
        });
        if (change == "accept") Assert.Equal(new(tree.ChannelId!, tree.OwnerId!, 42, node.ChildInstanceId!.Value), target);
        else Assert.Null(target);
    }

    [Fact]
    public void ReportedPeerCannotOpenConfirmationEvenWithForgedConnectionFields()
    {
        var node = new GenesisRelayTreeNode("reported", "nova", GenesisRelayTreeNodeKind.ReportedPeer,
            PcpRelayConnectionPhase.Connected, ConnectionId: 42, ReportId: 42, ChildInstanceId: Guid.NewGuid());
        var tree = new GenesisRelayTreeSnapshot(new(Guid.NewGuid()), new(Guid.NewGuid()), GenesisRelayTreeState.Active, new[] { node });
        Assert.Null(GenesisRelayTreeDisconnect.Confirm(GenesisRelayTreeSelection.Key(tree, node), () => tree, () => true,
            _ => throw new InvalidOperationException("Must not confirm a HOST report.")));
        Assert.NotEqual(GenesisRelayTreeDisconnect.Status(PcpDownstreamDisconnectOutcome.Completed),
            GenesisRelayTreeDisconnect.Status(PcpDownstreamDisconnectOutcome.Accepted));
        Assert.NotEqual(GenesisRelayTreeDisconnect.Status(PcpDownstreamDisconnectOutcome.Completed),
            GenesisRelayTreeDisconnect.Status(PcpDownstreamDisconnectOutcome.CleanupUnconfirmed));
    }
}
