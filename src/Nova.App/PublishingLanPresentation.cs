using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal static class PublishingLanPresentation
{
    internal static string Format(ObsPublishLanStatus? status, Guid channelId)
    {
        if (status?.Enabled != true) return "LAN PCP Relay: OFF";
        if (status.Failure != ObsPublishLanFailure.None) return status.Failure switch
        {
            ObsPublishLanFailure.InvalidSettings => "LAN PCP: NIC/IP・ポート設定が無効です。",
            ObsPublishLanFailure.InterfaceUnavailable => "LAN PCP: NIC/IP・Subnetを確認できません。再開始前に設定を確認してください。",
            ObsPublishLanFailure.BandwidthUnsupported => "LAN PCP: IPv4帯域上限の強制は未対応のため開始できません。",
            ObsPublishLanFailure.AdmissionUnavailable => "LAN PCP: 接続数制御の準備ができません。公開していません。",
            _ => "LAN PCP: Bind失敗。IP/ポート・競合・利用権限を確認してください。",
        };
        if (!status.Ready || status.Port is not (>= 1 and <= 65535) ||
            !ObsPublishLanOptions.TryPrivateAddress(status.Address, out _) || channelId == Guid.Empty)
            return "LAN PCP: 未待受／停止中（接続URLなし）";
        return $"LAN PCP: {status.Address}:{status.Port} · 帯域制限未適用\r\n" +
            DailyPcpPlaybackSourceParser.Format(status.Address!, status.Port.Value, channelId);
    }
}
