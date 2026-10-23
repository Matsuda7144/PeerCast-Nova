namespace PeerCast.Nova.App;

internal static class GenesisGuiNextPublishingPresentation
{
    internal const string ScopeText = "読み取り専用 · 配信開始/停止・OBS接続・WAN・NAT操作は未対応";
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

    internal static string ErrorText(NovaObsPublishingReadOnlySnapshot snapshot) =>
        snapshot.RejectionStage is not null && snapshot.RejectionReason is not null
            ? $"{snapshot.RejectionStage} / {snapshot.RejectionReason}"
            : snapshot.EndReason?.ToString() ?? "—";
}

internal sealed partial class GenesisGuiNextTabsViewModel
{
    private INovaObsPublishingReadOnlyStatus _publishingStatus = new NovaObsPublishingReadOnlyStatus(null);
    private System.Collections.ObjectModel.ReadOnlyCollection<NovaObsPublishingReadOnlySnapshot> _publishingRows = Array.AsReadOnly(Array.Empty<NovaObsPublishingReadOnlySnapshot>());
    internal IReadOnlyList<NovaObsPublishingReadOnlySnapshot> PublishingRows => _publishingRows;

    internal void BindPublishingStatus(INovaObsPublishingReadOnlyStatus? status)
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed) return;
            _publishingStatus = status ?? new NovaObsPublishingReadOnlyStatus(null);
        }
        RefreshPublishingStatus();
    }

    internal void RefreshPublishingStatus()
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed) return;
            var snapshot = _publishingStatus.Read();
            if (_publishingRows.Count == 1 && _publishingRows[0] == snapshot) return;
            _publishingRows = Array.AsReadOnly(new[] { snapshot });
        }
        OnPropertyChanged(nameof(PublishingRows));
    }
}
