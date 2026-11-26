using System.Globalization;

namespace PeerCast.Nova.App;

internal static class GenesisConnectionSettingsText
{
    internal const string Notice =
        "・ポート・帯域・リレー数・WAN公開は、保存すると即時に反映されます（IPv6は未対応）。\r\n" +
        "・「ポート自動開放」はチェックすると即座に反映・保存され、ルーターへのポート開放を自動試行します。\r\n" +
        "・帯域・リレー数は半角数字で入力します（空欄＝無制限、0＝許可しない）。";

    internal static string Summary(GenesisGuiNextConnectionPreferences value)
    {
        static string N(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? "無制限";
        return $"WAN {(value.WanRelayEnabled ? "ON" : "OFF")} / Port {value.ListenPort} / IPv6 {(value.UseIpv6 ? "ON" : "OFF")}\r\n" +
            $"帯域(kbps): IPv4 {N(value.TotalUpstreamIpv4Kbps)} / IPv6 {N(value.TotalUpstreamIpv6Kbps)}\r\n" +
            $"リレー: 合計 {N(value.MaxRelaysTotal)} / 配信毎 {N(value.MaxRelaysPerBroadcastChannel)} / 視聴毎 {N(value.MaxRelaysPerViewingChannel)}";
    }
}
