using System.Diagnostics;

namespace PeerCast.Nova.Playback;

public enum FiniteContinuousPlaybackPumpState
{
    Idle,
    Running,
    Stopping,
    Resetting,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum FiniteContinuousPlaybackPumpResult
{
    Completed,
    Stopped,
    IdleLimitReached,
    PumpLimitReached,
    DurationLimitReached,
    AlreadyRunning,
    ResetInProgress,
    ResetRequired,
    DeviceLost,
    ResizeRequired,
    InvalidConfiguration,
    InvalidEnvironment,
    Cancelled,
    Disposed,
    Failure,
}

public enum FiniteContinuousPlaybackPumpStopResult
{
    Stopped,
    NotRunning,
    Disposed,
    Failure,
    ResetInProgress,
}

public enum FiniteContinuousPlaybackPumpResetResult
{
    Reset,
    ResetRequired,
    Cancelled,
    Disposed,
    Failure,
    AlreadyResetting,
}

public sealed record FiniteContinuousPlaybackPumpOptions
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
}

public readonly record struct FiniteContinuousPlaybackPumpSnapshot(
    FiniteContinuousPlaybackPumpState State,
    int PumpCount,
    int ConsecutiveIdleCount,
    long RunGeneration);

public sealed class FiniteContinuousPlaybackPumpPolicyBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly IFiniteContinuousPlaybackPumpAdapter _adapter;
    private readonly IFiniteContinuousPlaybackPumpClock _clock;
    private readonly IFiniteContinuousPlaybackPumpDelay _delay;
    private readonly Func<bool> _isSupportedPlatform;
    private CancellationTokenSource? _runCancellation;
    private Task<FiniteContinuousPlaybackPumpResult>? _activeRun;
    private Task<FiniteContinuousPlaybackPumpResetResult>? _activeReset;
    private CancellationTokenSource? _resetCancellation;
    private FiniteContinuousPlaybackPumpState _state;
    private int _pumpCount;
    private int _consecutiveIdleCount;
    private long _runGeneration;
    private bool _stopRequested;
    private bool _isDisposed;

    public FiniteContinuousPlaybackPumpPolicyBoundary(
        PlaybackWindowsOutputSchedulerAdapterBoundary adapter)
        : this(new FiniteContinuousPlaybackPumpAdapter(adapter),
            new StopwatchContinuousPlaybackPumpClock(),
            new TaskContinuousPlaybackPumpDelay(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal FiniteContinuousPlaybackPumpPolicyBoundary(
        IFiniteContinuousPlaybackPumpAdapter adapter,
        IFiniteContinuousPlaybackPumpClock clock,
        IFiniteContinuousPlaybackPumpDelay delay,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _adapter = adapter;
        _clock = clock;
        _delay = delay;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public FiniteContinuousPlaybackPumpSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_state, _pumpCount, _consecutiveIdleCount, _runGeneration);
        }
    }

    public async ValueTask<FiniteContinuousPlaybackPumpResult> RunAsync(
        FiniteContinuousPlaybackPumpOptions options,
        CancellationToken cancellationToken)
    {
        if (!IsValid(options)) return FiniteContinuousPlaybackPumpResult.InvalidConfiguration;
        Task<FiniteContinuousPlaybackPumpResult> run;
        lock (_sync)
        {
            if (_isDisposed) return FiniteContinuousPlaybackPumpResult.Disposed;
            if (_state == FiniteContinuousPlaybackPumpState.Resetting)
                return FiniteContinuousPlaybackPumpResult.ResetInProgress;
            if (_activeRun is not null || _state == FiniteContinuousPlaybackPumpState.Stopping)
                return FiniteContinuousPlaybackPumpResult.AlreadyRunning;
            if (_state == FiniteContinuousPlaybackPumpState.ResetRequired)
                return FiniteContinuousPlaybackPumpResult.ResetRequired;
            if (_state == FiniteContinuousPlaybackPumpState.Faulted)
                return FiniteContinuousPlaybackPumpResult.Failure;
            if (!_isSupportedPlatform())
                return FiniteContinuousPlaybackPumpResult.InvalidEnvironment;
            _state = FiniteContinuousPlaybackPumpState.Running;
            _pumpCount = 0;
            _consecutiveIdleCount = 0;
            _stopRequested = false;
            IncrementSaturated(ref _runGeneration);
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
                if (!_isDisposed && _state != FiniteContinuousPlaybackPumpState.Resetting)
                    _state = ResultState(result);
            }
        }
        return result;
    }

    public async ValueTask<FiniteContinuousPlaybackPumpStopResult> StopAsync()
    {
        Task<FiniteContinuousPlaybackPumpResult>? run;
        lock (_sync)
        {
            if (_isDisposed) return FiniteContinuousPlaybackPumpStopResult.Disposed;
            if (_state == FiniteContinuousPlaybackPumpState.Resetting)
                return FiniteContinuousPlaybackPumpStopResult.ResetInProgress;
            run = _activeRun;
            if (run is null) return FiniteContinuousPlaybackPumpStopResult.NotRunning;
            _stopRequested = true;
            _state = FiniteContinuousPlaybackPumpState.Stopping;
            _runCancellation?.Cancel();
        }
        try
        {
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
            return FiniteContinuousPlaybackPumpStopResult.Stopped;
        }
        catch
        {
            return FiniteContinuousPlaybackPumpStopResult.Failure;
        }
    }

    public async ValueTask<FiniteContinuousPlaybackPumpResetResult> ResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken)
    {
        if (videoGeneration < 0 || audioGeneration < 0)
            return FiniteContinuousPlaybackPumpResetResult.Failure;
        TaskCompletionSource<FiniteContinuousPlaybackPumpResetResult> completion;
        lock (_sync)
        {
            if (_isDisposed) return FiniteContinuousPlaybackPumpResetResult.Disposed;
            if (_activeReset is not null)
                return FiniteContinuousPlaybackPumpResetResult.AlreadyResetting;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeReset = completion.Task;
            _resetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _state = FiniteContinuousPlaybackPumpState.Resetting;
        }

        FiniteContinuousPlaybackPumpResetResult final;
        try
        {
            await StopRunForResetAsync().ConfigureAwait(false);
            var result = await InvokeResetAsync(videoGeneration, audioGeneration,
                _resetCancellation.Token).ConfigureAwait(false);
            lock (_sync)
            {
                final = _isDisposed
                    ? FiniteContinuousPlaybackPumpResetResult.Disposed
                    : CommitReset(result);
            }
        }
        catch
        {
            lock (_sync)
            {
                final = _isDisposed
                    ? FiniteContinuousPlaybackPumpResetResult.Disposed
                    : SetResetState(FiniteContinuousPlaybackPumpState.Faulted,
                        FiniteContinuousPlaybackPumpResetResult.Failure);
            }
        }
        lock (_sync)
        {
            _resetCancellation?.Dispose();
            _resetCancellation = null;
            _activeReset = null;
        }
        completion.TrySetResult(final);
        return final;
    }

    public void Dispose()
    {
        Task<FiniteContinuousPlaybackPumpResult>? run;
        Task<FiniteContinuousPlaybackPumpResetResult>? reset;
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = FiniteContinuousPlaybackPumpState.Disposed;
            _stopRequested = true;
            _runCancellation?.Cancel();
            _resetCancellation?.Cancel();
            run = _activeRun;
            reset = _activeReset;
        }
        try { reset?.GetAwaiter().GetResult(); }
        catch { }
        try { run?.GetAwaiter().GetResult(); }
        catch { }
        lock (_sync) _state = FiniteContinuousPlaybackPumpState.Disposed;
    }

    private async ValueTask StopRunForResetAsync()
    {
        Task<FiniteContinuousPlaybackPumpResult>? run;
        lock (_sync)
        {
            run = _activeRun;
            if (run is null) return;
            _stopRequested = true;
            _runCancellation?.Cancel();
        }
        try { await run.ConfigureAwait(false); }
        catch { }
        lock (_sync)
        {
            if (ReferenceEquals(_activeRun, run))
            {
                _activeRun = null;
                _runCancellation?.Dispose();
                _runCancellation = null;
            }
        }
    }

    private async ValueTask<PlaybackWindowsOutputSchedulerAdapterResetResult> InvokeResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken)
    {
        try
        {
            return await _adapter.ResetAsync(videoGeneration, audioGeneration,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackWindowsOutputSchedulerAdapterResetResult.Cancelled;
        }
        catch
        {
            return PlaybackWindowsOutputSchedulerAdapterResetResult.Failure;
        }
    }

    private FiniteContinuousPlaybackPumpResetResult CommitReset(
        PlaybackWindowsOutputSchedulerAdapterResetResult result) => result switch
        {
            PlaybackWindowsOutputSchedulerAdapterResetResult.Reset => SetResetState(
                FiniteContinuousPlaybackPumpState.Idle,
                FiniteContinuousPlaybackPumpResetResult.Reset),
            PlaybackWindowsOutputSchedulerAdapterResetResult.PartialResetFault => SetResetState(
                FiniteContinuousPlaybackPumpState.ResetRequired,
                FiniteContinuousPlaybackPumpResetResult.ResetRequired),
            PlaybackWindowsOutputSchedulerAdapterResetResult.Cancelled => SetResetState(
                FiniteContinuousPlaybackPumpState.ResetRequired,
                FiniteContinuousPlaybackPumpResetResult.Cancelled),
            PlaybackWindowsOutputSchedulerAdapterResetResult.Disposed => SetResetState(
                FiniteContinuousPlaybackPumpState.Faulted,
                FiniteContinuousPlaybackPumpResetResult.Disposed),
            _ => SetResetState(FiniteContinuousPlaybackPumpState.Faulted,
                FiniteContinuousPlaybackPumpResetResult.Failure),
        };

    private async Task<FiniteContinuousPlaybackPumpResult> RunCoreAsync(
        FiniteContinuousPlaybackPumpOptions options, CancellationToken cancellationToken)
    {
        TimeSpan started;
        try { started = _clock.GetTimestamp(); }
        catch { return FiniteContinuousPlaybackPumpResult.Failure; }
        if (started < TimeSpan.Zero) return FiniteContinuousPlaybackPumpResult.Failure;

        while (true)
        {
            var duration = ReadDuration(started);
            if (duration is null) return FiniteContinuousPlaybackPumpResult.Failure;
            if (duration >= options.MaximumRunDuration)
                return FiniteContinuousPlaybackPumpResult.DurationLimitReached;
            int count;
            lock (_sync) count = _pumpCount;
            if (count >= options.MaximumPumpCount)
                return FiniteContinuousPlaybackPumpResult.PumpLimitReached;

            PlaybackWindowsOutputSchedulerAdapterResult result;
            try
            {
                result = await _adapter.PumpOneAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CancellationResult(cancellationToken);
            }
            catch
            {
                return FiniteContinuousPlaybackPumpResult.Failure;
            }
            lock (_sync) _pumpCount++;
            if (!Enum.IsDefined(result)) return FiniteContinuousPlaybackPumpResult.Failure;
            switch (result)
            {
                case PlaybackWindowsOutputSchedulerAdapterResult.VideoProcessed:
                case PlaybackWindowsOutputSchedulerAdapterResult.AudioProcessed:
                    lock (_sync) _consecutiveIdleCount = 0;
                    break;
                case PlaybackWindowsOutputSchedulerAdapterResult.Empty:
                case PlaybackWindowsOutputSchedulerAdapterResult.WouldBlock:
                    int idle;
                    lock (_sync) idle = ++_consecutiveIdleCount;
                    if (idle >= options.MaximumConsecutiveIdleCount)
                        return FiniteContinuousPlaybackPumpResult.IdleLimitReached;
                    try
                    {
                        await _delay.DelayAsync(options.IdleDelay, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return CancellationResult(cancellationToken);
                    }
                    break;
                case PlaybackWindowsOutputSchedulerAdapterResult.DeviceLost:
                    return FiniteContinuousPlaybackPumpResult.DeviceLost;
                case PlaybackWindowsOutputSchedulerAdapterResult.ResizeRequired:
                    return FiniteContinuousPlaybackPumpResult.ResizeRequired;
                case PlaybackWindowsOutputSchedulerAdapterResult.InitializationRequired:
                case PlaybackWindowsOutputSchedulerAdapterResult.BindingRequired:
                case PlaybackWindowsOutputSchedulerAdapterResult.ResetRequired:
                    return FiniteContinuousPlaybackPumpResult.ResetRequired;
                case PlaybackWindowsOutputSchedulerAdapterResult.InvalidEnvironment:
                    return FiniteContinuousPlaybackPumpResult.InvalidEnvironment;
                case PlaybackWindowsOutputSchedulerAdapterResult.Cancelled:
                    return CancellationResult(cancellationToken);
                case PlaybackWindowsOutputSchedulerAdapterResult.Disposed:
                    return FiniteContinuousPlaybackPumpResult.Disposed;
                default:
                    return FiniteContinuousPlaybackPumpResult.Failure;
            }
        }
    }

    private TimeSpan? ReadDuration(TimeSpan started)
    {
        try
        {
            var now = _clock.GetTimestamp();
            if (now < started) return null;
            return TimeSpan.FromTicks(checked(now.Ticks - started.Ticks));
        }
        catch { return null; }
    }

    private FiniteContinuousPlaybackPumpResult CancellationResult(
        CancellationToken cancellationToken)
    {
        lock (_sync)
            return _stopRequested
                ? FiniteContinuousPlaybackPumpResult.Stopped
                : cancellationToken.IsCancellationRequested
                    ? FiniteContinuousPlaybackPumpResult.Cancelled
                    : FiniteContinuousPlaybackPumpResult.Failure;
    }

    private FiniteContinuousPlaybackPumpResetResult SetResetState(
        FiniteContinuousPlaybackPumpState state,
        FiniteContinuousPlaybackPumpResetResult result)
    {
        _state = state;
        if (state == FiniteContinuousPlaybackPumpState.Idle)
        {
            _pumpCount = 0;
            _consecutiveIdleCount = 0;
        }
        return result;
    }

    private static FiniteContinuousPlaybackPumpState ResultState(
        FiniteContinuousPlaybackPumpResult result) => result switch
        {
            FiniteContinuousPlaybackPumpResult.ResetRequired or
            FiniteContinuousPlaybackPumpResult.DeviceLost or
            FiniteContinuousPlaybackPumpResult.ResizeRequired =>
                FiniteContinuousPlaybackPumpState.ResetRequired,
            FiniteContinuousPlaybackPumpResult.Failure => FiniteContinuousPlaybackPumpState.Faulted,
            FiniteContinuousPlaybackPumpResult.Disposed => FiniteContinuousPlaybackPumpState.Faulted,
            _ => FiniteContinuousPlaybackPumpState.Idle,
        };

    private static bool IsValid(FiniteContinuousPlaybackPumpOptions? value) =>
        value is not null && value.MaximumPumpCount is >= 1 and <=
            FiniteContinuousPlaybackPumpOptions.MaximumAllowedPumpCount &&
        value.MaximumConsecutiveIdleCount is >= 1 and <=
            FiniteContinuousPlaybackPumpOptions.MaximumAllowedConsecutiveIdleCount &&
        value.MaximumRunDuration > TimeSpan.Zero &&
        value.MaximumRunDuration <= FiniteContinuousPlaybackPumpOptions.MaximumAllowedRunDuration &&
        value.IdleDelay >= FiniteContinuousPlaybackPumpOptions.MinimumIdleDelay &&
        value.IdleDelay <= FiniteContinuousPlaybackPumpOptions.MaximumIdleDelay;

    private static void IncrementSaturated(ref long value)
    {
        if (value < long.MaxValue) value++;
    }
}

internal interface IFiniteContinuousPlaybackPumpAdapter
{
    ValueTask<PlaybackWindowsOutputSchedulerAdapterResult> PumpOneAsync(
        CancellationToken cancellationToken);
    ValueTask<PlaybackWindowsOutputSchedulerAdapterResetResult> ResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken);
}

internal interface IFiniteContinuousPlaybackPumpClock
{
    TimeSpan GetTimestamp();
}

internal interface IFiniteContinuousPlaybackPumpDelay
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class FiniteContinuousPlaybackPumpAdapter(
    PlaybackWindowsOutputSchedulerAdapterBoundary adapter) :
    IFiniteContinuousPlaybackPumpAdapter
{
    public ValueTask<PlaybackWindowsOutputSchedulerAdapterResult> PumpOneAsync(
        CancellationToken cancellationToken) => adapter.PumpOneAsync(cancellationToken);
    public ValueTask<PlaybackWindowsOutputSchedulerAdapterResetResult> ResetAsync(
        long videoGeneration, long audioGeneration, CancellationToken cancellationToken) =>
        adapter.ResetAsync(videoGeneration, audioGeneration, cancellationToken);
}

internal sealed class StopwatchContinuousPlaybackPumpClock :
    IFiniteContinuousPlaybackPumpClock
{
    public TimeSpan GetTimestamp() => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
}

internal sealed class TaskContinuousPlaybackPumpDelay :
    IFiniteContinuousPlaybackPumpDelay
{
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}
