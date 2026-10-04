using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal sealed record NovaPortMappingView(long Generation, bool Enabled, string Summary, string Diagnostics)
{
    public static NovaPortMappingView Create(NatPortMappingDisplay state, bool environmentOverride)
    {
        var mapping = state.State switch
        {
            NatDisplayState.Disabled => "無効",
            NatDisplayState.Discovering => "ルーター候補を確認中",
            NatDisplayState.Idle => "公開 listener 待ち",
            NatDisplayState.Mapping => "開放要求中",
            NatDisplayState.Mapped => "開放済み",
            NatDisplayState.Renewing => "更新要求中",
            NatDisplayState.Unmapping => "閉鎖要求中",
            NatDisplayState.UnmapUnconfirmed => "閉鎖未確認",
            NatDisplayState.ResultUnknown => "状態未確認",
            _ => "要求が拒否されました",
        };
        var ports = state.RegisteredInternalPorts.Count == 0
            ? "不明" : string.Join(", ", state.RegisteredInternalPorts);
        var externalPort = state.ExternalPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "不明";
        var lease = state.Lease;
        var confirmed = lease?.ConfirmedLifetimeSeconds is { } lifetime
            ? $"{lifetime}s" : "未確認";
        var expiry = lease is null ? "不明" : lease.EstimatedExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture);
        var evidence = state.LeaseEvidence switch
        {
            NatLeaseEvidenceKind.RequestedEstimate => "要求からの推定",
            NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry => "ルーター応答確認・期限は推定",
            _ => "応答なし",
        };
        var observed = state.LastObservation?.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "なし";
        var reachability = state.ExternalReachability switch
        {
            NatDisplayReachability.NotChecked => "未確認（検査未実施）",
            NatDisplayReachability.Reachable => "到達確認済み",
            NatDisplayReachability.Unreachable => "到達検査で接続できませんでした",
            _ => "不明",
        };
        var diagnostic = state.Diagnostics.LastOrDefault(item =>
            !item.Historical && item.Code is not ("Disabled" or "SettingChanged"));
        var lastDiagnostic = diagnostic is null ? "観測なし" : $"[{diagnostic.Code}] {DiagnosticSummary(diagnostic.Code)}";
        var summary = $"ポート自動開放: {(state.Enabled ? "ON" : "OFF")} / {state.Backend}\r\n" +
            $"ルーター設定: {mapping}    外部接続: {reachability}\r\n" +
            $"内部 TCP: {state.Network?.InternalAddress ?? "不明"}:{ports}    外部アドレス: {state.ExternalAddress ?? "不明"}    外部ポート（応答値）: {externalPort}\r\n" +
            $"lease: 要求 {lease?.RequestedLifetimeSeconds ?? state.RequestedLifetimeSeconds}s / 確認 {confirmed}\r\n" +
            $"推定期限: {expiry} / {evidence}\r\n" +
            $"最終観測: {observed} / 表示更新: {state.CapturedAt.ToLocalTime():HH:mm:ss}\r\n" +
            $"診断: {lastDiagnostic}" +
            (environmentOverride ? "\r\n起動時に環境変数を適用。GUI変更を保存し、次回起動でも環境変数が優先されます。" : "");
        var diagnostics = string.Join("\r\n", state.Diagnostics.Select(item =>
            $"{item.Timestamp.ToLocalTime():HH:mm:ss} {(item.Historical ? "旧世代・旧ネットワーク " : "")}[{item.Code}] {DiagnosticSummary(item.Code)}"));
        return new(state.Generation, state.Enabled, summary, diagnostics);
    }

    internal static string DiagnosticSummary(string code) => code switch
    {
        "RouterNotFound" => "対応するルーター候補が見つかりません（経路未確定）",
        "DiscoveryTimeout" => "ルーター候補の確認が時間内に完了しませんでした",
        "MapInProgress" => "開放要求中",
        "MappingEstablished" => "ルーター開放状態を確認しました",
        "MappingObserved" => "開放応答を観測しました",
        "MapResultUnknown" => "開放要求の結果は不明です",
        "RenewInProgress" => "lease 更新要求中",
        "RenewResultUnknown" => "更新要求の結果は不明です",
        "UnmapInProgress" => "閉鎖要求中",
        "UnmapUnconfirmed" => "ルーター側の閉鎖を確認できていません",
        "UnmapObserved" => "閉鎖応答を観測しました",
        "NetworkChanged" => "ネットワーク識別が変更されました",
        "StaleObservation" => "旧世代・旧ネットワークの観測（現在の状態とは別）",
        "ExternalPortRemapped" => "ルーターが別の外部ポートを返しました",
        "LifetimeShortened" => "ルーターが要求より短い lease を返しました",
        "ResponseTimeout" => "最終操作: 応答タイムアウト（結果不明）",
        "RouterRejected" => "ルーターが要求を拒否しました",
        "Disabled" => "自動ポート開放は OFF です",
        "SettingChanged" => "desired state の設定が変更されました",
        _ => code,
    };
}
