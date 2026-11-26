using System.Globalization;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal static class GenesisRelayTreeNodePresentation
{
    internal static string Summary(GenesisRelayTreeSnapshot tree) =>
        $"{GenesisGuiNextRelayTreePresentation.State(tree.State)} · 直接下流 {tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.Downstream)} · " +
        $"HOST申告 {tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.ReportedPeer)} · " +
        "申告は接続成功の証明ではありません";

    internal static string Row(GenesisRelayTreeSnapshot tree, GenesisRelayTreeNode node, bool mask)
    {
        if (node.Kind == GenesisRelayTreeNodeKind.LocalNova)
            return $"◆ PeerCast Nova · {GenesisGuiNextRelayTreePresentation.State(tree.State)} · " +
                $"実下流 {tree.Nodes.Count(n => n.Kind == GenesisRelayTreeNodeKind.Downstream)} · {Endpoint(node.Details?.Endpoint, mask)}";
        var role = node.Kind switch
        {
            GenesisRelayTreeNodeKind.Upstream => "▲ 上流 OwnedConnection",
            GenesisRelayTreeNodeKind.Downstream => "▼ 直接下流 OwnedConnection",
            GenesisRelayTreeNodeKind.LocalInput => "■ RTMP入力",
            _ => node.ParentId is null ? "◇ HOST申告・関係不明" : "◇ HOST申告",
        };
        var report = node.Details?.Report;
        return $"{role} {node.Id} · {Endpoint(node.Details?.Endpoint, mask)} · " +
            (node.Kind == GenesisRelayTreeNodeKind.ReportedPeer ? ReportState(report) : GenesisGuiNextRelayTreePresentation.Phase(node.Phase)) +
            $" · HOST[視聴 {Count(report?.DirectCount)} / 中継 {Count(report?.RelayCount)}] · {Version(report)}" +
            (node.Kind == GenesisRelayTreeNodeKind.ReportedPeer ? "" : " · " + GenesisGuiNextRelayTreePresentation.Rate(node.WriteKbps));
    }

    internal static string Details(GenesisRelayTreeSnapshot tree, GenesisRelayTreeNode? node, bool mask)
    {
        if (node is null) return "ノードを選択してください。表示・更新では通信操作を行いません。";
        var details = node.Details; var report = details?.Report;
        return $"{node.Kind} · {node.Id} · {Endpoint(details?.Endpoint, mask)} · " +
            (node.Kind == GenesisRelayTreeNodeKind.ReportedPeer ? "HOST申告（非実接続）" : "ローカル観測") + "\r\n" +
            $"{GenesisGuiNextRelayTreePresentation.Phase(node.Phase)} · {GenesisGuiNextRelayTreePresentation.Path(node.Path)} · " +
            GenesisGuiNextRelayTreePresentation.Rate(node.WriteKbps) + "\r\n" +
            $"HOST: {ReportState(report)} · 視聴 {Count(report?.DirectCount)} / 中継 {Count(report?.RelayCount)} · {Version(report)}\r\n" +
            $"取得元: {details?.Source?.Kind.ToString() ?? "Unknown"}" +
            (details?.Source is null ? "" : details.ViaBroadcast ? " / BCST内HOST" : " / HOST") +
            $" · 最終観測: {details?.Age?.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) ?? "Unknown"} 秒前 · " +
            (node.ReportedParent ? "親子関係: Reported" : node.Kind == GenesisRelayTreeNodeKind.ReportedPeer ? "親子関係: 不明" : "親子関係: ローカル観測") + "\r\n" +
            "Owner世代: " + (tree.OwnerId is null ? "Unknown" : "現行Snapshot") + " / " + GenesisGuiNextRelayTreePresentation.State(tree.State);
    }

    internal static string Endpoint(PcpLocalRelayEndpoint? endpoint, bool mask) => endpoint is null ? "IP/Port Unknown" :
        (mask ? "<IP非表示>" : endpoint.AddressText) + ":" + endpoint.Port.ToString(CultureInfo.InvariantCulture);
    private static string Count(uint? count) => count?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";
    private static string Version(PcpLocalHostInfo? host) => host?.Version is null && host?.VersionVp is null ? "Version Unknown" :
        $"PCP {Count(host.Version)} / VP {Count(host.VersionVp)}";
    private static string ReportState(PcpLocalHostInfo? host) => host?.Receiving == false ? "■ 受信停止（申告）" :
        host?.Firewalled == true ? "▣ Firewalled（申告）" : host?.RelayAvailable == false ? "● リレー満員（申告）" :
        host?.RelayAvailable == true ? "○ リレー可能（申告）" : "◇ 状態Unknown";
}
