using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

// Identity is independent of labels, native handles and display order. This is
// observation only; RT-4 must atomically revalidate in the owning network layer.
internal sealed record GenesisRelayTreeSelectionKey(ChannelId? ChannelId, ChannelSessionId? OwnerId,
    GenesisRelayTreeNodeKind Kind, long? ConnectionId, GenesisRelayTreePath Path, long? ReportId = null);

internal static class GenesisRelayTreeSelection
{
    internal static GenesisRelayTreeSelectionKey Key(GenesisRelayTreeSnapshot tree, GenesisRelayTreeNode node) =>
        new(tree.ChannelId, tree.OwnerId, node.Kind, node.ConnectionId, node.Path, node.ReportId);

    internal static GenesisRelayTreeNode? Resolve(GenesisRelayTreeSnapshot tree, GenesisRelayTreeSelectionKey? key)
    {
        if (key is null || key.ChannelId != tree.ChannelId || key.OwnerId != tree.OwnerId) return null;
        var matches = tree.Nodes.Where(n => Key(tree, n) == key).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static bool HasDownstreamIdentity(GenesisRelayTreeSnapshot tree, GenesisRelayTreeSelectionKey? key) =>
        tree.ChannelId is not null && tree.OwnerId is not null && tree.State == GenesisRelayTreeState.Active &&
        Resolve(tree, key) is
        {
            Kind: GenesisRelayTreeNodeKind.Downstream, ConnectionId: not null,
            Phase: PcpRelayConnectionPhase.Connected
        };
}
