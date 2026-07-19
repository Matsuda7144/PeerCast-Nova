namespace PeerCast.Nova.Playback;

public enum PlaybackRecoveryAwarePumpState
{
    Idle,
    Running,
    Recovering,
    Stopping,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackRecoveryAwarePumpResult
{
    Completed,
    Stopped,
    IdleLimitReached,
    PumpLimitReached,
    DurationLimitReached,
    RecoveryLimitReached,
    ResetRequired,
    DeviceLost,
    ResizeRequired,
    InitializationRequired,
    BindingRequired,
    InvalidConfiguration,
    InvalidEnvironment,
    AlreadyRunning,
    Cancelled,
    Disposed,
    Failure,
}

public sealed record PlaybackRecoveryAwarePumpOptions
{
    public const int MaximumAllowedRecoveries = 16;

    public int MaximumRecoveries { get; init; } = 1;
    public PlaybackLateAwareContinuousPumpOptions PumpOptions { get; init; } = new();
    public PlaybackAudioRecoveryOptions RecoveryOptions { get; init; } = new();
}

public readonly record struct PlaybackRecoveryAwarePumpSnapshot(
    PlaybackRecoveryAwarePumpState State,
    int RecoveryCount,
    long RunGeneration,
    long VideoGeneration,
    long AudioGeneration);

public sealed class PlaybackRecoveryAwarePumpOrchestrator : IDisposable
{
    private readonly object _sync = new();
    private readonly IPlaybackRecoveryAwarePump _pump;
    private readonly IPlaybackRecoveryAwareAudioRecovery _recovery;
    private CancellationTokenSource? _runCancellation;
    private Task<PlaybackRecoveryAwarePumpResult>? _activeRun;
    private PlaybackRecoveryAwarePumpState _state;
    private int _recoveryCount;
    private long _runGeneration;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _stopRequested;
    private bool _isDisposed;

    public PlaybackRecoveryAwarePumpOrchestrator(
        PlaybackLateAwareContinuousPumpBoundary pump,
        PlaybackAudioRecoveryBoundary recovery)
        : this(new PlaybackRecoveryAwarePump(pump), new PlaybackRecoveryAwareAudioRecovery(recovery))
    {
    }

    internal PlaybackRecoveryAwarePumpOrchestrator(
        IPlaybackRecoveryAwarePump pump,
        IPlaybackRecoveryAwareAudioRecovery recovery)
    {
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(recovery);
        _pump = pump;
        _recovery = recovery;
    }

    public PlaybackRecoveryAwarePumpSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_state, _recoveryCount, _runGeneration,
                    _videoGeneration, _audioGeneration);
        }
    }

    public async ValueTask<PlaybackRecoveryAwarePumpResult> RunAsync(
        long videoGeneration,
        long audioGeneration,
        PlaybackRecoveryAwarePumpOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!Validate(videoGeneration, audioGeneration, options))
            return PlaybackRecoveryAwarePumpResult.InvalidConfiguration;

        Task<PlaybackRecoveryAwarePumpResult> run;
        lock (_sync)
        {
            if (_isDisposed) return PlaybackRecoveryAwarePumpResult.Disposed;
            if (_activeRun is not null || _state == PlaybackRecoveryAwarePumpState.Stopping)
                return PlaybackRecoveryAwarePumpResult.AlreadyRunning;
            if (_state == PlaybackRecoveryAwarePumpState.ResetRequired)
                return PlaybackRecoveryAwarePumpResult.ResetRequired;
            if (_state == PlaybackRecoveryAwarePumpState.Faulted)
                return PlaybackRecoveryAwarePumpResult.Failure;

            _state = PlaybackRecoveryAwarePumpState.Running;
            _recoveryCount = 0;
            _videoGeneration = videoGeneration;
            _audioGeneration = audioGeneration;
            _stopRequested = false;
            if (_runGeneration < long.MaxValue) _runGeneration++;
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            run = RunCoreAsync(videoGeneration, audioGeneration, options, _runCancellation.Token);
            _activeRun = run;
        }

        var result = await run.ConfigureAwait(false);
        lock (_sync)
        {
            if (ReferenceEquals(_activeRun, run))
            {
                _activeRun = null;
                _runCancellation?.Dispose();
                _runCancellation = null;
                if (!_isDisposed) _state = ResultState(result);
            }
        }
        return result;
    }

    public async ValueTask<bool> StopAsync()
    {
        Task<PlaybackRecoveryAwarePumpResult>? run;
        lock (_sync)
        {
            if (_isDisposed) return false;
            run = _activeRun;
            if (run is null) return true;
            _stopRequested = true;
            _state = PlaybackRecoveryAwarePumpState.Stopping;
            _runCancellation?.Cancel();
        }

        try
        {
            await run.ConfigureAwait(false);
            return true;
        }
        catch
        {
            lock (_sync)
                if (!_isDisposed) _state = PlaybackRecoveryAwarePumpState.Faulted;
            return false;
        }
    }

    public async ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken = default)
    {
        if (videoGeneration < 0 || audioGeneration < 0) return false;
        if (!await StopAsync().ConfigureAwait(false)) return false;

        try
        {
            if (!await _recovery.ResetAsync(cancellationToken).ConfigureAwait(false))
                return SetFaulted();
            if (!await _pump.ResetAsync(videoGeneration, audioGeneration, cancellationToken)
                    .ConfigureAwait(false))
                return SetFaulted();

            lock (_sync)
            {
                if (_isDisposed) return false;
                _state = PlaybackRecoveryAwarePumpState.Idle;
                _recoveryCount = 0;
                _videoGeneration = videoGeneration;
                _audioGeneration = audioGeneration;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
                if (!_isDisposed) _state = PlaybackRecoveryAwarePumpState.ResetRequired;
            return false;
        }
        catch
        {
            return SetFaulted();
        }
    }

    public void Dispose()
    {
        Task<PlaybackRecoveryAwarePumpResult>? run;
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackRecoveryAwarePumpState.Disposed;
            _stopRequested = true;
            _runCancellation?.Cancel();
            run = _activeRun;
        }
        try { run?.GetAwaiter().GetResult(); }
        catch { }
        lock (_sync) _state = PlaybackRecoveryAwarePumpState.Disposed;
    }

    private async Task<PlaybackRecoveryAwarePumpResult> RunCoreAsync(
        long videoGeneration,
        long audioGeneration,
        PlaybackRecoveryAwarePumpOptions options,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            PlaybackLateAwareContinuousPumpResult pumpResult;
            try
            {
                pumpResult = await _pump.RunAsync(options.PumpOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CancellationResult(cancellationToken);
            }
            catch
            {
                return PlaybackRecoveryAwarePumpResult.Failure;
            }

            if (!Enum.IsDefined(pumpResult))
                return PlaybackRecoveryAwarePumpResult.Failure;

            if (pumpResult != PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired)
                return MapPumpResult(pumpResult, cancellationToken);

            int recoveryCount;
            lock (_sync)
            {
                recoveryCount = ++_recoveryCount;
                _state = PlaybackRecoveryAwarePumpState.Recovering;
            }
            if (recoveryCount > options.MaximumRecoveries)
                return PlaybackRecoveryAwarePumpResult.RecoveryLimitReached;

            PlaybackAudioRecoveryResult recoveryResult;
            try
            {
                recoveryResult = await _recovery.RecoverAsync(videoGeneration, audioGeneration,
                    options.RecoveryOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CancellationResult(cancellationToken);
            }
            catch
            {
                return PlaybackRecoveryAwarePumpResult.Failure;
            }

            if (!Enum.IsDefined(recoveryResult))
                return PlaybackRecoveryAwarePumpResult.Failure;

            switch (recoveryResult)
            {
                case PlaybackAudioRecoveryResult.Recovered:
                    try
                    {
                        if (!await _pump.ResetAsync(videoGeneration, audioGeneration,
                                cancellationToken).ConfigureAwait(false))
                            return PlaybackRecoveryAwarePumpResult.Failure;
                    }
                    catch (OperationCanceledException)
                    {
                        return CancellationResult(cancellationToken);
                    }
                    catch
                    {
                        return PlaybackRecoveryAwarePumpResult.Failure;
                    }
                    lock (_sync)
                        if (!_isDisposed) _state = PlaybackRecoveryAwarePumpState.Running;
                    break;
                case PlaybackAudioRecoveryResult.AttemptLimitReached:
                case PlaybackAudioRecoveryResult.ResetRequired:
                    return PlaybackRecoveryAwarePumpResult.ResetRequired;
                case PlaybackAudioRecoveryResult.InvalidConfiguration:
                    return PlaybackRecoveryAwarePumpResult.InvalidConfiguration;
                case PlaybackAudioRecoveryResult.AlreadyRunning:
                    return PlaybackRecoveryAwarePumpResult.Failure;
                case PlaybackAudioRecoveryResult.Cancelled:
                    return CancellationResult(cancellationToken);
                case PlaybackAudioRecoveryResult.Disposed:
                    return PlaybackRecoveryAwarePumpResult.Disposed;
                case PlaybackAudioRecoveryResult.Failure:
                default:
                    return PlaybackRecoveryAwarePumpResult.Failure;
            }
        }
    }

    private PlaybackRecoveryAwarePumpResult MapPumpResult(
        PlaybackLateAwareContinuousPumpResult result,
        CancellationToken cancellationToken) => result switch
        {
            PlaybackLateAwareContinuousPumpResult.Completed => PlaybackRecoveryAwarePumpResult.Completed,
            PlaybackLateAwareContinuousPumpResult.Stopped => PlaybackRecoveryAwarePumpResult.Stopped,
            PlaybackLateAwareContinuousPumpResult.IdleLimitReached => PlaybackRecoveryAwarePumpResult.IdleLimitReached,
            PlaybackLateAwareContinuousPumpResult.PumpLimitReached => PlaybackRecoveryAwarePumpResult.PumpLimitReached,
            PlaybackLateAwareContinuousPumpResult.DurationLimitReached => PlaybackRecoveryAwarePumpResult.DurationLimitReached,
            PlaybackLateAwareContinuousPumpResult.ResetRequired => PlaybackRecoveryAwarePumpResult.ResetRequired,
            PlaybackLateAwareContinuousPumpResult.DeviceLost => PlaybackRecoveryAwarePumpResult.DeviceLost,
            PlaybackLateAwareContinuousPumpResult.ResizeRequired => PlaybackRecoveryAwarePumpResult.ResizeRequired,
            PlaybackLateAwareContinuousPumpResult.InitializationRequired => PlaybackRecoveryAwarePumpResult.InitializationRequired,
            PlaybackLateAwareContinuousPumpResult.BindingRequired => PlaybackRecoveryAwarePumpResult.BindingRequired,
            PlaybackLateAwareContinuousPumpResult.InvalidConfiguration => PlaybackRecoveryAwarePumpResult.InvalidConfiguration,
            PlaybackLateAwareContinuousPumpResult.InvalidEnvironment => PlaybackRecoveryAwarePumpResult.InvalidEnvironment,
            PlaybackLateAwareContinuousPumpResult.AlreadyRunning => PlaybackRecoveryAwarePumpResult.AlreadyRunning,
            PlaybackLateAwareContinuousPumpResult.Cancelled => CancellationResult(cancellationToken),
            PlaybackLateAwareContinuousPumpResult.Disposed => PlaybackRecoveryAwarePumpResult.Disposed,
            PlaybackLateAwareContinuousPumpResult.Failure => PlaybackRecoveryAwarePumpResult.Failure,
            _ => PlaybackRecoveryAwarePumpResult.Failure,
        };

    private PlaybackRecoveryAwarePumpResult CancellationResult(CancellationToken cancellationToken)
    {
        lock (_sync)
            return _stopRequested
                ? PlaybackRecoveryAwarePumpResult.Stopped
                : cancellationToken.IsCancellationRequested
                    ? PlaybackRecoveryAwarePumpResult.Cancelled
                    : PlaybackRecoveryAwarePumpResult.Failure;
    }

    private bool SetFaulted()
    {
        lock (_sync)
            if (!_isDisposed) _state = PlaybackRecoveryAwarePumpState.Faulted;
        return false;
    }

    private static PlaybackRecoveryAwarePumpState ResultState(
        PlaybackRecoveryAwarePumpResult result) => result switch
        {
            PlaybackRecoveryAwarePumpResult.ResetRequired or
            PlaybackRecoveryAwarePumpResult.RecoveryLimitReached or
            PlaybackRecoveryAwarePumpResult.DeviceLost or
            PlaybackRecoveryAwarePumpResult.ResizeRequired or
            PlaybackRecoveryAwarePumpResult.InitializationRequired or
            PlaybackRecoveryAwarePumpResult.BindingRequired =>
                PlaybackRecoveryAwarePumpState.ResetRequired,
            PlaybackRecoveryAwarePumpResult.Failure or
            PlaybackRecoveryAwarePumpResult.Disposed =>
                PlaybackRecoveryAwarePumpState.Faulted,
            _ => PlaybackRecoveryAwarePumpState.Idle,
        };

    private static bool Validate(
        long videoGeneration,
        long audioGeneration,
        PlaybackRecoveryAwarePumpOptions? options) =>
        videoGeneration >= 0 && audioGeneration >= 0 && options is not null &&
        options.MaximumRecoveries is >= 1 and <=
            PlaybackRecoveryAwarePumpOptions.MaximumAllowedRecoveries &&
        options.PumpOptions is not null && options.RecoveryOptions is not null;
}

