using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

/// <summary>
/// Minimal Genesis main-window state. This type is UI-framework neutral so the
/// first visible shell can remain independent from production playback.
/// </summary>
public sealed partial class GenesisMainViewModel : INotifyPropertyChanged
{
    internal const int MaximumSourceCharacters = 2_048;
    internal GenesisRecordingOutputDirectorySettings? RecordingOutputSettings { get; set; }
    internal GenesisGuiNextPlaybackSettings? PlaybackSettings { get; set; }
    internal GenesisChannelNotificationSettings? NotificationSettings { get; set; }
    internal GenesisAppearanceSettings? AppearanceSettings { get; set; }
    internal bool UsesModernAppearance => AppearanceSettings?.CurrentRuntimeMode ==
        Foundation.Configuration.NovaGuiMode.Modern;
    private readonly IPlaybackSessionController _controller;
    private readonly IGenesisManualRecordingSessionController? _recordingController;
    private readonly GenesisManualRecordingCommandLanes _recordingCommandLanes = new();
    private readonly ConcurrentDictionary<ChannelId, RecordingCommandStatus>
        _recordingCommands = new();
    private readonly ConcurrentDictionary<ChannelId, RecordingCommandError> _recordingErrors = new();
    private readonly ConcurrentDictionary<ChannelId, string> _recordingNotices = new();
    private readonly bool _hasPlaybackWindowSurface;
    private PlaybackSessionSnapshot _snapshot;
    private IReadOnlyList<GenesisChannelItem> _channels =
        Array.Empty<GenesisChannelItem>();
    private GenesisPlaybackWindowState _playbackWindow;
    private GenesisChannelSort? _channelSort;
    private string _sourceText = string.Empty;
    private string? _errorMessage;
    private bool _errorClearsOnSourceChange;
    private string? _channelCatalogErrorMessage;
    private bool _playCommandInFlight;
    private bool _stopCommandInFlight;
    private bool _channelCatalogRefreshing;
    private GenesisChannelItem? _selectedChannel;
    private bool _playbackWindowClosePending;
    private CancellationToken _playbackWindowCloseCancellationToken;
    private long _recordingPresentationRevision;
    private long _activeRecordingRefreshSequence;
    private IReadOnlyList<GenesisActiveRecordingSession> _activeRecordingSessions =
        Array.AsReadOnly(Array.Empty<GenesisActiveRecordingSession>());
    private int _activeRecordingSnapshotAvailable;

    public GenesisMainViewModel(IPlaybackSessionController controller)
        : this(controller, hasPlaybackWindowSurface: true)
    {
    }

    internal GenesisMainViewModel(
        IPlaybackSessionController controller,
        bool hasPlaybackWindowSurface,
        IGuiNextNetworkSettingsAdapter? networkSettingsAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        NetworkSettingsAdapter = networkSettingsAdapter;
        _recordingController = controller as IGenesisManualRecordingSessionController;
        _automaticRecordingCommands = controller is GuiNextDailyPlaybackSessionController guiNext
            ? guiNext.AutomaticRecordingUserCommands : controller as IGenesisAutomaticRecordingUserCommands;
        if (_automaticRecordingCommands is not null)
        {
            _automaticRecordingCommands.SuppressionChanged += OnSuppressionChanged;
        }
        _hasPlaybackWindowSurface = hasPlaybackWindowSurface;
        _snapshot = controller.Snapshot;
        _errorMessage = _snapshot.ErrorMessage;
        _playbackWindow = PlaybackWindowForSnapshot(_snapshot);
        if (_controller is IPlaybackSessionSnapshotNotifications notifications)
        {
            notifications.SnapshotChanged += OnPlaybackSnapshotChanged;
        }
    }

    // Borrowed application capability. Closing this view never disposes it.
    internal IGuiNextNetworkSettingsAdapter? NetworkSettingsAdapter { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<GenesisChannelItem> Channels => _channels;

    public int ChannelCount => _channels.Count;

    public GenesisChannelSort? ChannelSort => _channelSort;

    public GenesisChannelItem? SelectedChannel
    {
        get => _selectedChannel;
        set => SetSelectedChannel(value, updateSourceText: true);
    }

    public string SourceText
    {
        get => _sourceText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_sourceText, next, StringComparison.Ordinal))
            {
                return;
            }

