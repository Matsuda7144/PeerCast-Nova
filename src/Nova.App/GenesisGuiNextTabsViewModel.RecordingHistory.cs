namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextTabsViewModel
{
    private IGenesisRecordingHistoryController? _recordingHistoryController;
    private GenesisRecordingHistorySnapshot? _recordingHistorySnapshot;
    private GenesisRecordingHistoryDurabilitySnapshot? _recordingHistoryDurability;
    private GenesisRecordingLibrarySnapshot? _historyLibrarySnapshot;

    private IReadOnlyList<GenesisGuiNextRecordingHistoryRow> _recordingHistoryRows = [];
    private GenesisRecordingHistoryPresentation _recordingHistoryStatus =
        new(GenesisGuiNextRecordingHistoryProjection.UnavailableText, true);
    private TaskCompletionSource _recordingHistoryDrained = CreateCompletedDrainSource();
    private CancellationTokenSource _recordingHistoryCancellation = new();
    private long _recordingHistoryGeneration;
    private bool _recordingHistoryInFlight;
    private bool _recordingHistoryPending;
    private CancellationToken _recordingHistoryPendingCancellation;
    private bool _recordingHistoryShutdown;

    internal void BindRecordingHistoryController(IGenesisRecordingHistoryController? controller)
    {
        lock (_recordingRulesLifecycleGate)
            if (!_disposed && _recordingHistoryController is null) _recordingHistoryController = controller;
    }

    public bool IsRecordingHistoryPage => SelectedTab == GenesisGuiNextTab.Recording &&
        RecordingPageMode == GenesisGuiNextRecordingPageMode.History;
    public bool CanRefreshRecordingHistory
    {
        get { lock (_recordingRulesLifecycleGate) return !_disposed && IsRecordingHistoryPage && _recordingHistoryController is not null; }
    }
    public IReadOnlyList<GenesisGuiNextRecordingHistoryRow> RecordingHistoryRows
    {
        get { lock (_recordingRulesLifecycleGate) return _recordingHistoryRows; }
    }
    public GenesisRecordingHistoryPresentation RecordingHistoryStatus
    {
        get { lock (_recordingRulesLifecycleGate) return _recordingHistoryStatus; }
    }
    public string RecordingHistoryDetails(GenesisGuiNextRecordingHistoryRow? row)
    {
        lock (_recordingRulesLifecycleGate)
            return GenesisGuiNextRecordingHistoryProjection.Bound(
                (row?.Details ?? "録画履歴を選択してください") + "\r\n" +
                GenesisGuiNextRecordingHistoryProjection.DurabilityDetails(_recordingHistoryDurability) +
                "\r\n" + _recordingHistoryStatus.Text + "\r\n" + _recordingHistoryStatus.WarningDetails, GenesisGuiNextRecordingHistoryProjection.DetailsLimit);
    }

    // Called on tab/mode changes. An old blocked query may finish, but cannot publish.
    private void RecordingHistoryPageChanged()
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed) return;
            _recordingHistoryGeneration++;
            _recordingHistoryPending = false;
            _recordingHistoryCancellation.Cancel();
            _recordingHistoryCancellation.Dispose();
            _recordingHistoryCancellation = new();
        }
        if (IsRecordingHistoryPage) _ = RefreshRecordingHistoryAsync();
    }

    public Task RefreshRecordingHistoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (!CanRefreshRecordingHistory || cancellationToken.IsCancellationRequested) return Task.CompletedTask;
            if (_recordingHistoryInFlight)
            {
                _recordingHistoryPending = true;
                _recordingHistoryPendingCancellation = cancellationToken;
                return _recordingHistoryDrained.Task;
            }
            _recordingHistoryInFlight = true;
            _recordingHistoryDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            // Capture the task before a synchronous worker can finish.
            var drained = _recordingHistoryDrained;
            _ = Task.Run(() => QueryRecordingHistoryAsync(drained, cancellationToken), CancellationToken.None);
            return drained.Task;
        }
    }

    private void QueryRecordingHistoryAsync(TaskCompletionSource drained, CancellationToken callerCancellation)
    {
        try
        {
            while (true)
            {
                long generation;
                CancellationToken modeCancellation;
                lock (_recordingRulesLifecycleGate)
                {
                    generation = _recordingHistoryGeneration;
                    modeCancellation = _recordingHistoryCancellation.Token;
                    _recordingHistoryPending = false;
                }
                try
                {
                    callerCancellation.ThrowIfCancellationRequested();
                    modeCancellation.ThrowIfCancellationRequested();
                    var history = _recordingHistoryController!.GetRecordingHistorySnapshot();
                    callerCancellation.ThrowIfCancellationRequested();
                    modeCancellation.ThrowIfCancellationRequested();
                    var durability = _recordingHistoryController.GetRecordingHistoryDurabilitySnapshot();
                    var library = _recordingHistoryController.GetRecordingLibrarySnapshot();
                    IReadOnlyList<GenesisGuiNextRecordingHistoryRow>? rows = null;
                    lock (_recordingRulesLifecycleGate)
                        if (_recordingHistorySnapshot?.Entries.SequenceEqual(history.Entries) == true &&
                            ReferenceEquals(_historyLibrarySnapshot, library))
                            rows = _recordingHistoryRows;
                    rows ??= GenesisGuiNextRecordingHistoryProjection.CreateRows(history, library);
                    lock (_recordingRulesLifecycleGate)
                    {
                        if (!_disposed && IsRecordingHistoryPage && generation == _recordingHistoryGeneration &&
                            !modeCancellation.IsCancellationRequested && !callerCancellation.IsCancellationRequested)
                        {
                            // Equality includes immutable facts, path association and cached metadata;
                            // durability-only changes must not reconstruct the native row collection.
                            if (!_recordingHistoryRows.SequenceEqual(rows))
                            {
                                _recordingHistoryRows = rows;
                                OnPropertyChanged(nameof(RecordingHistoryRows));
                            }
                            _recordingHistorySnapshot = history;
                            _recordingHistoryDurability = durability;
                            _historyLibrarySnapshot = library;

                            PublishRecordingHistoryStatus(GenesisGuiNextRecordingHistoryProjection.Status(history, durability, false));
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    lock (_recordingRulesLifecycleGate)
                        if (!_disposed && IsRecordingHistoryPage && generation == _recordingHistoryGeneration &&
                            !modeCancellation.IsCancellationRequested && !callerCancellation.IsCancellationRequested)
                        {
                            if (error is InvalidDataException)
                            {
                                _recordingHistoryRows = [];
                                _recordingHistorySnapshot = null;
                                OnPropertyChanged(nameof(RecordingHistoryRows));
                            }
                            PublishRecordingHistoryStatus(GenesisGuiNextRecordingHistoryProjection.Status(
                                _recordingHistorySnapshot, _recordingHistoryDurability, true));
                        }
                }
                lock (_recordingRulesLifecycleGate)
                {
                    if (!_disposed && IsRecordingHistoryPage && _recordingHistoryPending)
                    {
                        callerCancellation = _recordingHistoryPendingCancellation;
                        continue;
                    }
                    _recordingHistoryInFlight = false;
                    drained.TrySetResult();
                    return;
                }
            }
        }
        finally
        {
            lock (_recordingRulesLifecycleGate)
            {
                if (ReferenceEquals(drained, _recordingHistoryDrained)) _recordingHistoryInFlight = false;
                drained.TrySetResult();
            }
        }
    }

    private void PublishRecordingHistoryStatus(GenesisRecordingHistoryPresentation status)
    {
        _recordingHistoryStatus = status;
        OnPropertyChanged(nameof(RecordingHistoryStatus));
    }

    private Task CancelRecordingHistoryForDisposal()
    {
        lock (_recordingRulesLifecycleGate)
        {
            _recordingHistoryGeneration++;
            _recordingHistoryPending = false;
            if (!_recordingHistoryShutdown)
            {
                _recordingHistoryShutdown = true;
                _recordingHistoryCancellation.Cancel();
            }
            return _recordingHistoryDrained.Task;
        }
    }
}
