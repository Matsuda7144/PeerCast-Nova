using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextTopStatusValue(string Text, string Compact, int Color);
internal sealed record GenesisGuiNextTopStatusValues(GenesisGuiNextTopStatusValue Connection, GenesisGuiNextTopStatusValue Port)
{
    internal GenesisGuiNextTrafficValues Traffic { get; init; } = GenesisGuiNextTrafficValues.Unknown;
    internal GenesisGuiNextTopStatusValue Value(int index) => index switch
    {
        0 => Connection,
        1 => Traffic.Connections,
        2 => Traffic.Up,
        3 => Traffic.Down,
        4 => Port,
        _ => GenesisGuiNextTopStatusPresentation.Unknown,
    };
    // The native STATIC retains scope and the full, unabridged diagnostics for accessibility.
    internal string AccessibleText => $"PCP Relay接続: {Connection.Text} | PCP接続数 {Traffic.Connections.Text} | PCP上り {Traffic.Up.Text} | PCP下り {Traffic.Down.Text} | Port {Port.Text}。" +
        "接続対象は明示Relayのみ。再生・録画・OBS・YPの接続状態ではありません。計測範囲は成立済みPCPメディア接続のTransport成功Read/Writeのみ。固有Peer数や物理ワイヤ量ではありません。OBS・YP Announce・HTTP等の全体計測未対応。";
}

internal static class GenesisGuiNextTopStatusPresentation
{
    internal const string UnknownValue = "—";
    internal static GenesisGuiNextTopStatusValue Unknown { get; } = new(UnknownValue, UnknownValue, GenesisGuiNextDesign.SecondaryText);

    internal static string Label(int index, bool compact) => index switch
    {
        0 => compact ? "Relay" : "PCP Relay",
        1 => "PCP",
        2 => "↑",
        3 => "↓",
        4 => "Port",
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    // Classic's text and drawing route remain unchanged.
    public static string FormatUnavailable() =>
        $"接続 {UnknownValue} | P2Pノード数 {UnknownValue} | 上り {UnknownValue} | 下り {UnknownValue} | ポート開放 {UnknownValue}";

    internal static GenesisGuiNextTopStatusValues Create(GenesisRelayActivationSnapshot? relay, NovaApplicationNetworkSnapshot? network,
        GenesisGuiNextTrafficValues? traffic = null) => new(Connection(relay), Port(network)) { Traffic = traffic ?? GenesisGuiNextTrafficValues.Unknown };

    private static GenesisGuiNextTopStatusValue Value(string text, string compact, bool failure = false, bool confirmed = false) =>
        new(text, compact, failure ? GenesisGuiNextDesign.Warning : confirmed ? GenesisGuiNextDesign.Success : GenesisGuiNextDesign.SecondaryText);

    private static GenesisGuiNextTopStatusValue Connection(GenesisRelayActivationSnapshot? relay)
    {
        if (relay is null) return Unknown;
        if (!relay.CleanupConfirmed && relay.State is GenesisRelayActivationState.Stopped or GenesisRelayActivationState.Failed)
            return Value("解除未確認", "異常", failure: true);
        if (relay.State == GenesisRelayActivationState.Failed)
            return Value("エラー", "異常", failure: true);
        return relay.State switch
        {
            GenesisRelayActivationState.Running when relay.ListenerBound && relay.UpstreamReady && relay.ReceivingReady => Value("接続中", "接続", confirmed: true),
            GenesisRelayActivationState.Preparing or GenesisRelayActivationState.Starting => Value("準備中", "準備"),
            GenesisRelayActivationState.Stopping => Value("停止中", "停止"),
            GenesisRelayActivationState.Stopped when relay.CleanupConfirmed && !relay.ListenerBound => Value("未接続", "未接"),
            _ => Unknown,
        };
    }

    private static GenesisGuiNextTopStatusValue Port(NovaApplicationNetworkSnapshot? network)
    {
        if (network is null) return Unknown;
        if (network.ShutdownFailure == NovaApplicationNetworkFailure.ShutdownCleanupUnconfirmed ||
            network.Nat?.State == NatDisplayState.UnmapUnconfirmed)
            return Value("解除未確認", "解除?", failure: true);
        if (network.ShutdownFailure != NovaApplicationNetworkFailure.None ||
            network.InitializationFailure is not (NovaApplicationNetworkFailure.None or NovaApplicationNetworkFailure.NotInitialized or NovaApplicationNetworkFailure.Closing))
            return Value("エラー", "エラー", failure: true);
        if (network.Nat?.State == NatDisplayState.ResultUnknown)
            return Value("処理結果未確認", "結果?", failure: true);
        if (network.Disposed) return Value("未待受", "未待");
        if (network.Disposing) return Value("停止中", "停止");
        if (network.Initializing) return Value("準備中", "準備");
        if (!network.Initialized) return Unknown;
        if (network.PublicListenerAvailability == NovaApplicationPublicListenerAvailability.Failed)
            return Value("エラー", "エラー", failure: true);
        if (network.PublicListenerAvailability == NovaApplicationPublicListenerAvailability.Starting)
            return Value("準備中", "準備");
        if (network.PublicListenerAvailability == NovaApplicationPublicListenerAvailability.Stopping)
            return Value("停止中", "停止");
        if (network.PublicListenerAvailability != NovaApplicationPublicListenerAvailability.Listening)
            return Value("未待受", "未待");
        var nat = network.Nat;
        // A mapping for another listener must not be borrowed as evidence for this one.
        int? boundPort = network.SharedPublicRelay is { State: GenesisSharedPublicRelayState.Listening } shared
            ? shared.Port : network.PublicRelay is { State: NovaPublicRelayState.Listening, Endpoint: { } endpoint }
                ? endpoint.Port : null;
        if (boundPort is null || nat is null ||
            nat.InternalPort != boundPort || !nat.RegisteredInternalPorts.Contains(boundPort.Value))
            return Value("待受中・到達未確認", "待受");
        if (nat.State is NatDisplayState.Mapped or NatDisplayState.Renewing &&
            nat.LeaseEvidence is NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry or NatLeaseEvidenceKind.RouterConfirmedPermanent)
        {
            return nat.ExternalReachability switch
            {
                NatDisplayReachability.Reachable => Value("外部到達確認済み", "到達済", confirmed: true),
                NatDisplayReachability.Unreachable => Value("外部到達不可", "到達NG", failure: true),
                _ => Value("マッピング済み・外部到達未確認", "割当?"),
            };
        }
        return nat.State switch
        {
            NatDisplayState.Discovering => Value("ルーター検出中", "検出中"),
            NatDisplayState.Mapping or NatDisplayState.Unmapping => Value("開放・解除処理中", "処理中"),
            NatDisplayState.ResultUnknown => Value("処理結果未確認", "結果?", failure: true),
            NatDisplayState.Rejected or NatDisplayState.RouterNotFound or NatDisplayState.PortConflict => Value("マッピングエラー", "エラー", failure: true),
            _ => Value("待受中・到達未確認", "待受"),
        };
    }

    // The full accessible text above keeps distinctions that cannot fit in the compact toolbar.
    internal static string Display(GenesisGuiNextTopStatusValue value, bool compact) => compact ? value.Compact : value.Text switch
    {
        "マッピング済み・外部到達未確認" => "割当済/未確認",
        "待受中・到達未確認" => "待受中/未確認",
        "外部到達確認済み" => "到達確認済",
        "開放・解除処理中" => "処理中",
        _ => value.Text,
    };
}
