namespace PeerCast.Nova.Playback;

public enum PlaybackLateAwareContinuousPumpState
{
    Idle,
    Running,
    Stopping,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackLateAwareContinuousPumpResult
{
    Completed,
    Stopped,
    IdleLimitReached,
    PumpLimitReached,
    DurationLimitReached,
    AudioRecoveryRequired,
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

public sealed record PlaybackLateAwareContinuousPumpOptions
{
    public const int MaximumAllowedPumpCount = 1_000_000;
    public const int MaximumAllowedConsecutiveIdleCount = 100_000;
    public static readonly TimeSpan MaximumAllowedRunDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan MinimumIdleDelay = TimeSpan.FromMilliseconds(1);
    public static readonly TimeSpan MaximumIdleDelay = TimeSpan.FromSeconds(1);

    public int MaximumPumpCount { get; init; }
    public TimeSpan MaximumRunDuration { get; init; }
    public int MaximumConsecutiveIdleCount { get; init; }
    public TimeSpan IdleDelay { get; init; }
    public PlaybackTimedSchedulerDispatchOptions DispatchOptions { get; init; } = new();
}

public readonly record struct PlaybackLateAwareContinuousPumpSnapshot(
    PlaybackLateAwareContinuousPumpState State,
    int PumpCount,
    int ConsecutiveIdleCount,
    long VideoDropCount,
    long RunGeneration);

public sealed class PlaybackLateAwareContinuousPumpBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly IPlaybackLateAwareContinuousPumpDispatch _dispatch;
    private readonly IPlaybackLateAwareContinuousPumpClock _clock;
    private readonly IPlaybackLateAwareContinuousPumpDelay _delay;
    private CancellationTokenSource? _runCancellation;
    private Task<PlaybackLateAwareContinuousPumpResult>? _activeRun;
    private PlaybackLateAwareContinuousPumpState _state;
    private int _pumpCount;
    private int _consecutiveIdleCount;
    private long _videoDropCount;
    private long _runGeneration;
    private bool _stopRequested;
    private bool _isDisposed;

    public PlaybackLateAwareContinuousPumpBoundary(
        PlaybackLateAwareTimedDispatchBoundary dispatch)
        : this(new PlaybackLateAwareContinuousPumpDispatch(dispatch),
            new StopwatchLateAwareContinuousPumpClock(),
            new TaskLateAwareContinuousPumpDelay())
    {
    }

    internal PlaybackLateAwareContinuousPumpBoundary(
        IPlaybackLateAwareContinuousPumpDispatch dispatch,
        IPlaybackLateAwareContinuousPumpClock clock,
        IPlaybackLateAwareContinuousPumpDelay delay)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(delay);
        _dispatch = dispatch;
        _clock = clock;
        _delay = delay;
    }

    public PlaybackLateAwareContinuousPumpSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_state, _pumpCount, _consecutiveIdleCount,
                    _videoDropCount, _runGeneration);
        }
    }

    public async ValueTask<PlaybackLateAwareContinuousPumpResult> RunAsync(
        PlaybackLateAwareContinuousPumpOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateOptions(options))
            return PlaybackLateAwareContinuousPumpResult.InvalidConfiguration;
        Task<PlaybackLateAwareContinuousPumpResult> run;
        lock (_sync)
        {
            if (_isDisposed) return PlaybackLateAwareContinuousPumpResult.Disposed;
            if (_activeRun is not null || _state == PlaybackLateAwareContinuousPumpState.Stopping)
                return PlaybackLateAwareContinuousPumpResult.AlreadyRunning;
            if (_state == PlaybackLateAwareContinuousPumpState.ResetRequired)
                return PlaybackLateAwareContinuousPumpResult.ResetRequired;
            if (_state == PlaybackLateAwareContinuousPumpState.Faulted)
                return PlaybackLateAwareContinuousPumpResult.Failure;
            _state = PlaybackLateAwareContinuousPumpState.Running;
            _pumpCount = 0;
            _consecutiveIdleCount = 0;
            _videoDropCount = 0;
            _stopRequested = false;
            if (_runGeneration < long.MaxValue) _runGeneration++;
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            run = RunCoreAsync(options, _runCancellation.Token);
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
                if (!_isDisposed)
                    _state = ResultState(result);
            }
        }
        return result;
    }

    public async ValueTask<bool> StopAsync()
    {
        Task<PlaybackLateAwareContinuousPumpResult>? run;
        lock (_sync)
        {
            if (_isDisposed) return false;
            run = _activeRun;
            if (run is null) return true;
            _stopRequested = true;
            _state = PlaybackLateAwareContinuousPumpState.Stopping;
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
                if (!_isDisposed) _state = PlaybackLateAwareContinuousPumpState.Faulted;
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
            var reset = await _dispatch.ResetAsync(videoGeneration, audioGeneration,
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (_isDisposed) return false;
                _state = reset
                    ? PlaybackLateAwareContinuousPumpState.Idle
                    : PlaybackLateAwareContinuousPumpState.Faulted;
                if (reset)
                {
                    _pumpCount = 0;
                    _consecutiveIdleCount = 0;
                    _videoDropCount = 0;
                }
            }
            return reset;
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
                if (!_isDisposed) _state = PlaybackLateAwareContinuousPumpState.ResetRequired;
            return false;
        }
        catch
        {
            lock (_sync)
                if (!_isDisposed) _state = PlaybackLateAwareContinuousPumpState.Faulted;
            return false;
        }
    }

    public void Dispose()
    {
        Task<PlaybackLateAwareContinuousPumpResult>? run;
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackLateAwareContinuousPumpState.Disposed;
            _stopRequested = true;
            _runCancellation?.Cancel();
            run = _activeRun;
        }
        try { run?.GetAwaiter().GetResult(); }
        catch { }
        lock (_sync) _state = PlaybackLateAwareContinuousPumpState.Disposed;
    }

    private async Task<PlaybackLateAwareContinuousPumpResult> RunCoreAsync(
        PlaybackLateAwareContinuousPumpOptions options,
        CancellationToken cancellationToken)
    {
        TimeSpan started;
        try { started = _clock.GetTimestamp(); }
        catch { return PlaybackLateAwareContinuousPumpResult.Failure; }
        if (started < TimeSpan.Zero) return PlaybackLateAwareContinuousPumpResult.Failure;

        while (true)
        {
            TimeSpan now;
            try { now = _clock.GetTimestamp(); }
            catch { return PlaybackLateAwareContinuousPumpResult.Failure; }
            if (now < started) return PlaybackLateAwareContinuousPumpResult.Failure;
            TimeSpan duration;
            try { duration = TimeSpan.FromTicks(checked(now.Ticks - started.Ticks)); }
            catch { return PlaybackLateAwareContinuousPumpResult.Failure; }
            if (duration >= options.MaximumRunDuration)
                return PlaybackLateAwareContinuousPumpResult.DurationLimitReached;
            int count;
            lock (_sync) count = _pumpCount;
            if (count >= options.MaximumPumpCount)
                return PlaybackLateAwareContinuousPumpResult.PumpLimitReached;

            PlaybackLateAwareTimedDispatchResult result;
            try
            {
                result = await _dispatch.DispatchOneAsync(options.DispatchOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CancellationResult(cancellationToken);
            }
            catch
            {
                return PlaybackLateAwareContinuousPumpResult.Failure;
            }
            lock (_sync) _pumpCount++;
            if (!Enum.IsDefined(result))
                return PlaybackLateAwareContinuousPumpResult.Failure;

            switch (result)
            {
                case PlaybackLateAwareTimedDispatchResult.VideoProcessed:
                case PlaybackLateAwareTimedDispatchResult.AudioProcessed:
                    lock (_sync) _consecutiveIdleCount = 0;
                    break;
                case PlaybackLateAwareTimedDispatchResult.VideoDropConsumed:
                    lock (_sync)
                    {
                        _consecutiveIdleCount = 0;
                        if (_videoDropCount < long.MaxValue) _videoDropCount++;
                    }
                    break;
                case PlaybackLateAwareTimedDispatchResult.RetainedVideoLate:
                case PlaybackLateAwareTimedDispatchResult.RetainedAudioLate:
                case PlaybackLateAwareTimedDispatchResult.WaitLimitReached:
                case PlaybackLateAwareTimedDispatchResult.Empty:
                case PlaybackLateAwareTimedDispatchResult.WouldBlock:
                case PlaybackLateAwareTimedDispatchResult.ClockNotStarted:
                    if (await DelayIdleAsync(options, cancellationToken).ConfigureAwait(false))
                        break;
                    return cancellationToken.IsCancellationRequested
                        ? CancellationResult(cancellationToken)
                        : PlaybackLateAwareContinuousPumpResult.IdleLimitReached;
                case PlaybackLateAwareTimedDispatchResult.AudioRecoveryRequired:
                    return PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired;
                case PlaybackLateAwareTimedDispatchResult.ResetRequired:
                    return PlaybackLateAwareContinuousPumpResult.ResetRequired;
                case PlaybackLateAwareTimedDispatchResult.DeviceLost:
                    return PlaybackLateAwareContinuousPumpResult.DeviceLost;
                case PlaybackLateAwareTimedDispatchResult.ResizeRequired:
                    return PlaybackLateAwareContinuousPumpResult.ResizeRequired;
                case PlaybackLateAwareTimedDispatchResult.InitializationRequired:
                    return PlaybackLateAwareContinuousPumpResult.InitializationRequired;
                case PlaybackLateAwareTimedDispatchResult.BindingRequired:
                    return PlaybackLateAwareContinuousPumpResult.BindingRequired;
                case PlaybackLateAwareTimedDispatchResult.InvalidEnvironment:
                    return PlaybackLateAwareContinuousPumpResult.InvalidEnvironment;
                case PlaybackLateAwareTimedDispatchResult.Cancelled:
                    return CancellationResult(cancellationToken);
                case PlaybackLateAwareTimedDispatchResult.Disposed:
                    return PlaybackLateAwareContinuousPumpResult.Disposed;
                default:
                    return PlaybackLateAwareContinuousPumpResult.Failure;
            }
        }
    }

    private async ValueTask<bool> DelayIdleAsync(
        PlaybackLateAwareContinuousPumpOptions options,
        CancellationToken cancellationToken)
    {
        int idle;
        lock (_sync) idle = ++_consecutiveIdleCount;
        if (idle >= options.MaximumConsecutiveIdleCount) return false;
        try
        {
            await _delay.DelayAsync(options.IdleDelay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private PlaybackLateAwareContinuousPumpResult CancellationResult(
        CancellationToken cancellationToken)
    {
        lock (_sync)
            return _stopRequested
                ? PlaybackLateAwareContinuousPumpResult.Stopped
                : cancellationToken.IsCancellationRequested
                    ? PlaybackLateAwareContinuousPumpResult.Cancelled
                    : PlaybackLateAwareContinuousPumpResult.Failure;
    }

    private static PlaybackLateAwareContinuousPumpState ResultState(
        PlaybackLateAwareContinuousPumpResult result) => result switch
        {
            PlaybackLateAwareContinuousPumpResult.ResetRequired or
            PlaybackLateAwareContinuousPumpResult.DeviceLost or
            PlaybackLateAwareContinuousPumpResult.ResizeRequired or
            PlaybackLateAwareContinuousPumpResult.InitializationRequired or
            PlaybackLateAwareContinuousPumpResult.BindingRequired or
            PlaybackLateAwareContinuousPumpResult.AudioRecoveryRequired =>
                PlaybackLateAwareContinuousPumpState.ResetRequired,
            PlaybackLateAwareContinuousPumpResult.Failure or
            PlaybackLateAwareContinuousPumpResult.Disposed =>
                PlaybackLateAwareContinuousPumpState.Faulted,
            _ => PlaybackLateAwareContinuousPumpState.Idle,
        };

    private static bool ValidateOptions(PlaybackLateAwareContinuousPumpOptions? value) =>
        value is not null &&
        value.MaximumPumpCount is >= 1 and <=
            PlaybackLateAwareContinuousPumpOptions.MaximumAllowedPumpCount &&
        value.MaximumConsecutiveIdleCount is >= 1 and <=
            PlaybackLateAwareContinuousPumpOptions.MaximumAllowedConsecutiveIdleCount &&
        value.MaximumRunDuration > TimeSpan.Zero &&
        value.MaximumRunDuration <= PlaybackLateAwareContinuousPumpOptions.MaximumAllowedRunDuration &&
        value.IdleDelay >= PlaybackLateAwareContinuousPumpOptions.MinimumIdleDelay &&
        value.IdleDelay <= PlaybackLateAwareContinuousPumpOptions.MaximumIdleDelay &&
        value.DispatchOptions is not null;
}

internal interface IPlaybackLateAwareContinuousPumpDispatch
{
    ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
        PlaybackTimedSchedulerDispatchOptions options,
        CancellationToken cancellationToken);
    ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken);
}

internal interface IPlaybackLateAwareContinuousPumpClock
{
    TimeSpan GetTimestamp();
}

internal interface IPlaybackLateAwareContinuousPumpDelay
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class PlaybackLateAwareContinuousPumpDispatch(
    PlaybackLateAwareTimedDispatchBoundary dispatch) :
    IPlaybackLateAwareContinuousPumpDispatch
{
    public ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
        PlaybackTimedSchedulerDispatchOptions options,
        CancellationToken cancellationToken) =>
        dispatch.DispatchOneAsync(options, cancellationToken);

    public ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken) =>
        dispatch.ResetAsync(videoGeneration, audioGeneration, cancellationToken);
}

internal sealed class StopwatchLateAwareContinuousPumpClock :
    IPlaybackLateAwareContinuousPumpClock
{
    private readonly long _origin = System.Diagnostics.Stopwatch.GetTimestamp();
    public TimeSpan GetTimestamp() =>
        System.Diagnostics.Stopwatch.GetElapsedTime(_origin,
            System.Diagnostics.Stopwatch.GetTimestamp());
}

internal sealed class TaskLateAwareContinuousPumpDelay :
    IPlaybackLateAwareContinuousPumpDelay
{
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}
