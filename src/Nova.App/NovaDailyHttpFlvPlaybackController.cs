using System.Diagnostics;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal interface INovaDailyPlaybackController : IAsyncDisposable
{
    event Action<NovaDailyPlaybackTermination>? PlaybackEnded;

    Task PlayAsync(GenesisChannelItem channel, CancellationToken cancellationToken);

    Task StopAsync();
}

internal sealed record NovaDailyPlaybackTermination(string Source, bool Failed);

internal sealed class NovaDailyHttpFlvPlaybackController :
    INovaDailyPlaybackController,
    IGenesisRecordingLibraryController,
    IGenesisRecordingHistoryController,
    IGenesisManualRecordingSessionController,
    IGenesisAutomaticRecordingCatalogObserver
{
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(30);

    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly Func<IDailyPlaybackPlayerLauncher> _createPlayerLauncher;
    private readonly GenesisSharedPcpPlaybackRuntime _sharedPlaybackRuntime;
    private readonly PcpRecordingRuntimeComposition _recordingComposition;
    private readonly GenesisManualRecordingSessionController _manualRecordingController;
    private readonly GenesisAutomaticRecordingEvaluationController
        _automaticRecordingController;
    private readonly Action<string> _setStatus;
    private readonly Action<string> _showError;
    private ActivePlayback? _active;
    private CancellationTokenSource? _startupCancellation;
    private bool _disposed;

    public event Action<NovaDailyPlaybackTermination>? PlaybackEnded;

    public NovaDailyHttpFlvPlaybackController(
        string? explicitPlayerPath,
        Action<string> setStatus,
        Action<string> showError,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        GenesisRecordingHistoryStore? historyStore = null)
        : this(
            () => new PcrPlayerExternalPlayerLauncher(explicitPlayerPath),
            setStatus,
            showError,
            CreateProductionRecordingComposition(recordingSettings, historyStore))
    {
    }

    private NovaDailyHttpFlvPlaybackController(
        Func<IDailyPlaybackPlayerLauncher> createPlayerLauncher,
        Action<string> setStatus,
        Action<string> showError,
        PcpRecordingRuntimeComposition recordingComposition,
        GenesisGuiNextPublicChannelComposition? publicChannels = null)
        : this(
            createPlayerLauncher,
            setStatus,
            showError,
            recordingComposition,
            new GenesisSharedPcpPlaybackRuntime(recordingComposition, publicChannels))
    {
    }

    internal NovaDailyHttpFlvPlaybackController(
        IDailyPlaybackPlayerLauncher playerLauncher,
        Action<string> setStatus,
        Action<string> showError,
        PcpRecordingRuntimeComposition recordingComposition,
        GenesisSharedPcpPlaybackRuntime sharedPlaybackRuntime,
        IGenesisRecordingRuleSetProvider? ruleSetProvider = null)
        : this(() => playerLauncher, setStatus, showError, recordingComposition, sharedPlaybackRuntime, ruleSetProvider)
    {
        ArgumentNullException.ThrowIfNull(playerLauncher);
    }

    internal NovaDailyHttpFlvPlaybackController(
        Func<IDailyPlaybackPlayerLauncher> createPlayerLauncher,
        Action<string> setStatus,
        Action<string> showError,
        PcpRecordingRuntimeComposition recordingComposition,
        GenesisSharedPcpPlaybackRuntime sharedPlaybackRuntime,
        IGenesisRecordingRuleSetProvider? ruleSetProvider = null)
    {
        ArgumentNullException.ThrowIfNull(createPlayerLauncher);
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(showError);
        ArgumentNullException.ThrowIfNull(recordingComposition);
        ArgumentNullException.ThrowIfNull(sharedPlaybackRuntime);
        _createPlayerLauncher = createPlayerLauncher;
        _recordingComposition = recordingComposition;
        _sharedPlaybackRuntime = sharedPlaybackRuntime;
        _manualRecordingController = new(
            sharedPlaybackRuntime,
            recordingComposition.Recording);
        _automaticRecordingController = new(
            recordingComposition.Recording,
            sharedPlaybackRuntime,
            ruleSetProvider ?? GenesisRecordingRuleFileProvider.CreateSystem());
        _setStatus = setStatus;
        _showError = showError;
    }

    public ValueTask<GenesisAutomaticRecordingEvaluationSummary> ObserveAsync(
        IReadOnlyList<GenesisChannelItem> channels,
        CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            if (_active is { } active)
            {
                var updated = channels.FirstOrDefault(c => DailyPcpPlaybackSourceParser.TryParse(c.Source, out var s) && s?.ChannelId.Value == active.ChannelId);
                if (updated is not null)
                {
                    active.CatalogChannel = updated;
                    active.CatalogTimestamp = Stopwatch.GetTimestamp();
                }
            }
        }
        return _automaticRecordingController.ObserveAsync(channels, cancellationToken);
    }

    internal IGenesisAutomaticRecordingUserCommands AutomaticRecordingUserCommands => _automaticRecordingController;

    GenesisRecordingHistorySnapshot IGenesisRecordingHistoryController.GetRecordingHistorySnapshot() => GetRecordingHistorySnapshot();
    GenesisRecordingHistoryDurabilitySnapshot? IGenesisRecordingHistoryController.GetRecordingHistoryDurabilitySnapshot() => GetRecordingHistoryDurabilitySnapshot();
    GenesisRecordingLibrarySnapshot IGenesisRecordingHistoryController.GetRecordingLibrarySnapshot() => GetRecordingLibrarySnapshot();

    internal GenesisRecordingHistorySnapshot GetRecordingHistorySnapshot() =>
        _recordingComposition.RecordingHistory.GetSnapshot();

    internal GenesisRecordingHistoryDurabilitySnapshot? GetRecordingHistoryDurabilitySnapshot() =>
        _recordingComposition.GetHistoryDurabilitySnapshot();

    internal GenesisRecordingLibraryKnownRootSnapshot GetKnownRecordingRootsSnapshot() =>
        _recordingComposition.KnownRecordingRoots.GetSnapshot();

    internal GenesisRecordingRootRegistrationResult RegisterRecordingLibraryRoot(string? path) =>
        _recordingComposition.KnownRecordingRoots.RegisterExplicit(path);

    internal GenesisRecordingLibrarySnapshot GetRecordingLibrarySnapshot() =>
        _recordingComposition.RecordingLibrary.GetSnapshot();

    internal Task<GenesisRecordingLibraryRefreshResult> RefreshRecordingLibraryAsync(CancellationToken cancellationToken = default) =>
        _recordingComposition.RecordingLibrary.RefreshAsync(cancellationToken);

    GenesisRecordingLibrarySnapshot IGenesisRecordingLibraryController.GetSnapshot() =>
        GetRecordingLibrarySnapshot();

    Task<GenesisRecordingLibraryRefreshResult> IGenesisRecordingLibraryController.RefreshAsync(CancellationToken cancellationToken) =>
        RefreshRecordingLibraryAsync(cancellationToken);

    internal static NovaDailyHttpFlvPlaybackController CreateWithPlayerLauncherFactory(
        Func<IDailyPlaybackPlayerLauncher> createPlayerLauncher,
        Action<string> setStatus,
        Action<string> showError,
        GenesisRecordingOutputDirectorySettings? recordingSettings = null,
        GenesisRecordingHistoryStore? historyStore = null,
        GenesisGuiNextPublicChannelComposition? publicChannels = null) =>
        new(
            createPlayerLauncher,
            setStatus,
            showError,
            CreateProductionRecordingComposition(recordingSettings, historyStore),
            publicChannels);

    internal static PcpRecordingRuntimeComposition CreateProductionRecordingComposition(
        GenesisRecordingOutputDirectorySettings? settings = null,
        GenesisRecordingHistoryStore? historyStore = null)
    {
        settings ??= GenesisRecordingOutputDirectorySettings.CreateSystem();
        var diagnostics = RecordingTransformDiagnosticFile.FromEnvironment();
        try
        {
            return new(settings.CurrentRuntimeDirectory, historyStore ?? GenesisRecordingHistoryStore.CreateSystem(),
                defaultRecordingDirectory: settings.DefaultDirectory, diagnostics: diagnostics);
        }
        catch
        {
            if (diagnostics is not null)
                _ = diagnostics.DisposeAsync().AsTask();
            throw;
        }
    }

    public GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel) =>
        _manualRecordingController.GetSnapshot(channel);

    public GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channelId) =>
        _manualRecordingController.GetSnapshot(channelId);

    public bool IsKnownManualSession(ChannelId channelId, RecordingSessionId sessionId) =>
        _manualRecordingController.IsKnownManualSession(channelId, sessionId);

    public ValueTask<GenesisActiveRecordingSessionSnapshot> GetActiveRecordingSessionsAsync(
        CancellationToken cancellationToken = default) =>
        _manualRecordingController.GetActiveRecordingSessionsAsync(cancellationToken);

    public ValueTask<GenesisManualRecordingSessionSnapshot>
        GetActiveManualRecordingsAsync(CancellationToken cancellationToken = default) =>
        _manualRecordingController.GetActiveManualRecordingsAsync(cancellationToken);

    public ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default) =>
        _manualRecordingController.StartAsync(channel, cancellationToken);

    public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default) =>
        _manualRecordingController.StopAsync(channel, cancellationToken);

    public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken = default) =>
        _manualRecordingController.StopAsync(
            channelId,
            expectedSessionId,
            cancellationToken);

    public async Task PlayAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        await _commands.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        ActivePlayback? startingPlayback = null;

        try
        {
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _startupCancellation = startupCancellation;
            }

            if (!DailyPcpPlaybackSourceParser.TryParse(channel.Source, out var source) ||
                source is null)
            {
                throw new InvalidOperationException(
                    "The selected channel does not provide a valid pcp://.../channel/<id> source.");
            }

            var playerLauncher = _createPlayerLauncher();
            playerLauncher.ValidateConfiguration();
            await StopActiveCoreAsync().ConfigureAwait(false);
            startupCancellation.Token.ThrowIfCancellationRequested();

            var ready = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ActivePlayback? active = null;
            var host = new PcpHttpFlvPlaybackHost(
                streamUrlReady: streamUrl => ready.TrySetResult(streamUrl),
                playerChannelInfo: id => ReadPlayerInfo(active, id));
            active = new ActivePlayback(host) { ChannelId = source.ChannelId.Value, CatalogChannel = channel };
            startingPlayback = active;
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _active = active;
            }

            _setStatus($"{channel.Name}: PCP接続中…");
            host.Start();
            var playbackAttachment = await _sharedPlaybackRuntime.AttachPlaybackAsync(
                source,
                (channelId, sessionId) => host.Create(channelId, sessionId),
                startupCancellation.Token).ConfigureAwait(false);
            active.Consumer = playbackAttachment.Consumer;
            active.Registration = playbackAttachment.Registration;
            active.OwnerTask = playbackAttachment.OwnerCompletion;
            active.Attachment = playbackAttachment;

            _setStatus($"{channel.Name}: ストリームを準備しています…");
            Task readyOrEnded;
            try
            {
                readyOrEnded = await Task.WhenAny(ready.Task, active.OwnerTask)
                    .WaitAsync(BootstrapTimeout, startupCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw NovaDailyPlaybackStartupException.HeaderTimeout();
            }
            if (ReferenceEquals(readyOrEnded, active.OwnerTask))
            {
                var result = await active.OwnerTask.ConfigureAwait(false);
                throw NovaDailyPlaybackStartupException.FromResult(result, playbackAttachment.ReadFault?.Invoke());
            }

            var streamUrl = await ready.Task.ConfigureAwait(false);
            startupCancellation.Token.ThrowIfCancellationRequested();
            _setStatus($"{channel.Name}: プレイヤー起動中…");
            var info = ReadPlayerInfo(active, source.ChannelId.Value);
            active.Player = await playerLauncher.LaunchAsync(
                new(streamUrl, host.PlaylistUrlFor(source.ChannelId),
                    info?.Metadata.Name ?? channel.Name, info is null ? channel.ContactUrl : info.Metadata.ContactUrl),
                startupCancellation.Token).ConfigureAwait(false);
            _setStatus($"{channel.Name}: 再生中");
            _ = ObservePlaybackEndAsync(active, channel.Name, channel.Source);
        }
        catch
        {
            if (startingPlayback is not null)
            {
                await StopActiveCoreAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            lock (_stateGate)
            {
                if (ReferenceEquals(_startupCancellation, startupCancellation))
                {
                    _startupCancellation = null;
                }
            }

            _commands.Release();
        }
    }

    public async Task StopAsync()
    {
        lock (_stateGate)
        {
            try
            {
                _startupCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Startup completed while Stop was waiting for ownership.
            }
        }

        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopActiveCoreAsync().ConfigureAwait(false);
            _setStatus("Ready");
        }
        finally
        {
            _commands.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _startupCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Startup completed while shutdown was requested.
            }
        }

        Exception? cleanupFailure = null;
        try
        {
            await _automaticRecordingController.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null
                ? exception
                : new AggregateException(
                    "External playback cleanup failed during shutdown.",
                    cleanupFailure,
                    exception);
        }

        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopActiveCoreAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }
        finally
        {
            _commands.Release();
            _commands.Dispose();
        }

        try
        {
            await _manualRecordingController.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null
                ? exception
                : new AggregateException(
                    "Manual recording commands did not drain cleanly during shutdown.",
                    cleanupFailure,
                    exception);
        }

        try
        {
            // Recording sessions release their fan-out leases before the
            // relay runtime disposes the shared upstream owners.
            await _recordingComposition.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null
                ? exception
                : new AggregateException(
                    "Playback and shared recording ownership did not shut down cleanly.",
                    cleanupFailure,
                    exception);
        }

        try
        {
            await _sharedPlaybackRuntime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null
                ? exception
                : new AggregateException(
                    "The shared PCP playback owner failed to release its application state.",
                    cleanupFailure,
                    exception);
        }

        if (cleanupFailure is not null)
        {
            throw cleanupFailure;
        }
    }

    private async Task StopActiveCoreAsync()
    {
        ActivePlayback? active;
        lock (_stateGate)
        {
            active = _active;
            _active = null;
        }

        if (active is null)
        {
            return;
        }

        List<Exception>? failures = null;
        await CleanupStepAsync(
            () => active.Player?.DisposeAsync().AsTask() ?? Task.CompletedTask,
            failures ??= []).ConfigureAwait(false);

        if (active.Registration is not null)
        {
            await CleanupStepAsync(
                () => active.Registration.DisposeAsync().AsTask(),
                failures ??= []).ConfigureAwait(false);
        }

        if (active.Consumer is not null)
        {
            await CleanupStepAsync(
                () => active.Consumer.DisposeAsync().AsTask(),
                failures ??= []).ConfigureAwait(false);
        }

        await CleanupStepAsync(
            () => active.Host.DisposeAsync().AsTask(),
            failures ??= []).ConfigureAwait(false);

        if (failures is { Count: > 0 })
        {
            throw new AggregateException("The playback session did not clean up completely.", failures);
        }
    }

    private async Task ObservePlaybackEndAsync(
        ActivePlayback active,
        string channelName,
        string source)
    {
        try
        {
            var ownerTask = active.OwnerTask;
            var playerCompletion = active.Player!.Completion;
            var endedTask = ownerTask is null
                ? playerCompletion
                : await Task.WhenAny(ownerTask, playerCompletion)
                    .ConfigureAwait(false);
            bool failed;
            if (ownerTask is not null && ReferenceEquals(endedTask, ownerTask))
            {
                failed = await IsSharedOwnerTerminalFailureAsync(ownerTask)
                    .ConfigureAwait(false);
            }
            else
            {
                await endedTask.ConfigureAwait(false);
                failed = false;
            }

            lock (_stateGate)
            {
                if (!ReferenceEquals(_active, active))
                {
                    return;
                }
            }

            PublishPlaybackEnded(source, failed);
            _setStatus(failed
                ? $"{channelName}: 再生セッションが予期せず終了しました"
                : $"{channelName}: 再生が終了しました");
            await StopIfCurrentAsync(active).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_stateGate)
            {
                if (!ReferenceEquals(_active, active))
                {
                    return;
                }
            }

            try
            {
                _showError($"再生セッションが終了しました: {exception.Message}");
            }
            catch
            {
                // Error presentation must not prevent ownership cleanup.
            }

            PublishPlaybackEnded(source, failed: true);

            try
            {
                await StopIfCurrentAsync(active).ConfigureAwait(false);
            }
            catch
            {
                // The original session failure has already been reported.
            }
        }
    }

    private async Task StopIfCurrentAsync(ActivePlayback expectedActive)
    {
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (!ReferenceEquals(_active, expectedActive))
                {
                    return;
                }
            }

            await StopActiveCoreAsync().ConfigureAwait(false);
            _setStatus("Ready");
        }
        finally
        {
            _commands.Release();
        }
    }

    internal static async Task<bool> IsSessionTerminalFailureAsync(
        Task<PcpChannelSessionResult> sessionTask)
    {
        ArgumentNullException.ThrowIfNull(sessionTask);
        try
        {
            var result = await sessionTask.ConfigureAwait(false);
            return result.EndReason != PcpChannelSessionEndReason.Stopped;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    internal static async Task<bool> IsSharedOwnerTerminalFailureAsync(
        Task<PcpRelayCoordinatorResult> ownerTask)
    {
        ArgumentNullException.ThrowIfNull(ownerTask);
        try
        {
            var result = await ownerTask.ConfigureAwait(false);
            return result.EndReason != PcpRelayCoordinatorEndReason.Stopped;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void PublishPlaybackEnded(string source, bool failed)
    {
        try
        {
            PlaybackEnded?.Invoke(new(source, failed));
        }
        catch
        {
            // Playback cleanup must not depend on GUI state observers.
        }
    }

    private static async Task CleanupStepAsync(
        Func<Task> cleanup,
        List<Exception> failures)
    {
        try
        {
            await cleanup().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private PcpPlayerChannelInfo? ReadPlayerInfo(ActivePlayback? active, Guid id)
    {
        lock (_stateGate)
        {
            if (active is null || !ReferenceEquals(active, _active) || active.ChannelId != id || active.CatalogChannel is null) return null;
            return GenesisPlayerChannelInfo.Create(active.CatalogChannel, active.Attachment?.ReadStatus?.Invoke(),
                active.Attachment?.ReadPeerCounts?.Invoke(), Stopwatch.GetElapsedTime(active.StartTimestamp),
                Stopwatch.GetElapsedTime(active.CatalogTimestamp));
        }
    }

    private sealed class ActivePlayback(PcpHttpFlvPlaybackHost host)
    {
        public Guid ChannelId { get; init; }
        public GenesisChannelItem? CatalogChannel { get; set; }
        public long StartTimestamp { get; } = Stopwatch.GetTimestamp();
        public long CatalogTimestamp { get; set; } = Stopwatch.GetTimestamp();
        public GenesisSharedPcpPlaybackAttachment? Attachment { get; set; }
        public PcpHttpFlvPlaybackHost Host { get; } = host;

        public IOwnedPcpChannelPacketConsumer? Consumer { get; set; }

        public PcpRelayConsumerRegistration? Registration { get; set; }

        public Task<PcpRelayCoordinatorResult>? OwnerTask { get; set; }

        public IDailyPlaybackPlayerLifetime? Player { get; set; }
    }
}
