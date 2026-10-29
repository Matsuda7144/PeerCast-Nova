using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal static class PublishingWanPresentation
{
    internal static string Format(ObsPublishWanStatus status)
    {
        if (status.CleanupIncomplete) return "WAN PCP: Cleanup未完了。再開始できません。";
        if (status.Failure != ObsPublishWanFailure.None) return status.Failure switch
        {
            ObsPublishWanFailure.InvalidSettings => "WAN PCP: NIC/IP・ポート設定が無効です。",
            ObsPublishWanFailure.InterfaceUnavailable or ObsPublishWanFailure.NetworkChanged => "WAN PCP: NIC/IPが変化、または確認できません。再開始前に設定を確認してください。",
            ObsPublishWanFailure.BandwidthUnsupported => "WAN PCP: IPv4帯域上限の強制は未対応のため開始できません。",
            ObsPublishWanFailure.AdmissionUnavailable => "WAN PCP: 接続数制御の準備ができません。公開していません。",
            _ => "WAN PCP: Listener起動失敗。IP/ポート・競合・利用権限を確認してください。",
        };
        if (!status.ListenerReady || !PublishingIpv4.IsBindAddress(status.BindAddress) || status.BindPort is not (>= 1 and <= 65535))
            return "WAN PCP: 設定済み／未待受・停止中。外部到達性未確認。YP掲載OFF。";
        var declared = PublishingIpv4.IsPublicAddress(status.AdvertisedAddress) && status.AdvertisedPort is >= 1 and <= 65535
            ? $"公開設定: {status.AdvertisedAddress}:{status.AdvertisedPort}（未確認）" : "公開IPv4/外部ポート: 未設定";
        return $"WAN PCP: Listener稼働中 {status.BindAddress}:{status.BindPort}\r\n{declared}\r\n外部到達性未確認 · 帯域制限未適用 · YP掲載OFF";
    }
}
