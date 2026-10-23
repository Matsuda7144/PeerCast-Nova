namespace PeerCast.Nova.App;

internal sealed record GenesisChannelCatalogRefreshDiagnostics(
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? LastSuccessfulAt,
    GenesisChannelCatalogRefreshResult? LastResult)
{
    public static GenesisChannelCatalogRefreshDiagnostics Empty { get; } =
        new(null, null, null);
}

/// <summary>
/// Owns the finite lifetime of Genesis channel-catalog refresh work. Refreshes
/// are serialized so initial, manual, and automatic refresh requests cannot race.
/// </summary>
internal sealed class GenesisChannelCatalogSession : IAsyncDisposable
{
    internal static TimeSpan DefaultAutoRefreshInterval { get; } =
        TimeSpan.FromSeconds(60);

    private readonly object _sync = new();
    private readonly GenesisChannelCatalogCoordinator _coordinator;
    private readonly GenesisMainViewModel _viewModel;
    private readonly GenesisYellowPageRefreshDiagnosticTrace? _diagnosticTrace;
    private readonly IGenesisAutomaticRecordingCatalogObserver?
        _automaticRecordingCatalogObserver;
    private readonly CancellationTokenSource _lifetimeCancellation;
    private Task _tail = Task.CompletedTask;
    private Task _autoRefreshTask = Task.CompletedTask;
    private GenesisChannelCatalogRefreshDiagnostics _diagnostics =
        GenesisChannelCatalogRefreshDiagnostics.Empty;
    private int _initialRefreshStarted;
    private int _autoRefreshStarted;
    private int _disposed;

    private GenesisChannelCatalogSession(
        GenesisChannelCatalogCoordinator coordinator,
        GenesisMainViewModel viewModel,
        GenesisGuiNextTabsViewModel? guiNextTabs,
        GenesisYellowPageRefreshDiagnosticTrace? diagnosticTrace,
        CancellationToken cancellationToken)
    {
        _coordinator = coordinator;
        _viewModel = viewModel;
        _diagnosticTrace = diagnosticTrace;
        _automaticRecordingCatalogObserver =
            viewModel.AutomaticRecordingCatalogObserver;
        GuiNextTabs = guiNextTabs;
        _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        if (GuiNextTabs is not null)
        {
            GuiNextTabs.BindRecordingLibraryController(viewModel.RecordingLibraryController);
            GuiNextTabs.BindRecordingHistoryController(viewModel.RecordingHistoryController);
            TryUpdateGuiNextPlayback();
            TryUpdateGuiNextActiveRecordings();
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    internal GenesisGuiNextTabsViewModel? GuiNextTabs { get; }

    internal GenesisYellowPageRefreshDiagnosticTrace? DiagnosticTrace => _diagnosticTrace;

    internal event EventHandler? DiagnosticsChanged;

    internal GenesisChannelCatalogRefreshDiagnostics Diagnostics
    {
        get
        {
            lock (_sync)
            {
                return _diagnostics;
            }
        }
    }

    public static GenesisChannelCatalogSession Start(
        IGenesisChannelCatalog catalog,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        var session = Create(catalog, viewModel, cancellationToken);
        session.StartInitialRefresh();
        session.StartAutoRefresh(DefaultAutoRefreshInterval);
        return session;
    }

    internal static GenesisChannelCatalogSession Create(
        IGenesisChannelCatalog catalog,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken = default) =>
        Create(
            catalog,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            cancellationToken);

    internal static GenesisChannelCatalogSession Create(
        IGenesisChannelCatalog catalog,
        GenesisMainViewModel viewModel,
        IGenesisChannelPresenceNotificationPublisher notificationPublisher,
        CancellationToken cancellationToken = default) =>
        Create(
            catalog,
            viewModel,
            notificationPublisher,
            diagnosticTrace: null,
            cancellationToken);

    internal static GenesisChannelCatalogSession Create(
        IGenesisChannelCatalog catalog,
        GenesisMainViewModel viewModel,
        IGenesisChannelPresenceNotificationPublisher notificationPublisher,
        GenesisYellowPageRefreshDiagnosticTrace? diagnosticTrace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(notificationPublisher);

        return new GenesisChannelCatalogSession(
            new GenesisChannelCatalogCoordinator(
                catalog,
                viewModel,
                notificationPublisher,
                diagnosticTrace),
            viewModel,
            notificationPublisher is GenesisGuiNextChannelPresencePublisher guiNextPublisher
                ? guiNextPublisher.Tabs
                : null,
            diagnosticTrace,
            cancellationToken);
    }

    internal void StartInitialRefresh()
    {
        if (Interlocked.Exchange(ref _initialRefreshStarted, 1) == 0)
        {
            _ = QueueRefresh(
                GenesisYellowPageRefreshTrigger.Initial,
                cancellationToken: CancellationToken.None);
        }
    }

    internal void StartAutoRefresh(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "Automatic channel refresh interval must be positive.");
        }

        if (Volatile.Read(ref _disposed) != 0 ||
            _lifetimeCancellation.IsCancellationRequested ||
            Interlocked.Exchange(ref _autoRefreshStarted, 1) != 0)
        {
            return;
        }

        _autoRefreshTask = RunAutoRefreshAsync(interval);
    }

