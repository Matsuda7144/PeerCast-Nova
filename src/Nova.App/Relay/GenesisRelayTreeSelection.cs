using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

// Identity is independent of labels, native handles and display order. This is
// Network atomically revalidates the scalar identity before accepting a command.
internal sealed record GenesisRelayTreeSelectionKey(ChannelId? ChannelId, ChannelSessionId? OwnerId,
    GenesisRelayTreeNodeKind Kind, long? ConnectionId, GenesisRelayTreePath Path, long? ReportId = null, Guid? ChildInstanceId = null);

internal static class GenesisRelayTreeSelection
{
    internal static GenesisRelayTreeSelectionKey Key(GenesisRelayTreeSnapshot tree, GenesisRelayTreeNode node) =>
        new(tree.ChannelId, tree.OwnerId, node.Kind, node.ConnectionId, node.Path, node.ReportId, node.ChildInstanceId);

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
            Kind: GenesisRelayTreeNodeKind.Downstream, ConnectionId: not null, ReportId: null,
            ChildInstanceId: { } instance,
            Phase: PcpRelayConnectionPhase.Connected
        } && instance != Guid.Empty;

    internal static PcpDownstreamDisconnectTarget? DisconnectTarget(GenesisRelayTreeSnapshot tree,
        GenesisRelayTreeSelectionKey? key) => HasDownstreamIdentity(tree, key)
            ? new(key!.ChannelId!, key.OwnerId!, key.ConnectionId!.Value, key.ChildInstanceId!.Value) : null;
}
