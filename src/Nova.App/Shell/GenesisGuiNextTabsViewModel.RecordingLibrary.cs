namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextTabsViewModel
{
    private IGenesisRecordingLibraryController? _recordingLibraryController;
    private GenesisRecordingLibrarySnapshot _recordingLibrarySnapshot =
        new(0, GenesisRecordingLibraryObservationState.NotObservedYet, [], [], 0, false, 0, false);
    private IReadOnlyList<GenesisGuiNextRecordingLibraryRow> _recordingLibraryRows = [];
    private CancellationTokenSource? _recordingLibraryCancellation;
    private TaskCompletionSource _recordingLibraryDrained = CreateCompletedDrainSource();
    private bool _isRecordingLibraryRefreshing;
    private bool _recordingLibraryFailed;

    // Composition only. GetSnapshot is a pure query, never a filesystem observation.
    internal void BindRecordingLibraryController(IGenesisRecordingLibraryController? controller)
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed || _recordingLibraryController is not null || controller is null) return;
            _recordingLibraryController = controller;
            _recordingLibrarySnapshot = controller.GetSnapshot();
            _recordingLibraryRows = GenesisGuiNextRecordingLibraryProjection.CreateRows(_recordingLibrarySnapshot);
        }
    }

    public GenesisRecordingLibrarySnapshot RecordingLibrarySnapshot
    {
        get { lock (_recordingRulesLifecycleGate) return _recordingLibrarySnapshot; }
    }

    public IReadOnlyList<GenesisGuiNextRecordingLibraryRow> RecordingLibraryRows
    {
        get { lock (_recordingRulesLifecycleGate) return _recordingLibraryRows; }
    }

    public bool IsRecordingLibraryRefreshing
    {
        get { lock (_recordingRulesLifecycleGate) return _isRecordingLibraryRefreshing; }
    }

    public bool IsRecordingLibraryPage => SelectedTab == GenesisGuiNextTab.Recording &&
        RecordingPageMode == GenesisGuiNextRecordingPageMode.Library;

    public bool CanRefreshRecordingLibrary
    {
        get
        {
            lock (_recordingRulesLifecycleGate)
                return !_disposed && IsRecordingLibraryPage && _recordingLibraryController is not null &&
                    !_isRecordingLibraryRefreshing;
        }
    }

    public string RecordingLibraryStatusText
    {
        get
        {
            lock (_recordingRulesLifecycleGate)
                return GenesisGuiNextRecordingLibraryProjection.StatusText(_recordingLibrarySnapshot,
                    _isRecordingLibraryRefreshing, _recordingLibraryFailed);
        }
    }

    public Task RefreshRecordingLibraryAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource cancellation;
        TaskCompletionSource drained;
        lock (_recordingRulesLifecycleGate)
        {
            if (!CanRefreshRecordingLibrary || cancellationToken.IsCancellationRequested) return Task.CompletedTask;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _recordingLibraryCancellation = cancellation;
            _recordingLibraryDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            drained = _recordingLibraryDrained;
            _isRecordingLibraryRefreshing = true;
            _recordingLibraryFailed = false;
            NotifyRecordingLibraryRefresh();
        }
        // Even an injected synchronous controller cannot block a native message handler.
        return Task.Run(() => RefreshRecordingLibraryCoreAsync(cancellation, drained), CancellationToken.None);
    }

    private async Task RefreshRecordingLibraryCoreAsync(CancellationTokenSource cancellation, TaskCompletionSource drained)
    {
        try
        {
            var result = await _recordingLibraryController!.RefreshAsync(cancellation.Token).ConfigureAwait(false);
            lock (_recordingRulesLifecycleGate)
            {
                if (_disposed || cancellation.IsCancellationRequested ||
                    result.Status == GenesisRecordingLibraryRefreshStatus.Cancelled) return;
                if (!ReferenceEquals(_recordingLibrarySnapshot, result.Snapshot))
                {
                    var rows = GenesisGuiNextRecordingLibraryProjection.CreateRows(result.Snapshot);
                    _recordingLibrarySnapshot = result.Snapshot;
                    _recordingLibraryRows = rows;
                    OnPropertyChanged(nameof(RecordingLibrarySnapshot));
                    OnPropertyChanged(nameof(RecordingLibraryRows));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            lock (_recordingRulesLifecycleGate)
                if (!_disposed && !cancellation.IsCancellationRequested) _recordingLibraryFailed = true;
        }
        finally
        {
            lock (_recordingRulesLifecycleGate)
            {
                _isRecordingLibraryRefreshing = false;
                _recordingLibraryCancellation = null;
                if (!_disposed) NotifyRecordingLibraryRefresh();
                cancellation.Dispose();
                drained.TrySetResult();
            }
        }
    }

    private void NotifyRecordingLibraryRefresh()
    {
        OnPropertyChanged(nameof(IsRecordingLibraryRefreshing));
        OnPropertyChanged(nameof(CanRefreshRecordingLibrary));
        OnPropertyChanged(nameof(RecordingLibraryStatusText));
    }
}
