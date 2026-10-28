namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextTabsViewModel
{
    private INovaObsPublishingReadOnlyStatus _publishingStatus = new NovaObsPublishingReadOnlyStatus(null);
    private INovaGuiNextPublishingCommands? _publishingCommands;
    private PublishingSettingsController? _publishingSettings;
    internal PublishingSettingsController? PublishingSettings { get { lock (_recordingRulesLifecycleGate) return _publishingSettings; } }
    private readonly object _publishingUiGate = new();
    private Action<Action>? _publishingDispatch;
    private Action? _publishingPublication;
    private Task? _publishingReader;
    private readonly HashSet<Task> _publishingOperations = [];
    private bool _publishingRequested, _publishingClosed;
    internal Task PublishingCommandAdmission { get; private set; } = Task.CompletedTask;
    private long _publishingVersion, _publishingReadSerial, _publishingPublishedSerial;
    private sealed record PublishingBinding(long Version, INovaObsPublishingReadOnlyStatus Source,
        INovaGuiNextPublishingCommands? Boundary, bool Initial);
    private PublishingBinding? _publishingBinding, _publishingPublishedBinding;
    // Deterministic worker-admission observation; never called under either gate.
    internal Func<Task>? PublishingCommandWorkerStarting { get; set; }
    private NovaGuiNextPublishingCommandState _publishingCommandState = NovaGuiNextPublishingCommandState.Unavailable;
    internal NovaGuiNextPublishingCommandState PublishingCommands
    {
        get
        {
            var state = _publishingCommandState;
            var settings = PublishingSettings;
            if (settings?.CanStart != false) return state;
            var reason = settings.LoadFailed ? PublishingSettingsController.LoadWarning : PublishingSettingsController.SaveOrCancel;
            return state with { CanStart = false, DiagnosticText = string.IsNullOrEmpty(state.DiagnosticText) ? reason : state.DiagnosticText };
        }
        private set => _publishingCommandState = value;
    }
    private System.Collections.ObjectModel.ReadOnlyCollection<NovaObsPublishingReadOnlySnapshot> _publishingRows = Array.AsReadOnly(Array.Empty<NovaObsPublishingReadOnlySnapshot>());
    internal IReadOnlyList<NovaObsPublishingReadOnlySnapshot> PublishingRows => _publishingRows;

    // Native installs a PostMessage dispatcher. With no UI attached, publication
    // is retained, never delivered from the read worker on an assumed UI thread.
    internal void SetPublishingDispatcher(Action<Action>? dispatch)
    {
        Action? publication;
        lock (_publishingUiGate)
        {
            lock (_recordingRulesLifecycleGate)
            {
                _publishingDispatch = dispatch;
                _publishingWindowLifetime++;
                ResetPublishingConnectionLocked();
                publication = _publishingPublication;
                _publishingPublication = null;
            }
        }
        if (dispatch is not null && publication is not null) dispatch(publication);
        if (dispatch is not null) RefreshPublishingStatus();
    }

    internal void BindPublishingStatus(INovaObsPublishingReadOnlyStatus? status, INovaGuiNextPublishingCommands? commands = null)
    {
        lock (_publishingUiGate)
        {
            lock (_recordingRulesLifecycleGate)
            {
                if (_disposed || _publishingClosed) return;
                _publishingVersion++;
                _publishingStatus = status ?? new NovaObsPublishingReadOnlyStatus(null);
                if (_publishingSettings is not null) _publishingSettings.Changed -= PublishingSettingsChanged;
                _publishingSettings = (status as NovaGuiNextPublishingComposition)?.Settings;
                if (_publishingSettings is not null) _publishingSettings.Changed += PublishingSettingsChanged;
                _publishingCommands = commands;
                _publishingBinding = new(_publishingVersion, _publishingStatus, commands, _publishingBinding is null);
                ResetPublishingConnectionLocked();
            }
        }
        NotifyPublishingConnection();
        RefreshPublishingStatus();
    }

    internal void RefreshPublishingStatus() => _ = RefreshPublishingStatusAsync();

    private void PublishingSettingsChanged()
    {
        Action<Action>? dispatch;
        lock (_publishingUiGate) { dispatch = _publishingDispatch; }
        dispatch?.Invoke(() =>
        {
            lock (_recordingRulesLifecycleGate) { if (_disposed || _publishingClosed) return; }
            OnPropertyChanged(nameof(PublishingCommands));
        });
        RefreshPublishingStatus(); // Dirty/pending settings can disable cached Start even if a source read is blocked.
    }

    internal Task RefreshPublishingStatusAsync()
    {
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed || _publishingClosed) return Task.CompletedTask;
            _publishingRequested = true;
            if (_publishingReader is { IsCompleted: false }) return _publishingReader;
            _publishingReader = Task.Run(ReadPublishingLoop);
            ObservePublishingTask(_publishingReader);
            return _publishingReader;
        }
    }

    private void ReadPublishingLoop()
    {
        while (true)
        {
            INovaObsPublishingReadOnlyStatus source;
            INovaGuiNextPublishingCommands? boundary;
            long version, serial, windowLifetime;
            PublishingBinding? binding;
            lock (_recordingRulesLifecycleGate)
            {
                if (_disposed || _publishingClosed || !_publishingRequested) { _publishingReader = null; return; }
                _publishingRequested = false;
                source = _publishingStatus; boundary = _publishingCommands;
                version = _publishingVersion; serial = ++_publishingReadSerial;
                windowLifetime = _publishingWindowLifetime;
                binding = _publishingBinding;
            }
            NovaObsPublishingReadOnlySnapshot snapshot;
            NovaGuiNextPublishingCommandState commands;
            var consistent = true;
            try
            {
                if (ReferenceEquals(source, boundary) && source is NovaGuiNextPublishingComposition composition)
                    (snapshot, commands) = composition.ReadPublishingPresentation();
                else
                {
                    snapshot = source.Read();
                    commands = boundary?.ReadCommands() ?? NovaGuiNextPublishingCommandState.Unavailable;
                }
                if (boundary is not null && snapshot.Generation != commands.Generation)
                {
                    (snapshot, commands) = UnavailablePublishing();
                    consistent = false;
                }
            }
            catch { (snapshot, commands) = UnavailablePublishing(); consistent = false; }
            var captured = snapshot; var capturedCommands = commands;
            NovaObsPublishingConnectionInfo? connection = null;
            try { connection = (source as INovaObsPublishingConnectionInfo)?.ReadConnectionInfo(snapshot); }
            catch { /* Explicit disclosure fails closed without exception text. */ }
            var presentation = NovaObsPublishingConnectionPresentation.From(snapshot, connection);
            Action publish = () => PublishPublishing(version, serial, source, captured, capturedCommands, binding, consistent, presentation, windowLifetime);
            Action<Action>? dispatch;
            lock (_recordingRulesLifecycleGate)
            {
                if (_disposed || _publishingClosed || version != _publishingVersion) continue;
                dispatch = _publishingDispatch;
                if (dispatch is null) _publishingPublication = publish;
            }
            if (dispatch is not null) dispatch(publish);
        }
    }

    private static (NovaObsPublishingReadOnlySnapshot, NovaGuiNextPublishingCommandState) UnavailablePublishing() =>
        (new(NovaObsPublishingAvailability.Unavailable, null, null, "利用不可", "配信状態の整合性を確認できません。",
            PeerCast.Nova.Windows.NovaObsPublishingTone.Warning, null, null, null), NovaGuiNextPublishingCommandState.Unavailable);

    private void PublishPublishing(long version, long serial, INovaObsPublishingReadOnlyStatus source,
        NovaObsPublishingReadOnlySnapshot snapshot, NovaGuiNextPublishingCommandState commands, PublishingBinding? binding, bool consistent,
        NovaObsPublishingConnectionPresentation connection, long windowLifetime)
    {
        // UI serialization only. No external Read, command or callback under the
        // ViewModel admission gate. Dispose serializes with notifications here.
        lock (_publishingUiGate)
        {
            if (source is NovaGuiNextPublishingComposition composition && snapshot.Generation != composition.CurrentGeneration) return;
            // This interface performs bounded owned-state validation, never a
            // borrowed Snapshot read. Check after entering UI serialization.
            try
            {
                if (connection.Identity is { } identity &&
                    (source is not INovaObsPublishingConnectionInfo reader || !reader.IsConnectionInfoCurrent(identity)))
                    connection = NovaObsPublishingConnectionPresentation.Checking;
            }
            catch { connection = NovaObsPublishingConnectionPresentation.Checking; }
            bool rowsChanged, commandsChanged, connectionChanged;
            lock (_recordingRulesLifecycleGate)
            {
                if (_disposed || _publishingClosed || version != _publishingVersion || serial < _publishingPublishedSerial) return;
                if (windowLifetime != _publishingWindowLifetime || _publishingDispatch is null)
                    connection = NovaObsPublishingConnectionPresentation.Checking;
                _publishingPublishedSerial = serial;
                rowsChanged = _publishingRows.Count != 1 || _publishingRows[0] != snapshot;
                commandsChanged = PublishingCommands != commands;
                if (rowsChanged) _publishingRows = Array.AsReadOnly(new[] { snapshot });
                PublishingCommands = commands;
                var previousConnection = (_publishingConnection, PublishingKeyDisplay, PublishingKeyRevealed, _publishingConnectionFeedback);
                UpdatePublishingConnectionLocked(connection);
                connectionChanged = previousConnection != (_publishingConnection, PublishingKeyDisplay, PublishingKeyRevealed, _publishingConnectionFeedback);
                _publishingPublishedBinding = consistent ? binding : null;
            }
            if (rowsChanged) OnPropertyChanged(nameof(PublishingRows));
            lock (_recordingRulesLifecycleGate)
                if (_disposed || _publishingClosed || version != _publishingVersion) return;
            if (connectionChanged) OnPropertyChanged(nameof(PublishingConnection));
            // A subscriber can synchronously dispose/rebind during the first event.
            lock (_recordingRulesLifecycleGate)
                if (_disposed || _publishingClosed || version != _publishingVersion) return;
            if (commandsChanged) OnPropertyChanged(nameof(PublishingCommands));
        }
    }

    private void ClosePublishingView()
    {
        lock (_publishingUiGate)
            lock (_recordingRulesLifecycleGate)
            {
                _publishingClosed = true; _publishingVersion++;
                if (_publishingSettings is not null) _publishingSettings.Changed -= PublishingSettingsChanged;
                _publishingSettings = null;
                _publishingWindowLifetime++;
                ResetPublishingConnectionLocked();
                _publishingDispatch = null; _publishingPublication = null;
            }
        // Noncooperative reads remain owned and fault observed; shutdown never
        // joins them as if cancellation could terminate a synchronous callback.
    }

    internal Task RequestPublishingStartAsync() => RequestPublishingCommandAsync(0);
    internal Task RequestPublishingInterruptAsync() => RequestPublishingCommandAsync(1);
    internal Task RequestPublishingFinishAsync() => RequestPublishingCommandAsync(2);

    private Task RequestPublishingCommandAsync(int command)
    {
        PublishingBinding? binding; long? expected; bool initialUnpublished;
        lock (_recordingRulesLifecycleGate)
        {
            binding = _publishingBinding;
            if (!IsPublishingCommandBindingCurrent(binding)) return Task.CompletedTask;
            initialUnpublished = binding!.Initial && _publishingPublishedSerial == 0;
            expected = initialUnpublished ? null : PublishingCommands.Generation;
        }
        // Capture a target even when the first asynchronous display is pending.
        // This bounded internal query never reads the runtime and holds no VM gate.
        if (initialUnpublished) expected = (binding.Boundary as NovaGuiNextPublishingComposition)?.CurrentGeneration;
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed || _publishingClosed || SelectedTab != GenesisGuiNextTab.Publishing || _publishingCommands is null) return Task.CompletedTask;
            if (!IsPublishingCommandBindingCurrent(binding)) return Task.CompletedTask;
            if (binding.Boundary is NovaGuiNextPublishingComposition && expected is null) return Task.CompletedTask;
            var boundary = _publishingCommands; var version = _publishingVersion;
            var workerStarting = PublishingCommandWorkerStarting;
            var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PublishingCommandAdmission = admitted.Task;
            var task = Task.Run(async () =>
            {
                try
                {
                    if (workerStarting is not null) await workerStarting().ConfigureAwait(false);
                    lock (_recordingRulesLifecycleGate)
                        if (_disposed || _publishingClosed || version != _publishingVersion || !IsPublishingCommandBindingCurrent(binding)) return;
                    Task operation;
                    if (boundary is NovaGuiNextPublishingComposition composition)
                    {
                        var generation = expected;
                        operation = command switch
                        {
                            0 => composition.RequestStartForGenerationAsync(generation),
                            1 => composition.RequestInterruptForGenerationAsync(generation),
                            _ => composition.RequestFinishForGenerationAsync(generation),
                        };
                    }
                    else operation = command switch { 0 => boundary.RequestStartAsync(), 1 => boundary.RequestInterruptAsync(), _ => boundary.RequestFinishAsync() };
                    admitted.TrySetResult();
                    RefreshPublishingStatus();
                    await operation.ConfigureAwait(false);
                }
                finally { admitted.TrySetResult(); RefreshPublishingStatus(); }
            });
            _publishingOperations.Add(task);
            ObservePublishingTask(task);
            return task;
        }
    }

    // The displayed generation ordinal is meaningful only within its binding.
    // Only the very first binding may admit explicit work before any publication.
    private bool IsPublishingCommandBindingCurrent(PublishingBinding? binding) =>
        binding is not null && ReferenceEquals(binding, _publishingBinding) && binding.Version == _publishingVersion &&
        ReferenceEquals(binding.Source, _publishingStatus) && ReferenceEquals(binding.Boundary, _publishingCommands) &&
        (ReferenceEquals(binding, _publishingPublishedBinding) || (binding.Initial && _publishingPublishedSerial == 0));

    private void ObservePublishingTask(Task task) => _ = task.ContinueWith(t =>
    {
        if (t.IsFaulted) _ = t.Exception;
        lock (_recordingRulesLifecycleGate) _publishingOperations.Remove(t);
    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
}
