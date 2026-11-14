using System.Globalization;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

internal enum GenesisRelayTreeState { Unobserved, Active, Recovering, Stopped, CleanupUnconfirmed }
internal enum GenesisRelayTreeNodeKind { LocalNova, Upstream, Downstream, LocalInput }
internal enum GenesisRelayTreePath { Unknown, ViewingLoopback, PublishingLoopback, SharedPublicListener }
internal enum GenesisRelayTreePacing { Unknown, Disabled, Unlimited, Enabled, Blocked }
internal sealed record GenesisRelayTreeNode(string Id, string? ParentId, GenesisRelayTreeNodeKind Kind,
    PcpRelayConnectionPhase Phase, GenesisRelayTreePath Path = GenesisRelayTreePath.Unknown, double? WriteKbps = null);

// Only immutable scalar data and copied nodes reach the GUI. Names/URLs/peer
// metadata are deliberately absent; the existing selected-channel title remains.
internal sealed record GenesisRelayTreeSnapshot(ChannelId? ChannelId, ChannelSessionId? OwnerId,
    GenesisRelayTreeState State, IReadOnlyList<GenesisRelayTreeNode> Nodes, int OmittedConnections = 0,
    RelayAdmissionCapacitySnapshot? Capacity = null, PcpDownstreamTrafficSnapshot? Traffic = null,
    GenesisRelayTreePacing Pacing = GenesisRelayTreePacing.Unknown, PcpRelayConnectionPhase Upstream = PcpRelayConnectionPhase.Unknown)
{
    internal static GenesisRelayTreeSnapshot Empty(ChannelId? id = null) => new(id, null, GenesisRelayTreeState.Unobserved,
        id is null ? Array.Empty<GenesisRelayTreeNode>() : Array.AsReadOnly(new[]
        { new GenesisRelayTreeNode("nova", null, GenesisRelayTreeNodeKind.LocalNova, PcpRelayConnectionPhase.Unknown) }));
}

internal sealed partial class GenesisDownstreamRelayOwnerRegistry
{
    internal GenesisRelayTreeSnapshot ReadRelayTree(ChannelId channelId)
    {
        Owner owner;
        WeakReference<IPcpRelayTreeSource>? weak;
        lock (_gate)
        {
            if (_disposed) return GenesisRelayTreeSnapshot.Empty(channelId);
            if (!_owners.TryGetValue(channelId, out owner!) || owner.Retired)
            {
                // Existing leases, rather than a new peer history, are the
                // evidence that a retired owner has not finished cleanup.
                var pending = _connections.Values.FirstOrDefault(r => r.Owner.ChannelId == channelId);
                return pending is null ? GenesisRelayTreeSnapshot.Empty(channelId) :
                    GenesisRelayTreeSnapshot.Empty(channelId) with
                    { State = GenesisRelayTreeState.CleanupUnconfirmed, Capacity = pending.Owner.Scope.Snapshot };
            }
            weak = owner.TreeSource;
        }
        PcpRelayTreeObservation? observation = null;
        // No source call under the registry gate; sources never receive GUI
        // references and are held weakly rather than extending Owner lifetime.
        if (weak?.TryGetTarget(out var source) == true)
        {
            try { observation = source.ObserveRelayTree(); }
            catch (ObjectDisposedException) { }
        }
        lock (_gate)
        {
            if (_disposed || owner.Retired || !_owners.TryGetValue(channelId, out var current) || !ReferenceEquals(current, owner))
                return GenesisRelayTreeSnapshot.Empty(channelId);
            var capacity = owner.Scope.Snapshot;
            if (observation is null || observation.ChannelId != channelId || observation.OwnerId != owner.SessionId)
                return GenesisRelayTreeSnapshot.Empty(channelId) with
                {
                    OwnerId = owner.SessionId,
                    Capacity = capacity,
                    State = capacity.ChannelReservedRelays > owner.ActiveReservations
                        ? GenesisRelayTreeState.CleanupUnconfirmed : GenesisRelayTreeState.Unobserved,
                };
            return ProjectTree(observation, owner.Kind, capacity, owner.Traffic.Snapshot, owner.Pacing,
                capacity.ChannelReservedRelays > owner.ActiveReservations);
        }
    }