            _sourceText = next;
            if (_errorClearsOnSourceChange)
            {
                SetError(null);
            }

            OnPropertyChanged();
        }
    }

    public PlaybackSessionState PlaybackState => _snapshot.State;

    public string? CurrentSource => _snapshot.Source;

    internal PlaybackSessionSnapshot PlaybackSnapshot => _snapshot;

    // Non-owning typed boundary for the explicit local Relay command.
    // The application state owns shutdown. No Play/record command activates it.
    internal GenesisGuiNextPublicChannelComposition? PublicChannels { get; set; }

    internal IGenesisRecordingHistoryController? RecordingHistoryController =>
        _controller is GuiNextDailyPlaybackSessionController guiNext
            ? guiNext.RecordingHistoryController
            : _controller as IGenesisRecordingHistoryController;

    internal IGenesisRecordingLibraryController? RecordingLibraryController =>
        _controller is GuiNextDailyPlaybackSessionController guiNext
            ? guiNext.RecordingLibraryController
            : _controller as IGenesisRecordingLibraryController;

    internal IGenesisAutomaticRecordingCatalogObserver?
        AutomaticRecordingCatalogObserver =>
            _controller is GuiNextDailyPlaybackSessionController guiNextController
                ? guiNextController.AutomaticRecordingCatalogObserver
                : _controller as IGenesisAutomaticRecordingCatalogObserver;

    public string? ErrorMessage => _errorMessage;

    public bool IsChannelCatalogRefreshing => _channelCatalogRefreshing;

    public string? ChannelCatalogErrorMessage => _channelCatalogErrorMessage;

    public GenesisPlaybackWindowState PlaybackWindow => _playbackWindow;

    public bool IsPlaybackWindowOpen => _playbackWindow.IsOpen;

    public GenesisPlaybackWindowMode PlaybackWindowMode => _playbackWindow.Mode;

    public string? PlaybackWindowSource => _playbackWindow.Source;

    public string StatusText => PlaybackState switch
    {
        PlaybackSessionState.Starting => "Starting",
        PlaybackSessionState.Playing => "Playing",
        PlaybackSessionState.Stopping => "Stopping",
        PlaybackSessionState.Faulted => "Playback error",
        _ when _channelCatalogRefreshing => "Loading channels...",
        _ when !string.IsNullOrWhiteSpace(_channelCatalogErrorMessage) =>
            "Channel refresh failed.",
        _ => "Ready",
    };

    public bool CanPlay => !_playCommandInFlight &&
        !_stopCommandInFlight &&
        PlaybackState is PlaybackSessionState.Stopped or PlaybackSessionState.Faulted;

    public bool CanStop => !_stopCommandInFlight &&
        PlaybackState is PlaybackSessionState.Starting or PlaybackSessionState.Playing;

    internal long RecordingPresentationRevision =>
        Interlocked.Read(ref _recordingPresentationRevision);

    internal bool ActiveRecordingSnapshotAvailable =>
        Volatile.Read(ref _activeRecordingSnapshotAvailable) != 0;

    internal IReadOnlyList<GenesisGuiNextActiveRecording> ActiveRecordings
    {
        get
        {
            var suppressionByChannel = SuppressionRows.ToDictionary(row => row.ChannelId);
            return Array.AsReadOnly(_activeRecordingSessions
            .Where(session => session.Origin != RecordingSessionOrigin.AutomaticRule ||
                !(_automaticRecordingCommands?.IsStoppedAutomaticGeneration(session.ChannelId, session.SessionId) ?? false))
            .Select(session =>
            {
                var isAvailable = ActiveRecordingSnapshotAvailable;
                var command = _recordingCommands.TryGetValue(
                    session.ChannelId,
                    out var currentCommand) &&
                    currentCommand.Kind == RecordingCommandState.Stopping &&
                    currentCommand.SessionId == session.SessionId
                        ? currentCommand
                        : (RecordingCommandStatus?)null;
                var errorMessage = GetRecordingError(
                    session.ChannelId,
                    session.SessionId);
                var displayName = ResolveRecordingDisplayName(session);
                return new GenesisGuiNextActiveRecording(
                    session.ChannelId,
                    session.SessionId,
                    session.Origin,
                    displayName,
                    !isAvailable
                        ? "状態を確認できません"
                        : suppressionByChannel.TryGetValue(session.ChannelId, out var suppressed) && suppressed.State == GenesisAutomaticRecordingSuppressionState.AdmittedPending
                            ? suppressed.StatusText
                        : command?.Kind == RecordingCommandState.Stopping
                            ? "録画停止中"
                            : "録画中",
                    session.AutomaticRuleId,
                    isAvailable ? errorMessage : "録画状態を確認できませんでした。");
            })
            .ToArray());
        }
    }

    internal IReadOnlyList<GenesisGuiNextActiveRecording> ActiveManualRecordings =>
        Array.AsReadOnly(ActiveRecordings
            .Where(static session => session.Origin == RecordingSessionOrigin.Manual)
            .ToArray());

    internal GenesisManualRecordingPresentation GetManualRecordingPresentation(
        GenesisChannelItem? channel)
    {
        if (_recordingController is null || channel is null ||
            !GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var channelId) ||
            channelId is null)
        {
            return GenesisManualRecordingPresentation.Unavailable;
        }

        GenesisRecordingCoreSnapshot snapshot;
        try
        {
            snapshot = _recordingController.GetSnapshot(channel);
        }
        catch
        {
            return GenesisManualRecordingPresentation.Unavailable;
        }

        var errorMessage = GetRecordingError(channelId, snapshot.SessionId);
        if (_recordingCommands.TryGetValue(channelId, out var command) &&
            (command.Kind == RecordingCommandState.Starting ||
                command.Kind == RecordingCommandState.Stopping &&
                command.SessionId == snapshot.SessionId))
        {
            return command.Kind == RecordingCommandState.Starting
                ? new(false, false, false, true, false, "録画開始中", null)
                : new(false, false, true, true, false, "録画停止中", errorMessage);
        }

        switch (snapshot.State)
        {
            case GenesisRecordingCoreStateKind.Manual:
                var canStop = snapshot.SessionId is not null &&
                    _recordingController.IsKnownManualSession(
                        channelId,
                        snapshot.SessionId);
                return new(
                    false,
                    canStop,
                    true,
                    false,
                    errorMessage is not null,
                    "録画中",
                    errorMessage);
            case GenesisRecordingCoreStateKind.Automatic:
                return new(
                    false,
                    false,
                    true,
                    false,
                    errorMessage is not null,
                    "自動録画中",
                    errorMessage);
            case GenesisRecordingCoreStateKind.Idle when snapshot.IsCandidateValid:
                var notice = _recordingNotices.GetValueOrDefault(channelId);
                return new(
                    true,
                    false,
                    false,
                    false,
                    errorMessage is not null,
                    errorMessage is null ? notice ?? string.Empty : "録画失敗",
                    errorMessage);
            case GenesisRecordingCoreStateKind.Unavailable when errorMessage is not null:
                return new(false, false, false, false, true, "録画失敗", errorMessage);
            default:
                return GenesisManualRecordingPresentation.Unavailable;
        }
    }

    internal GenesisManualRecordingPresentation GetManualRecordingPresentation(
        GenesisGuiNextActiveRecording recording) => GetActiveRecordingPresentation(recording);

    internal GenesisManualRecordingPresentation GetActiveRecordingPresentation(
        GenesisGuiNextActiveRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (!ActiveRecordingSnapshotAvailable)
        {
            return new(
                CanRecord: false,
                CanStop: false,
                IsRecording: false,
                IsTransitioning: false,
                IsFailure: true,
                StatusText: "録画状態を確認できません",
                ErrorMessage: "録画状態を確認できませんでした。");
        }

        if (_recordingController is null)
        {
            return GenesisManualRecordingPresentation.Unavailable;
        }

        GenesisRecordingCoreSnapshot snapshot;
        try
        {
            snapshot = _recordingController.GetSnapshot(recording.ChannelId);
        }
        catch
        {
            return GenesisManualRecordingPresentation.Unavailable;
        }

        var expectedState = recording.Origin switch
        {
            RecordingSessionOrigin.Manual => GenesisRecordingCoreStateKind.Manual,
            RecordingSessionOrigin.AutomaticRule => GenesisRecordingCoreStateKind.Automatic,
            _ => GenesisRecordingCoreStateKind.Unavailable,
        };
        if (snapshot.State != expectedState || snapshot.SessionId != recording.SessionId)
        {
            return GenesisManualRecordingPresentation.Unavailable;
        }

        var commandMatches = _recordingCommands.TryGetValue(
                recording.ChannelId,
                out var command) &&
            command.Kind == RecordingCommandState.Stopping &&
            command.SessionId == recording.SessionId;
        var errorMessage = GetRecordingError(recording.ChannelId, recording.SessionId);
        if (recording.Origin == RecordingSessionOrigin.AutomaticRule)
        {
            var pendingEntry = SuppressionRows.FirstOrDefault(e => e.ChannelId == recording.ChannelId &&
                e.State == GenesisAutomaticRecordingSuppressionState.AdmittedPending);
            var pending = pendingEntry is not null;
            return new(
                CanRecord: false,
                CanStop: !commandMatches && (_automaticRecordingCommands?.CanStopAutomatic(recording.ChannelId, recording.SessionId) ?? false),
                IsRecording: true,
                IsTransitioning: pending || commandMatches,
                IsFailure: errorMessage is not null,
                StatusText: pending ? pendingEntry!.StatusText : commandMatches ? "停止処理中" : "自動録画中",
                ErrorMessage: errorMessage ?? _recordingNotices.GetValueOrDefault(recording.ChannelId));
        }

        return new(
            CanRecord: false,
            CanStop: !commandMatches && _recordingController.IsKnownManualSession(
                recording.ChannelId,
                recording.SessionId),
            IsRecording: true,
            IsTransitioning: commandMatches,
            IsFailure: errorMessage is not null,
            StatusText: commandMatches
                ? "録画停止中"
                : "録画中",
            ErrorMessage: errorMessage);
    }

    internal async ValueTask RefreshActiveRecordingsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_recordingController is null)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _activeRecordingRefreshSequence);
        GenesisActiveRecordingSessionSnapshot snapshot;
        try
        {
            snapshot = await _recordingController
                .GetActiveRecordingSessionsAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            MarkActiveRecordingsUnavailable(sequence);
            return;
        }

        if (sequence != Interlocked.Read(ref _activeRecordingRefreshSequence))
        {
            return;
        }

        if (!snapshot.IsAvailable)
        {
            MarkActiveRecordingsUnavailable(sequence);
            return;
        }

        _activeRecordingSessions = Array.AsReadOnly(snapshot.Sessions
            .OrderBy(static session => session.ChannelId.Value)
            .ThenBy(static session => session.SessionId.Value)
            .ThenBy(static session => session.Origin)
            .ToArray());
        Volatile.Write(ref _activeRecordingSnapshotAvailable, 1);
        NotifyRecordingPresentationChanged();
    }

    internal ValueTask RefreshActiveManualRecordingsAsync(
        CancellationToken cancellationToken = default) =>
        RefreshActiveRecordingsAsync(cancellationToken);

    private void MarkActiveRecordingsUnavailable(long sequence)
    {
        if (sequence != Interlocked.Read(ref _activeRecordingRefreshSequence))
        {
            return;
        }

        Volatile.Write(ref _activeRecordingSnapshotAvailable, 0);
        NotifyRecordingPresentationChanged();
    }

    internal async ValueTask StartManualRecordingAsync(
        GenesisChannelItem? channel,
        CancellationToken cancellationToken = default)
    {
        if (_recordingController is null ||
            channel is null ||
            !GetManualRecordingPresentation(channel).CanRecord ||
            !GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var channelId) ||
            channelId is null ||
            !_recordingCommandLanes.TryEnter(channelId))
        {
            return;
        }

        _recordingErrors.TryRemove(channelId, out _);
        _recordingNotices.TryRemove(channelId, out _);
        _recordingCommands[channelId] = new(RecordingCommandState.Starting, null);
        NotifyRecordingPresentationChanged();
        try
        {
            var outcome = await _recordingController
                .StartAsync(channel, cancellationToken)
                .ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case GenesisManualRecordingCommandKind.Started:
                case GenesisManualRecordingCommandKind.AlreadyRunning:
                    _recordingErrors.TryRemove(channelId, out _);
                    break;
                case GenesisManualRecordingCommandKind.Rejected
                    when outcome.Snapshot.State is GenesisRecordingCoreStateKind.Manual or
                        GenesisRecordingCoreStateKind.Automatic:
                    _recordingErrors.TryRemove(channelId, out _);
                    break;
                case GenesisManualRecordingCommandKind.Cancelled:
                    break;
                default:
                    _recordingErrors[channelId] = new(
                        "録画を開始できませんでした。",
                        SessionId: null);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _recordingErrors.TryRemove(channelId, out _);
        }
        catch
        {
            _recordingErrors[channelId] = new(
                "録画を開始できませんでした。",
                SessionId: null);
        }
        finally
        {
            _recordingCommands.TryRemove(channelId, out _);
            _recordingCommandLanes.Exit(channelId);
            await RefreshActiveRecordingsAsync(cancellationToken)
                .ConfigureAwait(false);
            NotifyRecordingPresentationChanged();
        }
    }

    internal async ValueTask StopManualRecordingAsync(
        GenesisChannelItem? channel,
        CancellationToken cancellationToken = default)
    {
        if (_recordingController is null ||
            channel is null ||
            !GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var channelId) ||
            channelId is null)
        {
            return;
        }

        var presentation = GetManualRecordingPresentation(channel);
        var snapshot = _recordingController.GetSnapshot(channel);
        if (!presentation.CanStop || snapshot.SessionId is null)
        {
            return;
        }

        await StopManualRecordingAsync(
            channelId,
            snapshot.SessionId,
            cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask StopManualRecordingAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        if (_recordingController is null ||
            !IsCurrentKnownManualSession(channelId, expectedSessionId) ||
            !_recordingCommandLanes.TryEnter(channelId))
        {
            return;
        }

        _recordingErrors.TryRemove(channelId, out _);
        _recordingNotices.TryRemove(channelId, out _);
        _recordingCommands[channelId] = new(
            RecordingCommandState.Stopping,
            expectedSessionId);
        NotifyRecordingPresentationChanged();
        try
        {
            var outcome = await _recordingController
                .StopAsync(channelId, expectedSessionId, cancellationToken)
                .ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case GenesisManualRecordingCommandKind.Stopped:
                case GenesisManualRecordingCommandKind.NoActiveSession:
                case GenesisManualRecordingCommandKind.AlreadyStopped:
                case GenesisManualRecordingCommandKind.Cancelled:
                    break;
                case GenesisManualRecordingCommandKind.NoDecodableVideoStart:
                    _recordingNotices[channelId] = "録画ファイルなし（映像開始点未到達）";
                    break;
                case GenesisManualRecordingCommandKind.Rejected
                    when outcome.Snapshot.State == GenesisRecordingCoreStateKind.Automatic:
                    break;
                default:
                    _recordingErrors[channelId] = new(
                        "録画を停止できませんでした。",
                        expectedSessionId);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _recordingErrors.TryRemove(channelId, out _);
        }
        catch
        {
            _recordingErrors[channelId] = new(
                "録画を停止できませんでした。",
                expectedSessionId);
        }
        finally
        {
            _recordingCommands.TryRemove(channelId, out _);
            _recordingCommandLanes.Exit(channelId);
            await RefreshActiveRecordingsAsync(cancellationToken)
                .ConfigureAwait(false);
            NotifyRecordingPresentationChanged();
        }
    }

    public void ReplaceChannels(IEnumerable<GenesisChannelItem> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        SetChannels(channels, _selectedChannel);
    }

    public void SortChannels(GenesisChannelColumn column)
    {
        var direction = _channelSort?.Column == column &&
            _channelSort.Direction == GenesisChannelSortDirection.Ascending
                ? GenesisChannelSortDirection.Descending
                : GenesisChannelSortDirection.Ascending;
        _channelSort = new(column, direction);
        OnPropertyChanged(nameof(ChannelSort));
        SetChannels(_channels, _selectedChannel);
    }

    internal void SetChannelCatalogRefreshState(bool isRefreshing, string? errorMessage)
    {
        var statusChanged = _channelCatalogRefreshing != isRefreshing ||
            !string.Equals(
                _channelCatalogErrorMessage,
                errorMessage,
                StringComparison.Ordinal);
        if (!statusChanged)
        {
            return;
        }

        _channelCatalogRefreshing = isRefreshing;
        _channelCatalogErrorMessage = errorMessage;
        OnPropertyChanged(nameof(IsChannelCatalogRefreshing));
        OnPropertyChanged(nameof(ChannelCatalogErrorMessage));
        OnPropertyChanged(nameof(StatusText));
    }

    public async ValueTask PlayAsync(CancellationToken cancellationToken = default)
    {
        if (!CanPlay)
        {
            return;
        }

        var source = SourceText.Trim();
        var validationError = GetSourceValidationError(source);
        if (validationError is not null)
        {
            SetError(validationError, clearsOnSourceChange: true);
            return;
        }

        var selectedChannel = SelectedChannel;
        var request = selectedChannel is not null &&
            string.Equals(selectedChannel.Source, source, StringComparison.Ordinal)
                ? new PlaybackSessionRequest(
                    source,
                    selectedChannel.Name,
                    selectedChannel.ChannelId)
                : new PlaybackSessionRequest(source);

        SetPlayCommandInFlight(true);
        SetSnapshot(new PlaybackSessionSnapshot(
            PlaybackSessionState.Starting,
            source,
            null)
        {
            ChannelName = request.ChannelName ?? source,
            ChannelId = request.ChannelId,
        });
        try
        {
            var outcome = await _controller.PlayAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var currentSnapshot = _controller.Snapshot;
            if (currentSnapshot != outcome.Snapshot)
            {
                SetSnapshot(currentSnapshot);
                SetError(currentSnapshot.ErrorMessage, clearsOnSourceChange: true);
            }
            else
            {
                ApplyOutcome(outcome, errorClearsOnSourceChange: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetSnapshot(_controller.Snapshot);
            SetError(null);
        }
        catch
        {
            SetSnapshot(new(PlaybackSessionState.Faulted, source, "Playback failed."));
            SetError("Playback failed.", clearsOnSourceChange: true);
        }
        finally
        {
            SetPlayCommandInFlight(false);
            if (_playbackWindowClosePending)
            {
                var closeCancellationToken = _playbackWindowCloseCancellationToken;
                _playbackWindowClosePending = false;
                _playbackWindowCloseCancellationToken = default;
                if (IsPlaybackWindowOpen && !closeCancellationToken.IsCancellationRequested)
                {
                    await StopAsync(closeCancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    internal static string? GetSourceValidationError(string? source)
    {
        var normalized = source?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return "Enter a channel or source URL.";
        }

        return normalized.Length > MaximumSourceCharacters
            ? "The channel or source URL is too long."
            : null;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStop)
        {
            return;
        }

        var playbackWindowBeforeStop = _playbackWindow;
        SetStopCommandInFlight(true);
        SetSnapshot(_snapshot with { State = PlaybackSessionState.Stopping, ErrorMessage = null });
        try
        {
            var outcome = await _controller.StopAsync(cancellationToken).ConfigureAwait(false);
            ApplyOutcome(
                outcome,
                errorClearsOnSourceChange: false,
                rejectedErrorMessage: "Stop request was rejected.",
                faultedErrorMessage: "Stop failed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetSnapshot(_controller.Snapshot);
            SetError(null);
        }
        catch
        {
            SetSnapshot(_snapshot with
            {
                State = PlaybackSessionState.Faulted,
                ErrorMessage = "Stop failed.",
            });
            SetError("Stop failed.");
        }
        finally
        {
            if (playbackWindowBeforeStop.IsOpen &&
                _snapshot.State == PlaybackSessionState.Playing &&
                string.Equals(
                    _snapshot.Source,
                    playbackWindowBeforeStop.Source,
                    StringComparison.Ordinal))
            {
                SetPlaybackWindowMode(playbackWindowBeforeStop.Mode);
            }

            SetStopCommandInFlight(false);
        }
    }

    public ValueTask ClosePlaybackWindowAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsPlaybackWindowOpen)
        {
            return ValueTask.CompletedTask;
        }

        if (_playCommandInFlight)
        {
            _playbackWindowClosePending = true;
            _playbackWindowCloseCancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }

        return StopAsync(cancellationToken);
    }

    public void ShowVideoOnly()
    {
        SetPlaybackWindowMode(GenesisPlaybackWindowMode.VideoOnly);
    }

    public void ShowFullscreen()
    {
        SetPlaybackWindowMode(GenesisPlaybackWindowMode.Fullscreen);
    }

    public void RestorePlaybackWindow()
    {
        SetPlaybackWindowMode(GenesisPlaybackWindowMode.Normal);
    }

    private void SetChannels(
        IEnumerable<GenesisChannelItem> channels,
        GenesisChannelItem? selectedChannelBeforeReplace)
    {
        _channels = GenesisChannelListOrdering.Apply(channels, _channelSort);
        OnPropertyChanged(nameof(Channels));
        OnPropertyChanged(nameof(ChannelCount));
        NotifyRecordingPresentationChanged();

        if (selectedChannelBeforeReplace is not null)
        {
            var selectedSource = selectedChannelBeforeReplace.Source;
            var selectedChannel = _channels.FirstOrDefault(channel =>
                GenesisChannelIdentity.Matches(channel, selectedChannelBeforeReplace));
            var updateSourceText = string.Equals(
                _sourceText,
                selectedSource,
                StringComparison.Ordinal);
            SetSelectedChannel(selectedChannel, updateSourceText);
        }
    }

    private void SetSelectedChannel(
        GenesisChannelItem? value,
        bool updateSourceText)
    {
        if (ReferenceEquals(_selectedChannel, value))
        {
            return;
        }

        _selectedChannel = value;
        OnPropertyChanged(nameof(SelectedChannel));
        NotifyRecordingPresentationChanged();
        if (updateSourceText && value is not null)
        {
            SourceText = value.Source;
        }
    }

    private void ApplyOutcome(
        PlaybackSessionCommandOutcome outcome,
        bool errorClearsOnSourceChange,
        string rejectedErrorMessage = "Playback request was rejected.",
        string faultedErrorMessage = "Playback failed.")
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(outcome.Snapshot);
        SetSnapshot(outcome.Snapshot);
        SetError(
            outcome.Result switch
            {
                PlaybackSessionCommandResult.Completed => outcome.Snapshot.ErrorMessage,
                PlaybackSessionCommandResult.Cancelled => null,
                PlaybackSessionCommandResult.Rejected =>
                    outcome.Snapshot.ErrorMessage ?? rejectedErrorMessage,
                _ => outcome.Snapshot.ErrorMessage ?? faultedErrorMessage,
            },
            errorClearsOnSourceChange);
    }

    private void SetSnapshot(PlaybackSessionSnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(PlaybackSnapshot));
        OnPropertyChanged(nameof(PlaybackState));
        OnPropertyChanged(nameof(CurrentSource));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanStop));
        SetPlaybackWindow(PlaybackWindowForSnapshot(snapshot));
    }

    private void SetPlaybackWindowMode(GenesisPlaybackWindowMode mode)
    {
        if (!_playbackWindow.IsOpen || _playbackWindow.Mode == mode)
        {
            return;
        }

        SetPlaybackWindow(_playbackWindow with { Mode = mode });
    }

    private void SetPlaybackWindow(GenesisPlaybackWindowState value)
    {
        if (Equals(_playbackWindow, value))
        {
            return;
        }

        _playbackWindow = value;
        OnPropertyChanged(nameof(PlaybackWindow));
        OnPropertyChanged(nameof(IsPlaybackWindowOpen));
        OnPropertyChanged(nameof(PlaybackWindowMode));
        OnPropertyChanged(nameof(PlaybackWindowSource));
    }

    private GenesisPlaybackWindowState PlaybackWindowForSnapshot(
        PlaybackSessionSnapshot snapshot) =>
        _hasPlaybackWindowSurface &&
        snapshot.State == PlaybackSessionState.Playing &&
        !string.IsNullOrWhiteSpace(snapshot.Source)
            ? GenesisPlaybackWindowState.Open(snapshot.Source)
            : GenesisPlaybackWindowState.Closed;

    private void SetError(string? value, bool clearsOnSourceChange = false)
    {
        var nextClearsOnSourceChange = value is not null && clearsOnSourceChange;
        if (string.Equals(_errorMessage, value, StringComparison.Ordinal) &&
            _errorClearsOnSourceChange == nextClearsOnSourceChange)
        {
            return;
        }

        _errorMessage = value;
        _errorClearsOnSourceChange = nextClearsOnSourceChange;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void SetPlayCommandInFlight(bool value)
    {
        if (_playCommandInFlight == value)
        {
            return;
        }

        _playCommandInFlight = value;
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanStop));
    }

    private void SetStopCommandInFlight(bool value)
    {
        if (_stopCommandInFlight == value)
        {
            return;
        }

        _stopCommandInFlight = value;
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanStop));
    }

    private void OnPlaybackSnapshotChanged(object? sender, EventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        var snapshot = _controller.Snapshot;
        SetSnapshot(snapshot);
        SetError(
            snapshot.ErrorMessage,
            clearsOnSourceChange: snapshot.State == PlaybackSessionState.Faulted);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));

    private void NotifyRecordingPresentationChanged()
    {
        _ = Interlocked.Increment(ref _recordingPresentationRevision);
        OnPropertyChanged(nameof(RecordingPresentationRevision));
        OnPropertyChanged(nameof(ActiveRecordings));
        OnPropertyChanged(nameof(ActiveManualRecordings));
        OnPropertyChanged(nameof(ActiveRecordingSnapshotAvailable));
    }

    private string? GetRecordingError(
        ChannelId channelId,
        RecordingSessionId? sessionId)
    {
        if (!_recordingErrors.TryGetValue(channelId, out var error) ||
            error.SessionId is not null && error.SessionId != sessionId)
        {
            return null;
        }

        return error.Message;
    }

    private string ResolveRecordingDisplayName(GenesisActiveRecordingSession session)
    {
        if (session.Origin == RecordingSessionOrigin.Manual &&
            !string.IsNullOrWhiteSpace(session.RetainedChannelName))
        {
            return session.RetainedChannelName;
        }

        foreach (var channel in _channels)
        {
            if (GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var currentId) &&
                currentId == session.ChannelId &&
                !string.IsNullOrWhiteSpace(channel.Name))
            {
                return channel.Name;
            }
        }

        return GenesisRecordingDisplayName.Fallback(session.ChannelId);
    }

    private bool IsCurrentKnownManualSession(
        ChannelId channelId,
        RecordingSessionId expectedSessionId)
    {
        if (_recordingController is null)
        {
            return false;
        }

        try
        {
            var snapshot = _recordingController.GetSnapshot(channelId);
            return snapshot.State == GenesisRecordingCoreStateKind.Manual &&
                snapshot.SessionId == expectedSessionId &&
                _recordingController.IsKnownManualSession(channelId, expectedSessionId);
        }
        catch
        {
            return false;
        }
    }

    private enum RecordingCommandState
    {
        Starting,
        Stopping,
    }

    private sealed record RecordingCommandStatus(
        RecordingCommandState Kind,
        RecordingSessionId? SessionId);

    private sealed record RecordingCommandError(
        string Message,
        RecordingSessionId? SessionId);
}
