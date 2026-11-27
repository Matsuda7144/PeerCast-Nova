using System.Globalization;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal static class GenesisRelayTreeNodePresentation
{
    internal const string UnattachedGroupParentKey = "__unattached_group__";

    internal static string UnattachedGroupLabel(int count) => $"その他の観測ピア（接続元不明・{count}件）";

    internal static (HashSet<string> UnattachedRootIds, HashSet<string> UnattachedNodeIds) PartitionUnattachedReportedPeers(
        IReadOnlyList<GenesisRelayTreeNode> nodes)
    {
        var byId = new Dictionary<string, GenesisRelayTreeNode>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<GenesisRelayTreeNode>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            _ = byId.TryAdd(node.Id, node);
            if (node.ParentId is { } pid)
            {
                if (!children.TryGetValue(pid, out var list))
                {
                    list = new List<GenesisRelayTreeNode>();
                    children[pid] = list;
                }

                list.Add(node);
            }
        }

        var unattachedRoots = new HashSet<string>(StringComparer.Ordinal);
        var unattachedNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var isRoot = node.ParentId is null || !byId.ContainsKey(node.ParentId);
            if (!isRoot || node.Kind != GenesisRelayTreeNodeKind.ReportedPeer)
            {
                continue;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<GenesisRelayTreeNode>();
            stack.Push(node);
            var hasNonReported = false;
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current.Id))
                {
                    continue;
                }

                if (current.Kind != GenesisRelayTreeNodeKind.ReportedPeer)
                {
                    hasNonReported = true;
                    break;
                }

                if (children.TryGetValue(current.Id, out var list))
                {
                    foreach (var child in list)
                    {
                        stack.Push(child);
                    }
                }
            }

            if (!hasNonReported)
            {
                _ = unattachedRoots.Add(node.Id);
                foreach (var id in visited)
                {
                    _ = unattachedNodes.Add(id);
                }
            }
        }

        return (unattachedRoots, unattachedNodes);
    }

    internal static string Summary(GenesisRelayTreeSnapshot tree) =>
        $"{GenesisGuiNextRelayTreePresentation.State(tree.State)} · 直接下流 {tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.Downstream)} · " +
        $"観測ピア {tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer)} · " +
        "（※観測ピアの情報は各ピアからの申告値）";

    internal static string Row(GenesisRelayTreeSnapshot tree, GenesisRelayTreeNode node, bool mask)
    {
        if (node.Kind == GenesisRelayTreeNodeKind.LocalNova)
        {
            var downstreamCount = tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.Downstream);
            var localLabel = LocalEndpointLabel(node.Details?.Endpoint, mask);
            return $"★ PeerCast Nova（自分） · {GenesisGuiNextRelayTreePresentation.State(tree.State)} · 直接下流 {downstreamCount}" +
                AppendIf(localLabel);
        }

        if (node.Kind == GenesisRelayTreeNodeKind.LocalInput)
        {
            return $"■ RTMP入力 · {GenesisGuiNextRelayTreePresentation.Phase(node.Phase)}";
        }

        var report = node.Details?.Report;
        if (node.Kind == GenesisRelayTreeNodeKind.Upstream)
        {
            return $"▲ 上流 · {Endpoint(node.Details?.Endpoint, mask)} · {GenesisGuiNextRelayTreePresentation.Phase(node.Phase)}" +
                AppendIf(HostCounts(report)) +
                AppendIf(CompactVersion(report)) +
                AppendIf(ValidRate(node.WriteKbps));
        }

        if (node.Kind == GenesisRelayTreeNodeKind.Downstream)
        {
            return $"▼ 下流 · {Endpoint(node.Details?.Endpoint, mask)} · {GenesisGuiNextRelayTreePresentation.Phase(node.Phase)}" +
                AppendIf(ValidRate(node.WriteKbps)) +
                AppendIf(HostCounts(report)) +
                AppendIf(CompactVersion(report));
        }

        return $"{CompactReportState(report)} · {Endpoint(node.Details?.Endpoint, mask)}" +
            AppendIf(HostCounts(report)) +
            AppendIf(CompactVersion(report));
    }

    internal static string Details(GenesisRelayTreeSnapshot tree, GenesisRelayTreeNode? node, bool mask)
    {
        if (node is null)
        {
            return "ノードを選択すると詳細を表示します（表示・更新では通信操作を行いません）。\r\n" +
                "色の見方（PeerCastStation準拠）: 緑=リレー可能 / 青=リレー満員 / 橙=Firewalled・接続中 / 赤=受信停止・切断 / 水色=自分";
        }

        var details = node.Details;
        var report = details?.Report;
        var sourceLabel = details?.Source?.Kind.ToString() ?? "Unknown";
        var originSuffix = details?.Source is null ? "" : details.ViaBroadcast ? " / BCST内HOST" : " / HOST";
        var parentRelation = node.ReportedParent
            ? "親子関係: Reported"
            : node.Kind == GenesisRelayTreeNodeKind.ReportedPeer ? "親子関係: 不明" : "親子関係: ローカル観測";
        return $"{node.Kind} · ID {node.Id} · {Endpoint(details?.Endpoint, mask)} · " +
            (node.Kind == GenesisRelayTreeNodeKind.ReportedPeer ? "HOST申告（非実接続）" : "ローカル観測") + "\r\n" +
            $"{GenesisGuiNextRelayTreePresentation.Phase(node.Phase)} · {GenesisGuiNextRelayTreePresentation.Path(node.Path)} · " +
            GenesisGuiNextRelayTreePresentation.Rate(node.WriteKbps) + "\r\n" +
            $"HOST: {ReportState(report)} · 視聴 {Count(report?.DirectCount)} / 中継 {Count(report?.RelayCount)} · {Version(report)}\r\n" +
            $"取得元: {sourceLabel}{originSuffix} · 最終観測: {details?.Age?.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) ?? "Unknown"} 秒前 · {parentRelation}\r\n" +
            "Owner世代: " + (tree.OwnerId is null ? "Unknown" : "現行Snapshot") + " / " + GenesisGuiNextRelayTreePresentation.State(tree.State);
    }

    internal static int NodeTextColor(GenesisRelayTreeSnapshot tree, GenesisRelayTreeNode node) => node.Kind switch
    {
        GenesisRelayTreeNodeKind.LocalNova => tree.State switch
        {
            GenesisRelayTreeState.Stopped or GenesisRelayTreeState.CleanupUnconfirmed => GenesisGuiNextDesign.Recording,
            GenesisRelayTreeState.Recovering => GenesisGuiNextDesign.Warning,
            GenesisRelayTreeState.Active => GenesisGuiNextDesign.RelaySelf,
            _ => GenesisGuiNextDesign.SecondaryText,
        },
        GenesisRelayTreeNodeKind.LocalInput => node.Phase switch
        {
            PcpRelayConnectionPhase.Connected => GenesisGuiNextDesign.Success,
            PcpRelayConnectionPhase.Connecting or PcpRelayConnectionPhase.Negotiating => GenesisGuiNextDesign.Warning,
            PcpRelayConnectionPhase.Closing or PcpRelayConnectionPhase.Disconnected => GenesisGuiNextDesign.Recording,
            _ => GenesisGuiNextDesign.SecondaryText,
        },
        GenesisRelayTreeNodeKind.Upstream or GenesisRelayTreeNodeKind.Downstream => node.Phase switch
        {
            PcpRelayConnectionPhase.Closing or PcpRelayConnectionPhase.Disconnected => GenesisGuiNextDesign.Recording,
            PcpRelayConnectionPhase.Connecting or PcpRelayConnectionPhase.Negotiating => GenesisGuiNextDesign.Warning,
            _ => node.Details?.Report is { } report
                ? ReportColor(report, GenesisGuiNextDesign.Text)
                : GenesisGuiNextDesign.Text,
        },
        _ => ReportColor(node.Details?.Report, GenesisGuiNextDesign.SecondaryText),
    };

    private static int ReportColor(PcpLocalHostInfo? host, int fallback) =>
        host?.Receiving == false ? GenesisGuiNextDesign.Recording :
        host?.Firewalled == true ? GenesisGuiNextDesign.Warning :
        host?.RelayAvailable == false ? GenesisGuiNextDesign.RelayFull :
        host?.RelayAvailable == true ? GenesisGuiNextDesign.Success :
        fallback;

    internal static string Endpoint(PcpLocalRelayEndpoint? endpoint, bool mask) => endpoint is null ? "IP/Port Unknown" :
        (mask ? "<IP非表示>" : endpoint.AddressText) + ":" + endpoint.Port.ToString(CultureInfo.InvariantCulture);

    private static string? LocalEndpointLabel(PcpLocalRelayEndpoint? endpoint, bool mask)
    {
        if (endpoint is null)
        {
            return null;
        }

        if (endpoint.AddressText is "127.0.0.1" or "::1" ||
            string.Equals(endpoint.AddressText, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "ローカル視聴";
        }

        return Endpoint(endpoint, mask);
    }

    private static string AppendIf(string? segment) => string.IsNullOrEmpty(segment) ? "" : " · " + segment;
    private static string? ValidRate(double? rate) => rate is { } value && double.IsFinite(value) && value >= 0
        ? GenesisGuiNextRelayTreePresentation.Rate(value) : null;
    private static string Count(uint? count) => count?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";
    private static string? HostCounts(PcpLocalHostInfo? host) => host is null || (host.DirectCount is null && host.RelayCount is null)
        ? null
        : $"視聴 {Count(host.DirectCount)} / 中継 {Count(host.RelayCount)}";
    private static string? CompactVersion(PcpLocalHostInfo? host) =>
        host?.VersionVp is { } vp ? $"VP {vp.ToString(CultureInfo.InvariantCulture)}" :
        host?.Version is { } version ? $"PCP {version.ToString(CultureInfo.InvariantCulture)}" :
        null;
    private static string Version(PcpLocalHostInfo? host) => host?.Version is null && host?.VersionVp is null ? "Version Unknown" :
        $"PCP {Count(host.Version)} / VP {Count(host.VersionVp)}";
    private static string CompactReportState(PcpLocalHostInfo? host) => host?.Receiving == false ? "■ 受信停止" :
        host?.Firewalled == true ? "▣ Firewalled" : host?.RelayAvailable == false ? "● リレー満員" :
        host?.RelayAvailable == true ? "○ リレー可能" : "◇ 状態不明";
    private static string ReportState(PcpLocalHostInfo? host) => host?.Receiving == false ? "■ 受信停止（申告）" :
        host?.Firewalled == true ? "▣ Firewalled（申告）" : host?.RelayAvailable == false ? "● リレー満員（申告）" :
        host?.RelayAvailable == true ? "○ リレー可能（申告）" : "◇ 状態Unknown";
}