internal interface IPlaybackRecoveryAwarePump
{
    ValueTask<PlaybackLateAwareContinuousPumpResult> RunAsync(
        PlaybackLateAwareContinuousPumpOptions options,
        CancellationToken cancellationToken);

    ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken);
}

internal interface IPlaybackRecoveryAwareAudioRecovery
{
    ValueTask<PlaybackAudioRecoveryResult> RecoverAsync(
        long videoGeneration,
        long audioGeneration,
        PlaybackAudioRecoveryOptions options,
        CancellationToken cancellationToken);

    ValueTask<bool> ResetAsync(CancellationToken cancellationToken);
}

internal sealed class PlaybackRecoveryAwarePump(
    PlaybackLateAwareContinuousPumpBoundary pump) : IPlaybackRecoveryAwarePump
{
    public ValueTask<PlaybackLateAwareContinuousPumpResult> RunAsync(
        PlaybackLateAwareContinuousPumpOptions options,
        CancellationToken cancellationToken) => pump.RunAsync(options, cancellationToken);

    public ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken) =>
        pump.ResetAsync(videoGeneration, audioGeneration, cancellationToken);
}

internal sealed class PlaybackRecoveryAwareAudioRecovery(
    PlaybackAudioRecoveryBoundary recovery) : IPlaybackRecoveryAwareAudioRecovery
{
    public ValueTask<PlaybackAudioRecoveryResult> RecoverAsync(
        long videoGeneration,
        long audioGeneration,
        PlaybackAudioRecoveryOptions options,
        CancellationToken cancellationToken) =>
        recovery.RecoverAsync(videoGeneration, audioGeneration, options, cancellationToken);

    public ValueTask<bool> ResetAsync(CancellationToken cancellationToken) =>
        recovery.ResetAsync(cancellationToken);
}
