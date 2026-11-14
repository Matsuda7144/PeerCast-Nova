using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

/// <summary>
/// Adapts the GUI playback contract to the existing daily PCP/HTTP-FLV
/// controller. The daily controller remains the owner of the PCP session,
/// loopback HTTP-FLV host, and external player process.
/// </summary>
internal sealed class GuiNextDailyPlaybackSessionController :
    IPlaybackSessionController,
    IGenesisRecordingLibraryController,
    IGenesisRecordingHistoryController,
    IGenesisManualRecordingSessionController,
    IPlaybackSessionSnapshotNotifications,
    IAsyncDisposable
{
    private const string InvalidSourceMessage =
        "This source is not a valid PCP channel URI.";
    private const string PlaybackFailedMessage =
        "The playback source could not be started.";
    private const string PlayerUnavailableMessage =
        "PCRPlayer64.exe is unavailable. Check Playback Settings or clear the path to use NOVA_PCRPLAYER_EXE or PATH.";
    private const string StopFailedMessage =
        "The playback session could not be stopped.";
    private const string EndedUnexpectedlyMessage =
        "The playback session ended unexpectedly.";

    private readonly object _gate = new();
    private readonly INovaDailyPlaybackController _dailyController;
    private readonly IGenesisManualRecordingSessionController? _recordingController;
    private PlaybackSessionSnapshot _snapshot = PlaybackSessionSnapshot.Stopped;
    private NovaDailyPlaybackTermination? _pendingTermination;
    private bool _playStarting;
    private bool _stopRequested;
    private bool _disposed;

    public GuiNextDailyPlaybackSessionController(
        INovaDailyPlaybackController dailyController)
    {
        ArgumentNullException.ThrowIfNull(dailyController);
        _dailyController = dailyController;
        _recordingController = dailyController as IGenesisManualRecordingSessionController;
        _dailyController.PlaybackEnded += OnPlaybackEnded;
    }

    public event EventHandler? SnapshotChanged;

    public PlaybackSessionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    internal IGenesisAutomaticRecordingCatalogObserver?
        AutomaticRecordingCatalogObserver =>
            _dailyController as IGenesisAutomaticRecordingCatalogObserver;

    internal IGenesisAutomaticRecordingUserCommands? AutomaticRecordingUserCommands =>
        (_dailyController as NovaDailyHttpFlvPlaybackController)?.AutomaticRecordingUserCommands;

    internal IGenesisRecordingHistoryController? RecordingHistoryController =>
        _dailyController is IGenesisRecordingHistoryController ? this : null;

    GenesisRecordingHistorySnapshot IGenesisRecordingHistoryController.GetRecordingHistorySnapshot() =>
        ((IGenesisRecordingHistoryController)_dailyController).GetRecordingHistorySnapshot();
    GenesisRecordingHistoryDurabilitySnapshot? IGenesisRecordingHistoryController.GetRecordingHistoryDurabilitySnapshot() =>
        ((IGenesisRecordingHistoryController)_dailyController).GetRecordingHistoryDurabilitySnapshot();
    GenesisRecordingLibrarySnapshot IGenesisRecordingHistoryController.GetRecordingLibrarySnapshot() =>
        ((IGenesisRecordingHistoryController)_dailyController).GetRecordingLibrarySnapshot();

    internal IGenesisRecordingLibraryController? RecordingLibraryController =>
        _dailyController is IGenesisRecordingLibraryController ? this : null;

    GenesisRecordingLibrarySnapshot IGenesisRecordingLibraryController.GetSnapshot() =>
        (_dailyController as IGenesisRecordingLibraryController ??
            throw new InvalidOperationException("Recording Library is unavailable.")).GetSnapshot();

    Task<GenesisRecordingLibraryRefreshResult> IGenesisRecordingLibraryController.RefreshAsync(CancellationToken cancellationToken) =>
        (_dailyController as IGenesisRecordingLibraryController ??
            throw new InvalidOperationException("Recording Library is unavailable.")).RefreshAsync(cancellationToken);

    public GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel) =>
        _recordingController?.GetSnapshot(channel) ??
        GenesisRecordingCoreSnapshot.Unavailable;

    public GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channelId) =>
        _recordingController?.GetSnapshot(channelId) ??
        GenesisRecordingCoreSnapshot.Unavailable;

    public bool IsKnownManualSession(ChannelId channelId, RecordingSessionId sessionId) =>
        _recordingController?.IsKnownManualSession(channelId, sessionId) ?? false;

    public ValueTask<GenesisActiveRecordingSessionSnapshot> GetActiveRecordingSessionsAsync(
        CancellationToken cancellationToken = default) =>
        _recordingController?.GetActiveRecordingSessionsAsync(cancellationToken) ??
        ValueTask.FromResult(GenesisActiveRecordingSessionSnapshot.Unavailable);

    public ValueTask<GenesisManualRecordingSessionSnapshot>
        GetActiveManualRecordingsAsync(CancellationToken cancellationToken = default) =>
        _recordingController?.GetActiveManualRecordingsAsync(cancellationToken) ??
        ValueTask.FromResult(GenesisManualRecordingSessionSnapshot.Unavailable);

    public ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default) =>
        _recordingController?.StartAsync(channel, cancellationToken) ??
        ValueTask.FromResult(new GenesisManualRecordingCommandOutcome(
            GenesisManualRecordingCommandKind.Rejected,
            GenesisRecordingCoreSnapshot.Unavailable));

    public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default) =>
        _recordingController?.StopAsync(channel, cancellationToken) ??
        ValueTask.FromResult(new GenesisManualRecordingCommandOutcome(
            GenesisManualRecordingCommandKind.Rejected,
            GenesisRecordingCoreSnapshot.Unavailable));

    public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken = default) =>
        _recordingController?.StopAsync(
            channelId,
            expectedSessionId,
            cancellationToken) ??
        ValueTask.FromResult(new GenesisManualRecordingCommandOutcome(
            GenesisManualRecordingCommandKind.Rejected,
            GenesisRecordingCoreSnapshot.Unavailable));

    public static GuiNextDailyPlaybackSessionController CreateSystem(
        string? explicitPlayerPath = null,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        GenesisRecordingHistoryStore? historyStore = null) =>
        new(new NovaDailyHttpFlvPlaybackController(
            explicitPlayerPath,
            static _ => { },
            static _ => { }, recordingSettings, historyStore));

    internal static GuiNextDailyPlaybackSessionController CreateSystemWithPathProvider(
        Func<string?> explicitPlayerPathProvider,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        GenesisRecordingHistoryStore? historyStore = null) =>
        new(NovaDailyHttpFlvPlaybackController.CreateWithPlayerPathProvider(
            explicitPlayerPathProvider,
            static _ => { },
            static _ => { }, recordingSettings, historyStore));

    public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
        string source,
        CancellationToken cancellationToken = default) =>
        PlayAsync(new PlaybackSessionRequest(source), cancellationToken);

    public async ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
        PlaybackSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome(PlaybackSessionCommandResult.Cancelled, Snapshot);
        }

        var source = request.Source?.Trim() ?? string.Empty;
        if (!DailyPcpPlaybackSourceParser.TryParse(source, out var parsed) ||
            parsed is null ||
            !ChannelIdMatches(request.ChannelId, parsed.ChannelId))
        {
            return RejectInvalidSource();
        }

        var channelName = string.IsNullOrWhiteSpace(request.ChannelName)
            ? source
            : request.ChannelName.Trim();
        var channelId = string.IsNullOrWhiteSpace(request.ChannelId)
            ? parsed.ChannelId.Value.ToString("N").ToUpperInvariant()
            : request.ChannelId.Trim();

        lock (_gate)
        {
            if (_disposed)
            {
                return Outcome(
                    PlaybackSessionCommandResult.Faulted,
                    new(
                        PlaybackSessionState.Faulted,
                        source,
                        PlaybackFailedMessage));
            }

            if (_snapshot.State is PlaybackSessionState.Starting or
                PlaybackSessionState.Stopping)
            {
                return Outcome(PlaybackSessionCommandResult.Rejected, _snapshot);
            }

            _snapshot = new PlaybackSessionSnapshot(
                PlaybackSessionState.Starting,
                source,
                null)
            {
                ChannelName = channelName,
                ChannelId = channelId,
            };
            _playStarting = true;
            _stopRequested = false;
            _pendingTermination = null;
        }

        NotifySnapshotChanged();

        try
        {
            var channel = request.CatalogChannel is { } catalog &&
                string.Equals(catalog.Source, source, StringComparison.Ordinal) && ChannelIdMatches(catalog.ChannelId, parsed.ChannelId)
                    ? catalog with { Name = channelName, Source = source, ChannelId = channelId }
                    : new GenesisChannelItem(channelName, source, ChannelId: channelId);
            await _dailyController.PlayAsync(
                    channel,
                    cancellationToken)
                .ConfigureAwait(false);

            PlaybackSessionCommandOutcome outcome;
            lock (_gate)
            {
                _playStarting = false;
                if (_pendingTermination is { } termination)
                {
                    _snapshot = SnapshotForTermination(
                        source,
                        termination,
                        channelName,
                        channelId);
                    outcome = OutcomeForSnapshot(_snapshot);
                    _pendingTermination = null;
                }
                else if (_stopRequested)
                {
                    _snapshot = PlaybackSessionSnapshot.Stopped;
                    outcome = Outcome(PlaybackSessionCommandResult.Cancelled, _snapshot);
                }
                else
                {
                    _snapshot = new PlaybackSessionSnapshot(
                        PlaybackSessionState.Playing,
                        source,
                        null)
                    {
                        ChannelName = channelName,
                        ChannelId = channelId,
                    };
                    outcome = Outcome(PlaybackSessionCommandResult.Completed, _snapshot);
                }
            }

            NotifySnapshotChanged();
            return outcome;
        }
        catch (OperationCanceledException)
        {
            PlaybackSessionSnapshot snapshot;
            lock (_gate)
            {
                _playStarting = false;
                _pendingTermination = null;
                snapshot = _snapshot = PlaybackSessionSnapshot.Stopped;
            }

            NotifySnapshotChanged();
            return Outcome(PlaybackSessionCommandResult.Cancelled, snapshot);
        }
        catch (Exception exception)
        {
            var message = exception switch
            {
                PcrPlayerExecutableResolutionException => PlayerUnavailableMessage,
                NovaDailyPlaybackStartupException startup => startup.Message,
                _ => PlaybackFailedMessage,
            };
            var failed = new PlaybackSessionSnapshot(
                PlaybackSessionState.Faulted,
                source,
                message)
            {
                ChannelName = channelName,
                ChannelId = channelId,
            };
            lock (_gate)
            {
                _playStarting = false;
                _pendingTermination = null;
                _snapshot = failed;
            }

            NotifySnapshotChanged();
            return Outcome(PlaybackSessionCommandResult.Faulted, failed);
        }
    }

    public async ValueTask<PlaybackSessionCommandOutcome> StopAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome(PlaybackSessionCommandResult.Cancelled, Snapshot);
        }

        PlaybackSessionSnapshot stopping;
        lock (_gate)
        {
            if (_disposed || _snapshot.State is PlaybackSessionState.Stopped or
                PlaybackSessionState.Faulted or PlaybackSessionState.Stopping)
            {
                return Outcome(PlaybackSessionCommandResult.Completed, _snapshot);
            }

            _stopRequested = true;
            stopping = _snapshot = _snapshot with
            {
                State = PlaybackSessionState.Stopping,
                ErrorMessage = null,
            };
        }

        NotifySnapshotChanged();
        try
        {
            await _dailyController.StopAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _playStarting = false;
                _pendingTermination = null;
                _stopRequested = false;
                _snapshot = PlaybackSessionSnapshot.Stopped;
                stopping = _snapshot;
            }

            NotifySnapshotChanged();
            return Outcome(PlaybackSessionCommandResult.Completed, stopping);
        }
        catch (Exception)
        {
            var failed = new PlaybackSessionSnapshot(
                PlaybackSessionState.Faulted,
                stopping.Source,
                StopFailedMessage)
            {
                ChannelName = stopping.ChannelName,
                ChannelId = stopping.ChannelId,
            };
            lock (_gate)
            {
                _stopRequested = false;
                _snapshot = failed;
            }

            NotifySnapshotChanged();
            return Outcome(PlaybackSessionCommandResult.Faulted, failed);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _dailyController.PlaybackEnded -= OnPlaybackEnded;
        try
        {
            await _dailyController.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            SetSnapshot(PlaybackSessionSnapshot.Stopped);
        }
    }

    private void OnPlaybackEnded(NovaDailyPlaybackTermination termination)
    {
        var changed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_playStarting)
            {
                if (string.Equals(
                        termination.Source,
                        _snapshot.Source,
                        StringComparison.Ordinal))
                {
                    _pendingTermination = termination;
                }

                return;
            }

            if (_snapshot.State is not (PlaybackSessionState.Playing or
                PlaybackSessionState.Stopping) ||
                !string.Equals(
                    termination.Source,
                    _snapshot.Source,
                    StringComparison.Ordinal))
            {
                return;
            }

            _snapshot = SnapshotForTermination(
                termination.Source,
                termination,
                _snapshot.ChannelName,
                _snapshot.ChannelId);
            changed = true;
        }

        if (changed)
        {
            NotifySnapshotChanged();
        }
    }

    private void SetSnapshot(PlaybackSessionSnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
        }

        NotifySnapshotChanged();
    }

    private PlaybackSessionCommandOutcome RejectInvalidSource()
    {
        PlaybackSessionSnapshot rejected;
        var changed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return Outcome(
                    PlaybackSessionCommandResult.Faulted,
                    new(
                        PlaybackSessionState.Faulted,
                        null,
                        PlaybackFailedMessage));
            }

            if (_snapshot.State is PlaybackSessionState.Starting or
                PlaybackSessionState.Playing or
                PlaybackSessionState.Stopping)
            {
                return Outcome(PlaybackSessionCommandResult.Rejected, _snapshot);
            }

            rejected = _snapshot = new(
                PlaybackSessionState.Stopped,
                null,
                InvalidSourceMessage);
            changed = true;
        }

        if (changed)
        {
            NotifySnapshotChanged();
        }

        return Outcome(PlaybackSessionCommandResult.Rejected, rejected);
    }

    private void NotifySnapshotChanged()
    {
        try
        {
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // UI observers must not interrupt the playback owner.
        }
    }

    private static bool ChannelIdMatches(string? requestedChannelId, ChannelId parsedId)
    {
        if (string.IsNullOrWhiteSpace(requestedChannelId))
        {
            return true;
        }

        return Guid.TryParse(requestedChannelId, out var requestedId) &&
            requestedId == parsedId.Value;
    }

    private static PlaybackSessionSnapshot SnapshotForTermination(
        string source,
        NovaDailyPlaybackTermination termination,
        string? channelName,
        string? channelId) =>
        termination.Failed
            ? new PlaybackSessionSnapshot(
                PlaybackSessionState.Faulted,
                source,
                EndedUnexpectedlyMessage)
            {
                ChannelName = channelName,
                ChannelId = channelId,
            }
            : PlaybackSessionSnapshot.Stopped;

    private static PlaybackSessionCommandOutcome OutcomeForSnapshot(
        PlaybackSessionSnapshot snapshot) =>
        Outcome(
            snapshot.State == PlaybackSessionState.Faulted
                ? PlaybackSessionCommandResult.Faulted
                : PlaybackSessionCommandResult.Completed,
            snapshot);

    private static PlaybackSessionCommandOutcome Outcome(
        PlaybackSessionCommandResult result,
        PlaybackSessionSnapshot snapshot) =>
        new(result, snapshot);
}