    internal static GenesisRelayTreeSnapshot ProjectTree(PcpRelayTreeObservation observation, RelayChannelKind kind,
        RelayAdmissionCapacitySnapshot capacity, PcpDownstreamTrafficSnapshot? traffic, PcpDownstreamPacer? pacing,
        bool oldCleanupPending = false)
    {
        var recovering = observation.Recovery is PcpSmartRelayState.Recovering or PcpSmartRelayState.Backoff or
            PcpSmartRelayState.Discovering or PcpSmartRelayState.Connecting or PcpSmartRelayState.Negotiating;
        var state = observation.CleanupUnconfirmed || oldCleanupPending ? GenesisRelayTreeState.CleanupUnconfirmed :
            recovering ? GenesisRelayTreeState.Recovering : observation.Running ? GenesisRelayTreeState.Active : GenesisRelayTreeState.Stopped;
        var nodes = new List<GenesisRelayTreeNode>();
        string? parent = null;
        var upstream = observation.Upstream;
        if (kind == RelayChannelKind.Viewing && upstream?.ConnectionId is { } upId)
        {
            parent = "up-" + upId.ToString("X", CultureInfo.InvariantCulture);
            nodes.Add(new(parent, null, GenesisRelayTreeNodeKind.Upstream,
                observation.Running ? upstream.Phase : PcpRelayConnectionPhase.Closing));
        }
        if (kind == RelayChannelKind.Broadcast && observation.LocalInputConnected is { } input)
        {
            nodes.Add(new("input", null, GenesisRelayTreeNodeKind.LocalInput,
                input && observation.Running ? PcpRelayConnectionPhase.Connected : PcpRelayConnectionPhase.Disconnected));
            if (input && observation.Running) parent = "input";
        }
        nodes.Add(new("nova", parent, GenesisRelayTreeNodeKind.LocalNova,
            observation.Running ? PcpRelayConnectionPhase.Connected : PcpRelayConnectionPhase.Disconnected));
        var rows = observation.Downstreams.Take(PcpRelayTreeObservation.MaximumConnections).ToArray();
        foreach (var connection in rows.DistinctBy(c => c.ConnectionId))
        {
            if (connection.ConnectionId is not { } id) continue;
            var path = connection.Path == PcpRelayObservedPath.SharedPublicListener ? GenesisRelayTreePath.SharedPublicListener :
                connection.Path != PcpRelayObservedPath.ChannelLoopback ? GenesisRelayTreePath.Unknown :
                kind == RelayChannelKind.Broadcast ? GenesisRelayTreePath.PublishingLoopback : GenesisRelayTreePath.ViewingLoopback;
            nodes.Add(new("down-" + id.ToString("X", CultureInfo.InvariantCulture), "nova", GenesisRelayTreeNodeKind.Downstream,
                observation.Running ? connection.Phase : PcpRelayConnectionPhase.Closing, path, connection.Traffic?.KilobitsPerSecond));
        }
        return new(observation.ChannelId, observation.OwnerId, state, nodes.AsReadOnly(),
            observation.OmittedConnections + Math.Max(0, observation.Downstreams.Count - rows.Length), capacity, traffic,
            pacing is null ? GenesisRelayTreePacing.Disabled : pacing.MediaKbps is null ? GenesisRelayTreePacing.Unlimited :
                pacing.MediaKbps == 0 ? GenesisRelayTreePacing.Blocked : GenesisRelayTreePacing.Enabled,
            upstream?.Phase ?? PcpRelayConnectionPhase.Unknown);
    }
}
