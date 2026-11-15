using System.Globalization;

namespace PeerCast.Nova.App;

internal static class GenesisConnectionSettingsText
{
    internal const string Notice =
        "WAN Relayを公開すると、起動時に指定ポートで待受します。\r\n" +
        "WAN公開・ポート・帯域・リレー数は次回起動時に反映。IPv6は次回用（未対応）です。\r\n" +
        "自動開放は即時反映・自動保存。ONならルーターへ開放を試みます。\r\n" +
        "帯域・リレー数は半角整数。空欄は無制限、0は上限0。IPv4帯域の指定中はLAN/WAN配信を開始できません。";

    internal static string Summary(GenesisGuiNextConnectionPreferences value)
    {
        static string N(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? "無制限";
        return $"WAN {(value.WanRelayEnabled ? "ON" : "OFF")} / Port {value.ListenPort} / IPv6 {(value.UseIpv6 ? "ON" : "OFF")}\r\n" +
            $"帯域(kbps): IPv4 {N(value.TotalUpstreamIpv4Kbps)} / IPv6 {N(value.TotalUpstreamIpv6Kbps)}\r\n" +
            $"リレー: 合計 {N(value.MaxRelaysTotal)} / 配信毎 {N(value.MaxRelaysPerBroadcastChannel)} / 視聴毎 {N(value.MaxRelaysPerViewingChannel)}";
    }
}
