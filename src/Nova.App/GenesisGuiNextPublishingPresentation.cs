namespace PeerCast.Nova.App;

internal static class GenesisGuiNextPublishingPresentation
{
    internal const string StartLabel = "配信開始";
    internal const string FinishLabel = "配信終了";
    internal const string InterruptLabel = "配信中断";
    internal const string ScopeText = "待受の開始要求 · 終了＝Novaローカル（OBS正常EOSとは別） · 中断＝キャンセル終了 · 中断完了後に再開始可能（cleanup・世代証明等の安全条件成立時のみ）";
    internal static readonly IReadOnlyList<GenesisNativeListColumnPresentation> Columns = Array.AsReadOnly(
        new GenesisNativeListColumnPresentation[]
        {
            new("状態", 210, GenesisChannelColumnAlignment.Left),
            new("ChannelId", 280, GenesisChannelColumnAlignment.Left),
            new("SessionId", 280, GenesisChannelColumnAlignment.Left),
            new("エラー分類 / 終了理由", 300, GenesisChannelColumnAlignment.Left),
        });

    internal static string[] Values(NovaObsPublishingReadOnlySnapshot snapshot) =>
        [snapshot.StateText, snapshot.ChannelId?.ToString("N").ToUpperInvariant() ?? "未接続",
            snapshot.SessionId?.ToString("N").ToUpperInvariant() ?? "未接続", ErrorText(snapshot)];

    internal static string ErrorText(NovaObsPublishingReadOnlySnapshot snapshot)
    {
        var source = snapshot.RejectionStage is not null && snapshot.RejectionReason is not null
            ? $"{snapshot.RejectionStage} / {snapshot.RejectionReason}"
            : snapshot.EndReason?.ToString() ?? "—";
        var error = snapshot.LifecycleFailure is { } failure && failure != NovaObsPublishingLifecycleFailure.None
            ? $"{failure} / {source}" : source;
        return error + (snapshot.CompletionKind is { } kind && kind != NovaObsPublishingCompletionKind.None ? $" · 終了結果: {kind}" : string.Empty) +
            (snapshot.CleanupCompleted is { } cleaned ? cleaned ? " · 世代cleanup証拠あり（再開始許可とは別）" : " · 世代cleanup未証明" : string.Empty) +
            (snapshot.WriterEvidence is { } writer ? $" · 録画writer証拠: {writer}（最終化未証明）" : string.Empty);
    }
}
