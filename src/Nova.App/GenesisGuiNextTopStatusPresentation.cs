namespace PeerCast.Nova.App;

internal static class GenesisGuiNextTopStatusPresentation
{
    internal const string UnknownValue = "—";

    internal static string Label(int index, bool compact) => index switch
    {
        0 => "接続",
        1 => compact ? "P2P" : "P2Pノード数",
        2 => compact ? "↑" : "上り",
        3 => compact ? "↓" : "下り",
        4 => compact ? "Port" : "ポート開放",
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public static string FormatUnavailable() =>
        $"接続 {UnknownValue} | P2Pノード数 {UnknownValue} | 上り {UnknownValue} | 下り {UnknownValue} | ポート開放 {UnknownValue}";
}
