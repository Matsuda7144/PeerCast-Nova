namespace PeerCast.Nova.App;

internal static class GenesisGuiNextTopStatusPresentation
{
    internal const string UnknownValue = "—";

    public static string FormatUnavailable() =>
        $"接続 {UnknownValue} | P2Pノード数 {UnknownValue} | 上り {UnknownValue} | 下り {UnknownValue} | ポート開放 {UnknownValue}";
}
