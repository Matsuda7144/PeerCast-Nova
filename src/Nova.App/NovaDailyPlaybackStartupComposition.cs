using System.Collections.Concurrent;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal interface INovaDailyPlaybackControllerFactory
{
    INovaDailyPlaybackController Create(
        string? explicitPlayerPath,
        Action<string> setStatus,
        Action<string> showError);
}

internal sealed class SystemNovaDailyPlaybackControllerFactory : INovaDailyPlaybackControllerFactory
{
    public static SystemNovaDailyPlaybackControllerFactory Instance { get; } = new();

    private SystemNovaDailyPlaybackControllerFactory()
    {
    }

    public INovaDailyPlaybackController Create(
        string? explicitPlayerPath,
        Action<string> setStatus,
        Action<string> showError) =>
        new NovaDailyHttpFlvPlaybackController(explicitPlayerPath, setStatus, showError);
}

internal sealed class NovaDailyPlaybackStartupComposition
{
    internal const string WindowTitle = "PeerCast Nova - Playback and Relay";

    private readonly string? _explicitPlayerPath;
    private readonly INovaDailyPlaybackWindowFactory _windowFactory;
    private readonly INovaDailyPlaybackControllerFactory _controllerFactory;
    private readonly INovaDailyRelayControllerFactory _relayControllerFactory;
    private readonly IGenesisChannelCatalog _catalog;

    public NovaDailyPlaybackStartupComposition(string? explicitPlayerPath)
        : this(
            explicitPlayerPath,
            SystemNovaDailyPlaybackWindowFactory.Instance,
            SystemNovaDailyPlaybackControllerFactory.Instance,
            SpGenesisChannelCatalog.Create(),
            SystemNovaDailyRelayControllerFactory.Instance)
    {
    }

    internal NovaDailyPlaybackStartupComposition(
        string? explicitPlayerPath,
        INovaDailyPlaybackWindowFactory windowFactory,
        INovaDailyPlaybackControllerFactory controllerFactory,
        IGenesisChannelCatalog catalog,
        INovaDailyRelayControllerFactory? relayControllerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(windowFactory);
        ArgumentNullException.ThrowIfNull(controllerFactory);
        ArgumentNullException.ThrowIfNull(catalog);
        _explicitPlayerPath = explicitPlayerPath;
        _windowFactory = windowFactory;
        _controllerFactory = controllerFactory;
        _relayControllerFactory = relayControllerFactory ??
            SystemNovaDailyRelayControllerFactory.Instance;
        _catalog = catalog;
    }

    public async ValueTask<NovaWindowsStartupResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        await using var window = _windowFactory.Create(WindowTitle);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var start = await window.StartAsync(lifetime.Token).ConfigureAwait(false);
        if (start != NovaDailyPlaybackWindowResult.Started)
        {
            return start switch
            {
                NovaDailyPlaybackWindowResult.PlatformUnsupported =>
                    NovaWindowsStartupResult.PlatformUnsupported,
                NovaDailyPlaybackWindowResult.Cancelled => NovaWindowsStartupResult.Cancelled,
                _ => NovaWindowsStartupResult.Faulted,
            };
        }

        await using var controller = _controllerFactory.Create(
            _explicitPlayerPath,
            window.SetStatus,
            window.ShowError);
        await using var relayController = _relayControllerFactory.Create(
            window.SetStatus,
            window.SetRelayEndpoint,
            window.ShowError);
        var operations = new OperationTracker();
        var channelState = new ChannelSelectionState();

        void RefreshRequested() => operations.Start(async () =>
        {
            await StopRelayAsync(relayController, window).ConfigureAwait(false);
            await RefreshAsync(window, channelState.Replace, lifetime.Token).ConfigureAwait(false);
        });
        void PlayRequested(int index) => operations.Start(
            () => PlayAsync(controller, window, channelState.Snapshot, index, lifetime.Token));
        void StopRequested() => operations.Start(
            () => StopAsync(controller, window));
        void RelayRequested(int index) => operations.Start(
            () => StartRelayAsync(
                relayController,
                window,
                channelState.Snapshot,
                index,
                lifetime.Token));
        void StopRelayRequested() => operations.Start(
            () => StopRelayAsync(relayController, window));
        void ChannelSelectionChanged() => operations.Start(
            () => StopRelayAsync(relayController, window));

        window.RefreshRequested += RefreshRequested;
        window.PlayRequested += PlayRequested;
        window.StopRequested += StopRequested;
        window.RelayRequested += RelayRequested;
        window.StopRelayRequested += StopRelayRequested;
        window.ChannelSelectionChanged += ChannelSelectionChanged;
        window.SetStatus("チャンネル一覧を取得しています…");
        operations.Start(() => RefreshAsync(window, channelState.Replace, lifetime.Token));

