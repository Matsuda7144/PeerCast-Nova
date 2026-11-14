using System.Globalization;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

// This internal projection is consumed only by the local native GUI. Addresses
// never enter the public network observation or status/remote-management API.
internal sealed record GenesisRelayTreePeerDetails(Guid? SessionId, PcpLocalRelayEndpoint? Endpoint,
    PcpLocalRelayEndpoint? ListeningEndpoint = null, PcpLocalHostInfo? Report = null,
    PcpLocalHostSource? Source = null, bool ViaBroadcast = false, TimeSpan? Age = null);

internal static class GenesisRelayTreeTopology
{
    internal const int MaximumNodes = PcpLocalRelayTreeObservation.MaximumReports + PcpRelayTreeObservation.MaximumConnections + 2;

    internal static GenesisRelayTreeSnapshot Apply(GenesisRelayTreeSnapshot tree, PcpLocalRelayTreeObservation? local)
    {
        if (local is null || tree.ChannelId?.Value != local.ChannelId || tree.OwnerId?.Value != local.OwnerId) return tree;
        var nodes = tree.Nodes.ToDictionary(n => n.Id);
        var connections = local.Connections.Take(PcpLocalRelayTreeObservation.MaximumReports).GroupBy(c => c.ConnectionId)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        foreach (var node in tree.Nodes)
        {
            if (node.Kind == GenesisRelayTreeNodeKind.LocalNova)
                nodes[node.Id] = node with { Details = new(local.OwnerId, local.LocalEndpoint) };
            else if (node.ConnectionId is { } id && connections.TryGetValue(id, out var connection))
                nodes[node.Id] = node with { Details = new(connection.Peer.SessionId, connection.Peer.Endpoint, connection.Peer.ListeningEndpoint) };
        }
        var reports = local.Reports.Where(r => r.Host.ChannelId == local.ChannelId && r.Host.PeerSessionId is { } sid && sid != Guid.Empty &&
            sid != local.OwnerId && r.Age >= TimeSpan.Zero && r.Age < PcpLocalRelayTreeObservation.MaximumReportAge)
            .Take(PcpLocalRelayTreeObservation.MaximumReports).GroupBy(r => r.ReportId).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
        var sidCounts = reports.GroupBy(r => r.Host.PeerSessionId).ToDictionary(g => g.Key!.Value, g => g.Count());
        var reportNodes = new Dictionary<long, string>();
        foreach (var report in reports)
        {
            var host = report.Host;
            var matches = nodes.Values.Where(n => n.Kind is GenesisRelayTreeNodeKind.Upstream or GenesisRelayTreeNodeKind.Downstream &&
                n.Details?.SessionId == host.PeerSessionId && MatchesEndpoint(n, host)).ToArray();
            var details = new GenesisRelayTreePeerDetails(host.PeerSessionId, host.GlobalEndpoint ?? host.LocalEndpoint,
                Report: host, Source: report.Source, ViaBroadcast: report.ViaBroadcast, Age: report.Age);
            if (sidCounts[host.PeerSessionId!.Value] == 1 && matches.Length == 1)
            {
                var owned = matches[0];
                nodes[owned.Id] = owned with { Details = owned.Details! with { Report = host, Source = report.Source, ViaBroadcast = report.ViaBroadcast, Age = report.Age } };
                reportNodes[report.ReportId] = owned.Id;
            }
            else
            {
                var id = "reported-" + report.ReportId.ToString("X", CultureInfo.InvariantCulture);
                nodes.Add(id, new(id, null, GenesisRelayTreeNodeKind.ReportedPeer, PcpRelayConnectionPhase.Unknown,
                    ReportId: report.ReportId, Details: details));
                reportNodes[report.ReportId] = id;
            }
        }
        var aliases = new Dictionary<PcpLocalRelayEndpoint, HashSet<string>>();
        void Alias(PcpLocalRelayEndpoint? endpoint, string id)
        {
            if (endpoint is not { Port: > 0 and <= 65535 } || endpoint.Address >> 24 is not (> 0 and < 224) || endpoint.Address == uint.MaxValue) return;
            if (!aliases.TryGetValue(endpoint!, out var ids)) aliases[endpoint!] = ids = [];
            ids.Add(id);
        }
        foreach (var node in nodes.Values)
        {
            if (node.Kind == GenesisRelayTreeNodeKind.Downstream) Alias(node.Details?.ListeningEndpoint, node.Id);
            else if (node.Kind != GenesisRelayTreeNodeKind.ReportedPeer) Alias(node.Details?.Endpoint, node.Id);
        }
        foreach (var report in reports)
        {
            Alias(report.Host.GlobalEndpoint, reportNodes[report.ReportId]);
            Alias(report.Host.LocalEndpoint, reportNodes[report.ReportId]);
        }
        foreach (var report in reports)
        {
            var id = reportNodes[report.ReportId]; var node = nodes[id];
            if (node.Kind is not (GenesisRelayTreeNodeKind.ReportedPeer or GenesisRelayTreeNodeKind.Upstream) ||
                sidCounts[report.Host.PeerSessionId!.Value] != 1 || report.Host.UpstreamEndpoint is not { } endpoint ||
                !aliases.TryGetValue(endpoint, out var parents) || parents.Count != 1) continue;
            var parent = parents.Single();
            var parentSid = nodes[parent].Details?.SessionId;
            if (parent == id || parentSid is { } sid && sidCounts.TryGetValue(sid, out var duplicates) && duplicates != 1) continue;
            nodes[id] = node with { ParentId = parent, ReportedParent = true };
        }
        // Remove only reported edges from cycles; actual Nova/child ownership is
        // never reparented by a remote claim. All work is bounded by node count.
        foreach (var start in nodes.Keys.ToArray())
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>();
            string? current = start;
            while (current is not null && nodes.TryGetValue(current, out var node))
            {
                if (positions.TryGetValue(current, out var at))
                {
                    foreach (var cycle in path.Skip(at))
                        if (nodes[cycle].ReportedParent) nodes[cycle] = nodes[cycle] with { ParentId = null, ReportedParent = false };
                    break;
                }
                positions.Add(current, path.Count);
                path.Add(current); current = node.ParentId;
            }
        }
        var ordered = new List<GenesisRelayTreeNode>();
        var queue = new Queue<GenesisRelayTreeNode>(nodes.Values.Where(n => n.ParentId is null || !nodes.ContainsKey(n.ParentId)));
        while (queue.TryDequeue(out var node))
        {
            ordered.Add(node);
            foreach (var child in nodes.Values.Where(n => n.ParentId == node.Id)) queue.Enqueue(child);
        }
        return tree with { Nodes = ordered.AsReadOnly() };
    }

    private static bool MatchesEndpoint(GenesisRelayTreeNode node, PcpLocalHostInfo host)
    {
        var endpoint = node.Kind == GenesisRelayTreeNodeKind.Downstream ? node.Details?.ListeningEndpoint : node.Details?.Endpoint;
        return endpoint is not null && (endpoint == host.GlobalEndpoint || endpoint == host.LocalEndpoint);
    }
}
