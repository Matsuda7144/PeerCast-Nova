using System.Globalization;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextRelayTreePresentation(string Tree, string Summary)
{
    internal static GenesisGuiNextRelayTreePresentation From(GenesisRelayTreeSnapshot snapshot)
    {
        if (snapshot.ChannelId is null) return new("チャンネルを選択してください。", "リレーツリー · 情報未取得");
        var lines = new List<string>();
        foreach (var node in snapshot.Nodes.Where(n => n.Kind is GenesisRelayTreeNodeKind.Upstream or GenesisRelayTreeNodeKind.LocalInput))
        {
            lines.Add(node.Kind == GenesisRelayTreeNodeKind.LocalInput
                ? $"ローカル配信入力（RTMP） · {Phase(node.Phase)}"
                : $"上流接続 {node.Id} · {Phase(node.Phase)}");
            if (snapshot.Nodes.Any(n => n.Kind == GenesisRelayTreeNodeKind.LocalNova && n.ParentId == node.Id)) lines.Add("  │");
        }
        lines.Add("PeerCast Nova · " + State(snapshot.State));
        var children = snapshot.Nodes.Where(n => n.Kind == GenesisRelayTreeNodeKind.Downstream)
            .Take(PcpRelayTreeObservation.MaximumConnections).ToArray();
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            lines.Add($"  {(i == children.Length - 1 ? "└─" : "├─")} 下流接続 {child.Id} · {Phase(child.Phase)} · {Path(child.Path)} · {Rate(child.WriteKbps)}");
        }
        if (snapshot.OmittedConnections > 0) lines.Add($"  … 他 {snapshot.OmittedConnections} 接続は表示上限により省略");
        if (snapshot.State == GenesisRelayTreeState.Unobserved) lines.Add("観測情報なし（表示から通信は開始しません）");
        var connected = children.Count(n => n.Phase == PcpRelayConnectionPhase.Connected);
        var pending = children.Count(n => n.Phase is PcpRelayConnectionPhase.Connecting or PcpRelayConnectionPhase.Negotiating);
        var closing = children.Count(n => n.Phase is PcpRelayConnectionPhase.Closing or PcpRelayConnectionPhase.Disconnected);
        var capacity = snapshot.Capacity;
        var limit = capacity is null ? "Unknown" : capacity.ChannelRelayLimit?.ToString(CultureInfo.InvariantCulture) ?? "制限なし";
        var reserved = capacity?.ChannelReservedRelays?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";
        var summary = $"ツリー · {State(snapshot.State)}\r\n上流 · {Phase(snapshot.Upstream)}\r\n" +
            $"表示内下流 · 接続 {connected} / 交渉 {pending} / 終了 {closing}\r\n予約 {reserved} / 上限 {limit}\r\n" +
            $"書込み完了 · {Rate(snapshot.Traffic?.KilobitsPerSecond)}\r\nペーシング · {Pacing(snapshot.Pacing)}";
        return new(string.Join("\r\n", lines), summary);
    }

    private static string Rate(double? rate) => rate is { } value && double.IsFinite(value) && value >= 0
        ? value.ToString("0.0", CultureInfo.InvariantCulture) + " kbps" : "Unknown";
    private static string State(GenesisRelayTreeState state) => state switch
    {
        GenesisRelayTreeState.Active => "稼働中",
        GenesisRelayTreeState.Recovering => "復旧中",
        GenesisRelayTreeState.Stopped => "停止",
        GenesisRelayTreeState.CleanupUnconfirmed => "終了処理未確認",
        _ => "情報未取得",
    };
    private static string Phase(PcpRelayConnectionPhase phase) => phase switch
    {
        PcpRelayConnectionPhase.Connecting => "接続試行中",
        PcpRelayConnectionPhase.Negotiating => "PCP交渉中",
        PcpRelayConnectionPhase.Connected => "接続済み",
        PcpRelayConnectionPhase.Closing => "終了処理中",
        PcpRelayConnectionPhase.Disconnected => "切断済み",
        _ => "Unknown",
    };
    private static string Path(GenesisRelayTreePath path) => path switch
    {
        GenesisRelayTreePath.ViewingLoopback => "視聴loopback",
        GenesisRelayTreePath.PublishingLoopback => "配信loopback",
        GenesisRelayTreePath.SharedPublicListener => "共有listener",
        _ => "経路Unknown",
    };
    private static string Pacing(GenesisRelayTreePacing pacing) => pacing switch
    {
        GenesisRelayTreePacing.Enabled => "有効",
        GenesisRelayTreePacing.Unlimited => "媒体制限なし",
        GenesisRelayTreePacing.Blocked => "媒体停止",
        GenesisRelayTreePacing.Disabled => "OFF",
        _ => "Unknown",
    };
}

// Reuses the existing card's body and action row; its primary controls stay put.
internal sealed record GenesisGuiNextRelayTreeLayout(int Width)
{
    internal static bool Applies(bool modern, GenesisGuiNextTab tab) => modern && tab == GenesisGuiNextTab.Channels;
    internal GenesisGuiNextAppShellPlacement.WindowRectangle Body => new(22, 50,
        Math.Max(23, new GenesisGuiNextChannelCardLayout(Width).DividerLeft - 14), 120);
    internal static GenesisGuiNextAppShellPlacement.WindowRectangle Toggle => new(390, 146, 526, 178);
}