    public ValueTask<GenesisChannelCatalogRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        new(QueueRefresh(
            GenesisYellowPageRefreshTrigger.Manual,
            cancellationToken: cancellationToken));

    internal ValueTask<GenesisChannelCatalogRefreshResult> RefreshAsync(
        Guid refreshRequestId,
        CancellationToken cancellationToken = default) =>
        new(QueueRefresh(
            GenesisYellowPageRefreshTrigger.Manual,
            refreshRequestId,
            cancellationToken));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (GuiNextTabs is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            GuiNextTabs.UpdateActiveRecordings(
                Array.Empty<GenesisGuiNextActiveRecording>(),
                isAvailable: false);
        }

        try
        {
            _lifetimeCancellation.Cancel();
        }
        catch
        {
            // Cancellation callbacks belong to catalog implementations. A callback
            // failure must not prevent the session from draining finite refresh work.
        }

        Task tail;
        Task autoRefreshTask;
        lock (_sync)
        {
            tail = _tail;
            autoRefreshTask = _autoRefreshTask;
        }

        try
        {
            await Task.WhenAll(tail, autoRefreshTask).ConfigureAwait(false);
        }
        catch
        {
            // Coordinator converts expected catalog failures to bounded results and
            // the automatic loop treats lifetime cancellation as normal shutdown.
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            if (_diagnosticTrace is not null)
            {
                await _diagnosticTrace.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task RunAutoRefreshAsync(TimeSpan interval)
    {
        while (Volatile.Read(ref _disposed) == 0 &&
            !_lifetimeCancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _lifetimeCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (Volatile.Read(ref _disposed) != 0 ||
                _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            _ = await QueueRefresh(
                    GenesisYellowPageRefreshTrigger.Automatic,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private Task<GenesisChannelCatalogRefreshResult> QueueRefresh(
        GenesisYellowPageRefreshTrigger trigger,
        Guid? requestedId = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = requestedId is { } suppliedId && suppliedId != Guid.Empty
            ? suppliedId
            : Guid.NewGuid();
        var diagnosticContext = _diagnosticTrace is null
            ? null
            : new GenesisYellowPageRefreshDiagnosticContext(
                _diagnosticTrace,
                requestId,
                trigger);
        _diagnosticTrace?.TryRecord(
            "RefreshRequested",
            diagnosticContext,
            stage: "Request",
            outcome: "Requested");

        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                _lifetimeCancellation.IsCancellationRequested ||
                cancellationToken.IsCancellationRequested)
            {
                _diagnosticTrace?.TryRecord(
                    "RefreshSuppressed",
                    diagnosticContext,
                    stage: "Queue",
                    outcome: "Suppressed",
                    suppressionReason: "SessionDisposedOrCancelled");
                return Task.FromResult(GenesisChannelCatalogRefreshResult.Cancelled);
            }

            _diagnosticTrace?.TryRecord(
                "RefreshQueued",
                diagnosticContext,
                stage: "Queue",
                outcome: "Queued");
            var completion = new TaskCompletionSource<GenesisChannelCatalogRefreshResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _tail = RefreshAfterAsync(
                _tail,
                completion,
                diagnosticContext,
                cancellationToken);
            return completion.Task;
        }
    }

    private async Task RefreshAfterAsync(
        Task predecessor,
        TaskCompletionSource<GenesisChannelCatalogRefreshResult> completion,
        GenesisYellowPageRefreshDiagnosticContext? diagnosticContext,
        CancellationToken requestCancellationToken)
    {
        try
        {
            try
            {
                await predecessor.ConfigureAwait(false);
            }
            catch
            {
                // A prior refresh cannot prevent a later bounded refresh attempt.
            }

            if (Volatile.Read(ref _disposed) != 0 ||
                _lifetimeCancellation.IsCancellationRequested ||
                requestCancellationToken.IsCancellationRequested)
            {
                _diagnosticTrace?.TryRecord(
                    "RefreshSuppressed",
                    diagnosticContext,
                    stage: "Queue",
                    outcome: "Suppressed",
                    suppressionReason: "CancelledBeforeRead");
                completion.TrySetResult(GenesisChannelCatalogRefreshResult.Cancelled);
                return;
            }

            using var diagnosticScope = diagnosticContext is null
                ? null
                : GenesisYellowPageRefreshDiagnosticAmbient.Push(diagnosticContext);
            RecordAttempt(DateTimeOffset.UtcNow);
            GenesisChannelCatalogRefreshResult result;
            try
            {
                using var refreshCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        _lifetimeCancellation.Token,
                        requestCancellationToken);
                result = await _coordinator.RefreshAsync(refreshCancellation.Token)
                    .ConfigureAwait(false);
                if (result == GenesisChannelCatalogRefreshResult.Completed)
                {
                    if (_automaticRecordingCatalogObserver is not null &&
                        _coordinator.ChannelViews is { } channelViews)
                    {
                        try
                        {
                            _ = await _automaticRecordingCatalogObserver.ObserveAsync(
                                    channelViews.Channels,
                                    refreshCancellation.Token)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            // Automatic recording is auxiliary to catalog display.
                            // Its failure must not turn a successful refresh into a
                            // failed channel-list update.
                        }
                    }

                    try
                    {
                        await _viewModel.RefreshActiveRecordingsAsync(
                                refreshCancellation.Token)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Recording projection is auxiliary to catalog display.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result = GenesisChannelCatalogRefreshResult.Cancelled;
            }
            catch
            {
                result = GenesisChannelCatalogRefreshResult.Faulted;
            }

            RecordResult(result, DateTimeOffset.UtcNow);
            completion.TrySetResult(result);
        }
        catch
        {
            completion.TrySetResult(GenesisChannelCatalogRefreshResult.Faulted);
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (string.Equals(
                e.PropertyName,
                nameof(GenesisMainViewModel.PlaybackSnapshot),
                StringComparison.Ordinal))
        {
            TryUpdateGuiNextPlayback();
        }

        if (string.Equals(
                e.PropertyName,
                nameof(GenesisMainViewModel.ActiveRecordings),
                StringComparison.Ordinal) ||
            string.Equals(
                e.PropertyName,
                nameof(GenesisMainViewModel.RecordingPresentationRevision),
                StringComparison.Ordinal))
        {
            TryUpdateGuiNextActiveRecordings();
        }
    }

    private void TryUpdateGuiNextPlayback()
    {
        try
        {
            GuiNextTabs?.UpdatePlayback(_viewModel.PlaybackSnapshot);
        }
        catch
        {
            // Playing-tab observers are auxiliary GUI surfaces and must not turn
            // a playback state transition into a playback command failure.
        }
    }

    private void TryUpdateGuiNextActiveRecordings()
    {
        try
        {
            GuiNextTabs?.UpdateSuppression(_viewModel.SuppressionRows);
            GuiNextTabs?.UpdateActiveRecordings(
                _viewModel.ActiveRecordings,
                _viewModel.ActiveRecordingSnapshotAvailable);
        }
        catch
        {
            // Recording-page projection is auxiliary and cannot affect Core state.
        }
    }

    private void RecordAttempt(DateTimeOffset attemptedAt)
    {
        lock (_sync)
        {
            _diagnostics = _diagnostics with
            {
                LastAttemptedAt = attemptedAt,
            };
        }

        RaiseDiagnosticsChanged();
    }

    private void RecordResult(
        GenesisChannelCatalogRefreshResult result,
        DateTimeOffset completedAt)
    {
        lock (_sync)
        {
            _diagnostics = _diagnostics with
            {
                LastSuccessfulAt = result == GenesisChannelCatalogRefreshResult.Completed
                    ? completedAt
                    : _diagnostics.LastSuccessfulAt,
                LastResult = result,
            };
        }

        RaiseDiagnosticsChanged();
    }

    private void RaiseDiagnosticsChanged()
    {
        var handlers = DiagnosticsChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // Diagnostics observers are auxiliary GUI surfaces and must not
                // turn a bounded catalog refresh into a refresh failure.
            }
        }
    }
}
