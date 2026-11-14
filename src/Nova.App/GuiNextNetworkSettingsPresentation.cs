using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal enum GuiNextNetworkTone { Normal, Blue, Green, Orange, Purple }

internal sealed record GuiNextNetworkSettingsPresentation(
    bool Checked, bool CanEdit, string Settings, string Listener, string Mapping,
    string Diagnostics, string Notice, GuiNextNetworkTone SettingsTone,
    GuiNextNetworkTone ListenerTone, GuiNextNetworkTone MappingTone,
    NovaApplicationNetworkFailure Failure)
{
    internal const string AutoMappingUnavailable = "ポート自動開放設定：利用不可";
    public string IntegratedSettings { get; init; } = AutoMappingUnavailable;

    public static GuiNextNetworkSettingsPresentation Unavailable(string reason) =>
        new(false, false, AutoMappingUnavailable, "公開PCP listener：状態未確認",
            "NAT mapping：状態未確認\r\n外部到達性：未確認", string.Empty, reason,
            GuiNextNetworkTone.Orange, GuiNextNetworkTone.Orange, GuiNextNetworkTone.Orange,
            NovaApplicationNetworkFailure.NotInitialized);

    public static GuiNextNetworkSettingsPresentation Create(NovaApplicationNetworkSnapshot snapshot,
        int localPending = 0, NovaApplicationNetworkPersistenceResult? localResult = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var result = localResult is { RequestId: 0 } ? localResult :
            snapshot.LastPersistenceResult is { } authoritative &&
                (localResult is null || authoritative.RequestId >= localResult.RequestId)
                    ? authoritative : localResult;
        var failure = snapshot.SafeFailure != NovaApplicationNetworkFailure.None ? snapshot.SafeFailure :
            result?.Failure ?? NovaApplicationNetworkFailure.None;
        var canEdit = snapshot.Initialized && !snapshot.Disposing && !snapshot.Disposed && failure != NovaApplicationNetworkFailure.Closing;
        if (!canEdit && failure == NovaApplicationNetworkFailure.None)
            failure = snapshot.Disposing || snapshot.Disposed ? NovaApplicationNetworkFailure.Closing : NovaApplicationNetworkFailure.NotInitialized;
        var save = result?.Status switch
        {
            NovaApplicationNetworkPersistenceStatus.Pending => "保存中",
            NovaApplicationNetworkPersistenceStatus.Saved => "保存成功",
            NovaApplicationNetworkPersistenceStatus.Failed => "保存失敗（runtime設定は戻しません）",
            NovaApplicationNetworkPersistenceStatus.Rejected => "保存要求を受理できません",
            _ => "保存操作なし",
        };
        var pending = Math.Max(localPending, snapshot.PendingSaveCount);
        var settings = $"現在のruntime desired：{OnOff(snapshot.RuntimeDesiredEnabled)}    保存済み：{OnOff(snapshot.SavedAutoPortMapping)}\r\n" +
            $"環境変数override：{(snapshot.StartupEnvironmentOverride ? OnOff(snapshot.StartupEnvironmentEnabled) : "なし")}    次回起動時：{OnOff(snapshot.NextStartEffectiveEnabled)}\r\n" +
            $"保存待機：{pending}件    最新の保存結果：{save}\r\n" +
            (snapshot.StartupEnvironmentOverride ? "保存後も、次回起動では環境変数 NOVA_AUTO_PORT_MAPPING が優先されます。" : "次回起動時は保存済みの設定を使用します。");
        var listenerState = snapshot.PublicListenerAvailability switch
        {
            NovaApplicationPublicListenerAvailability.Starting => "開始中",
            NovaApplicationPublicListenerAvailability.Listening => "待受中",
            NovaApplicationPublicListenerAvailability.Stopping => "停止中",
            NovaApplicationPublicListenerAvailability.Failed => "失敗",
            _ => "未起動",
        };
        var relay = snapshot.PublicRelay;
        var endpoint = snapshot.PublicListenerAvailability == NovaApplicationPublicListenerAvailability.Listening &&
            relay is { State: NovaPublicRelayState.Listening, Endpoint: not null }
                ? $"実bound port：{relay.Endpoint.Port}" : "実bound port：なし";
        var listener = $"公開PCP listener：{listenerState}    {endpoint}\r\n" +
            $"共有参照（要求含む）：{relay?.ReferenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "未確認"}    ChannelId：{relay?.ChannelId?.ToString() ?? "なし"}" +
            (snapshot.PublicListenerAvailability == NovaApplicationPublicListenerAvailability.Failed ? $"    分類：{relay?.Failure}" : "");
        var nat = snapshot.Nat;
        if (snapshot.SharedPublicRelay is { } shared)
            listener = $"公開PCP listener：{shared.ListenerText}    " +
                (shared.State == GenesisSharedPublicRelayState.Listening ? $"実bound port：{shared.Port}" : "実bound port：なし") +
                $"\r\nチャンネル数：{shared.Channels}    {shared.MappingText}    {shared.Reachability}" +
                (shared.Error is null ? "" : $"\r\n{shared.Error}");
        // Reuse the existing coherent projection and Japanese diagnostics.
        // Bounded display only; old network observations remain marked historical.
        var view = nat is null ? null : NovaPortMappingView.Create(nat with
        {
            Diagnostics = Array.AsReadOnly(nat.Diagnostics.TakeLast(32).ToArray()),
        }, snapshot.StartupEnvironmentOverride);
        var mapping = view?.Summary ?? "NAT mapping：利用不可";
        mapping += "\r\nRouter ACK：" +
            (nat?.LeaseEvidence == NatLeaseEvidenceKind.RouterConfirmedPermanent ? "応答確認済み（期限なし）" :
                nat?.LeaseEvidence == NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry ? "応答確認済み（期限は推定）" : "未確認");
        mapping += "\r\n外部到達性：" + (nat?.ExternalReachability switch
        {
            NatDisplayReachability.Reachable => "到達確認済み",
            NatDisplayReachability.Unreachable => "到達検査で接続できませんでした",
            _ => "未確認",
        });
        var notice = failure != NovaApplicationNetworkFailure.None ? $"Network分類：{failure}" :
            snapshot.RuntimeDesiredEnabled == true && snapshot.PublicListenerAvailability == NovaApplicationPublicListenerAvailability.Unavailable
                ? "ポート自動開放：有効（公開listener待機中）。この画面は公開listenerを開始しません。"
                : "WAN Relayが起動時にONなら公開待受を開始し、Nova終了まで維持します。自動開放ONならルーターへ開放を要求します。外部到達検査は行いません。";
        return new(snapshot.RuntimeDesiredEnabled == true, canEdit, settings, listener, mapping,
            view?.Diagnostics ?? string.Empty, notice,
            failure != NovaApplicationNetworkFailure.None ? GuiNextNetworkTone.Orange :
                result?.Status == NovaApplicationNetworkPersistenceStatus.Saved ? GuiNextNetworkTone.Green : GuiNextNetworkTone.Blue,
            snapshot.PublicListenerAvailability == NovaApplicationPublicListenerAvailability.Failed ? GuiNextNetworkTone.Orange : GuiNextNetworkTone.Blue,
            nat?.State == NatDisplayState.Mapped && nat.LeaseEvidence is NatLeaseEvidenceKind.RouterConfirmedWithEstimatedExpiry or NatLeaseEvidenceKind.RouterConfirmedPermanent
                ? GuiNextNetworkTone.Green : GuiNextNetworkTone.Orange, failure)
        {
            IntegratedSettings = (snapshot.SharedPublicRelay is { } current
                ? $"WAN Relay: {current.ListenerText} / port {current.Port} / {current.MappingText} / {current.Reachability}"
                : "チェックは即時反映・非同期で自動保存（共通の保存・キャンセルとは別）") + "\r\n" +
                $"実行中の設定：{OnOff(snapshot.RuntimeDesiredEnabled)}    保存済み：{OnOff(snapshot.SavedAutoPortMapping)}    {save}" +
                (snapshot.StartupEnvironmentOverride
                    ? $"\r\nNOVA_AUTO_PORT_MAPPING={OnOff(snapshot.StartupEnvironmentEnabled)}（優先）    次回起動時：{OnOff(snapshot.NextStartEffectiveEnabled)}"
                    : string.Empty),
        };
    }

    private static string OnOff(bool? value) => value switch { true => "ON", false => "OFF", _ => "未確認" };
}