        try
        {
            var exit = await window.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return exit switch
            {
                NovaDailyPlaybackWindowResult.Stopped => NovaWindowsStartupResult.Completed,
                NovaDailyPlaybackWindowResult.Cancelled => NovaWindowsStartupResult.Cancelled,
                _ => NovaWindowsStartupResult.Faulted,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NovaWindowsStartupResult.Cancelled;
        }
        finally
        {
            lifetime.Cancel();
            window.RefreshRequested -= RefreshRequested;
            window.PlayRequested -= PlayRequested;
            window.StopRequested -= StopRequested;
            window.RelayRequested -= RelayRequested;
            window.StopRelayRequested -= StopRelayRequested;
            window.ChannelSelectionChanged -= ChannelSelectionChanged;
            await operations.DrainAsync().ConfigureAwait(false);
            try
            {
                await relayController.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                window.ShowError($"終了時のRelay cleanupに失敗しました: {exception.Message}");
            }
            try
            {
                await controller.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                window.ShowError($"終了時の再生cleanupに失敗しました: {exception.Message}");
            }
        }
    }

    private async Task RefreshAsync(
        INovaDailyPlaybackWindow window,
        Action<IReadOnlyList<GenesisChannelItem>> replaceChannels,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _catalog.ReadAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var channels = snapshot.Channels
                .Where(static channel => !string.IsNullOrWhiteSpace(channel.Name))
                .Where(static channel => DailyPcpPlaybackSourceParser.TryParse(channel.Source, out _))
                .ToArray();
            replaceChannels(channels);
            window.SetChannels(channels.Select(static channel => channel.Name).ToArray());
            window.SetStatus($"{channels.Length} チャンネルを読み込みました。チャンネルを選択して再生してください。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            window.ShowError($"チャンネル一覧を取得できませんでした: {exception.Message}");
        }
    }

    private static async Task PlayAsync(
        INovaDailyPlaybackController controller,
        INovaDailyPlaybackWindow window,
        Func<IReadOnlyList<GenesisChannelItem>> channels,
        int index,
        CancellationToken cancellationToken)
    {
        var available = channels();
        if (index < 0 || index >= available.Count)
        {
            window.ShowError("再生するチャンネルを選択してください。");
            return;
        }

        try
        {
            await controller.PlayAsync(available[index], cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            window.ShowError(exception.Message);
        }
    }

    private static async Task StopAsync(
        INovaDailyPlaybackController controller,
        INovaDailyPlaybackWindow window)
    {
        window.SetStatus("停止しています…");
        try
        {
            await controller.StopAsync().ConfigureAwait(false);
            window.SetStatus("Ready");
        }
        catch (Exception exception)
        {
            window.ShowError($"再生の停止に失敗しました: {exception.Message}");
        }
    }

    private static async Task StartRelayAsync(
        INovaDailyRelayController relayController,
        INovaDailyPlaybackWindow window,
        Func<IReadOnlyList<GenesisChannelItem>> channels,
        int index,
        CancellationToken cancellationToken)
    {
        var available = channels();
        if (index < 0 || index >= available.Count)
        {
            window.ShowError("Relayするチャンネルを選択してください。");
            return;
        }

        try
        {
            await relayController.StartAsync(available[index], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            window.SetRelayEndpoint(string.Empty);
            window.ShowError(exception.Message);
        }
    }

    private static async Task StopRelayAsync(
        INovaDailyRelayController relayController,
        INovaDailyPlaybackWindow window)
    {
        try
        {
            await relayController.StopAsync().ConfigureAwait(false);
            window.SetRelayEndpoint(string.Empty);
        }
        catch (Exception exception)
        {
            window.ShowError($"PCP Relayの停止に失敗しました: {exception.Message}");
        }
    }

    private sealed class OperationTracker
    {
        private readonly ConcurrentDictionary<long, Task> _operations = new();
        private long _nextId;

        public void Start(Func<Task> operation)
        {
            var id = Interlocked.Increment(ref _nextId);
            var task = operation();
            if (task.IsCompleted)
            {
                return;
            }

            _operations[id] = task;
            _ = task.ContinueWith(
                completed =>
                {
                    _operations.TryRemove(id, out _);
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public async Task DrainAsync()
        {
            while (!_operations.IsEmpty)
            {
                await Task.WhenAll(_operations.Values).ConfigureAwait(false);
            }
        }
    }

    private sealed class ChannelSelectionState
    {
        private readonly object _gate = new();
        private IReadOnlyList<GenesisChannelItem> _channels = [];

        public void Replace(IReadOnlyList<GenesisChannelItem> channels)
        {
            lock (_gate)
            {
                _channels = channels;
            }
        }

        public IReadOnlyList<GenesisChannelItem> Snapshot()
        {
            lock (_gate)
            {
                return _channels;
            }
        }
    }
}
