using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal static class GenesisRelayTreeDisconnect
{
    internal static PcpDownstreamDisconnectTarget? Confirm(GenesisRelayTreeSelectionKey key,
        Func<GenesisRelayTreeSnapshot> observe, Func<bool> isOpen, Func<string, bool> confirm)
    {
        if (!isOpen()) return null;
        var before = observe();
        var target = GenesisRelayTreeSelection.DisconnectTarget(before, key);
        var node = GenesisRelayTreeSelection.Resolve(before, key);
        if (target is null || node is null || !confirm(node.Id) || !isOpen()) return null;
        // Never retarget to the current selection after a modal dialog.
        return GenesisRelayTreeSelection.DisconnectTarget(observe(), key) == target ? target : null;
    }

    internal static string Status(PcpDownstreamDisconnectOutcome? outcome) => outcome switch
    {
        null => "切断要求中",
        PcpDownstreamDisconnectOutcome.Accepted => "切断処理中",
        PcpDownstreamDisconnectOutcome.Completed => "切断完了",
        PcpDownstreamDisconnectOutcome.AlreadyDisconnected => "既に切断済み",
        PcpDownstreamDisconnectOutcome.OwnerChanged => "Owner変更により中止",
        PcpDownstreamDisconnectOutcome.ChannelMismatch => "チャンネル変更により中止",
        PcpDownstreamDisconnectOutcome.AlreadyStopping => "終了処理中",
        PcpDownstreamDisconnectOutcome.CleanupUnconfirmed => "終了処理未確認",
        PcpDownstreamDisconnectOutcome.Failed => "切断要求に失敗",
        _ => "対象が変化したため中止",
    };
}
