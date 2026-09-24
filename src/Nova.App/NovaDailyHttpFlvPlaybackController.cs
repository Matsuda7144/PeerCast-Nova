using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

internal interface INovaDailyPlaybackController : IAsyncDisposable
{
    Task PlayAsync(GenesisChannelItem channel, CancellationToken cancellationToken);

    Task StopAsync();
}

internal sealed class NovaDailyHttpFlvPlaybackController : INovaDailyPlaybackController
{
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromSeconds(30);

    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly PcrPlayerExternalPlayerLauncher _playerLauncher;
    private readonly Action<string> _setStatus;
    private readonly Action<string> _showError;
    private ActivePlayback? _active;
    private CancellationTokenSource? _startupCancellation;
    private bool _disposed;

    public NovaDailyHttpFlvPlaybackController(
        string? explicitPlayerPath,
        Action<string> setStatus,
        Action<string> showError)
    {
        ArgumentNullException.ThrowIfNull(setStatus);
        ArgumentNullException.ThrowIfNull(showError);
        _playerLauncher = new PcrPlayerExternalPlayerLauncher(explicitPlayerPath);
        _setStatus = setStatus;
        _showError = showError;
    }

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

            _playerLauncher.ValidateConfiguration();
            await StopActiveCoreAsync().ConfigureAwait(false);
            startupCancellation.Token.ThrowIfCancellationRequested();

            var ready = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var host = new PcpHttpFlvPlaybackHost(
                streamUrlReady: streamUrl => ready.TrySetResult(streamUrl));
            var active = new ActivePlayback(host);
            startingPlayback = active;
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _active = active;
            }

            _setStatus($"{channel.Name}: PCP接続中…");
            host.Start();
            var sessionId = new ChannelSessionId(Guid.NewGuid());
            active.Consumer = host.Create(source.ChannelId, sessionId);
            var tracker = new ChannelStatusTracker(source.ChannelId, sessionId);
            active.Session = new PcpChannelSession(
                new TcpTransportConnector(),
                tracker,
                active.Consumer);
            active.SessionTask = active.Session.RunAsync(
                source.Host,
                source.Port,
                startupCancellation.Token);

            var negotiationOrEnded = await Task.WhenAny(
                    active.Session.Negotiation,
                    active.SessionTask)
                .WaitAsync(BootstrapTimeout, startupCancellation.Token)
                .ConfigureAwait(false);
            if (ReferenceEquals(negotiationOrEnded, active.SessionTask))
            {
                var result = await active.SessionTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"The PCP session ended before negotiation completed ({result.EndReason}).");
            }

            var negotiation = await active.Session.Negotiation.ConfigureAwait(false);
            if (negotiation.Outcome != PcpChannelSessionNegotiationOutcome.Accepted)
            {
                throw new InvalidOperationException(
                    $"The PCP relay rejected the channel request " +
                    $"(HTTP {negotiation.HttpStatusCode}, {negotiation.CompatibilityOutcome}).");
            }

            _setStatus($"{channel.Name}: FLV streamを準備しています…");
            var readyOrEnded = await Task.WhenAny(ready.Task, active.SessionTask)
                .WaitAsync(BootstrapTimeout, startupCancellation.Token)
                .ConfigureAwait(false);
            if (ReferenceEquals(readyOrEnded, active.SessionTask))
            {
                var result = await active.SessionTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"The PCP session ended before an FLV header arrived ({result.EndReason}).");
            }

            var streamUrl = await ready.Task.ConfigureAwait(false);
            startupCancellation.Token.ThrowIfCancellationRequested();
            _setStatus($"{channel.Name}: PCRPlayer起動中…");
            active.Player = _playerLauncher.Launch(streamUrl);
            _setStatus($"{channel.Name}: 再生中");
            _ = ObservePlaybackEndAsync(active, channel.Name);
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

        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopActiveCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _commands.Release();
            _commands.Dispose();
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

        if (active.Session is not null)
        {
            active.Session.RequestStop();
            await CleanupStepAsync(
                () => active.Session.StopAsync(CancellationToken.None),
                failures ??= []).ConfigureAwait(false);
            await CleanupStepAsync(
                async () =>
                {
                    if (active.SessionTask is not null)
                    {
                        await active.SessionTask.ConfigureAwait(false);
                    }
                },
                failures ??= []).ConfigureAwait(false);
            await CleanupStepAsync(
                () => active.Session.DisposeAsync().AsTask(),
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

    private async Task ObservePlaybackEndAsync(ActivePlayback active, string channelName)
    {
        try
        {
            var endedTask = active.SessionTask is null
                ? active.Player!.Completion
                : await Task.WhenAny(active.SessionTask, active.Player!.Completion)
                    .ConfigureAwait(false);
            await endedTask.ConfigureAwait(false);

            lock (_stateGate)
            {
                if (!ReferenceEquals(_active, active))
                {
                    return;
                }
            }

            _setStatus($"{channelName}: 再生が終了しました");
            await StopAsync().ConfigureAwait(false);
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

            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // The original session failure has already been reported.
            }
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

    private sealed class ActivePlayback(PcpHttpFlvPlaybackHost host)
    {
        public PcpHttpFlvPlaybackHost Host { get; } = host;

        public IOwnedPcpChannelPacketConsumer? Consumer { get; set; }

        public PcpChannelSession? Session { get; set; }

        public Task<PcpChannelSessionResult>? SessionTask { get; set; }

        public IDailyExternalPlayerLifetime? Player { get; set; }
    }
}
