namespace PeerCast.Nova.App;

internal static class GenesisGuiNextPublishingPresentation
{
    internal const string StartLabel = "配信開始";
    internal const string InterruptLabel = "配信中断";
    internal const string ScopeText = "待受の開始要求 · 中断はキャンセルによる終了 · 正常停止・再開始は未対応";
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
        return snapshot.LifecycleFailure is { } failure && failure != NovaObsPublishingLifecycleFailure.None
            ? $"{failure} / {source}" : source;
    }
}

internal sealed partial class GenesisGuiNextTabsViewModel
{
    private INovaObsPublishingReadOnlyStatus _publishingStatus = new NovaObsPublishingReadOnlyStatus(null);
    private INovaGuiNextPublishingCommands? _publishingCommands;
    internal NovaGuiNextPublishingCommandState PublishingCommands { get; private set; } = NovaGuiNextPublishingCommandState.Unavailable;
    private System.Collections.ObjectModel.ReadOnlyCollection<NovaObsPublishingReadOnlySnapshot> _publishingRows = Array.AsReadOnly(Array.Empty<NovaObsPublishingReadOnlySnapshot>());
    internal IReadOnlyList<NovaObsPublishingReadOnlySnapshot> PublishingRows => _publishingRows;

    internal void BindPublishingStatus(INovaObsPublishingReadOnlyStatus? status, INovaGuiNextPublishingCommands? commands = null)
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed) return;
            _publishingStatus = status ?? new NovaObsPublishingReadOnlyStatus(null);
            _publishingCommands = commands;
        }
        RefreshPublishingStatus();
    }

    internal void RefreshPublishingStatus()
    {
        bool rowsChanged;
        bool commandsChanged;
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed) return;
            var snapshot = _publishingStatus.Read();
            rowsChanged = _publishingRows.Count != 1 || _publishingRows[0] != snapshot;
            if (rowsChanged) _publishingRows = Array.AsReadOnly(new[] { snapshot });
            var commands = _publishingCommands?.ReadCommands() ?? NovaGuiNextPublishingCommandState.Unavailable;
            commandsChanged = PublishingCommands != commands;
            PublishingCommands = commands;
        }
        if (rowsChanged) OnPropertyChanged(nameof(PublishingRows));
        if (commandsChanged) OnPropertyChanged(nameof(PublishingCommands));
    }

    internal Task RequestPublishingStartAsync() => RequestPublishingCommandAsync(start: true);
    internal Task RequestPublishingInterruptAsync() => RequestPublishingCommandAsync(start: false);

    private async Task RequestPublishingCommandAsync(bool start)
    {
        Task operation;
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed || SelectedTab != GenesisGuiNextTab.Publishing || _publishingCommands is null) return;
            operation = start ? _publishingCommands.RequestStartAsync() : _publishingCommands.RequestInterruptAsync();
        }
        RefreshPublishingStatus();
        try { await operation.ConfigureAwait(false); }
        finally { RefreshPublishingStatus(); } // Disposed tabs reject late UI updates.
    }
}
