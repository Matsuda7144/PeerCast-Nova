namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
{
    Created,
    MissingDelivery,
    DeliveryUnavailable,
    TimingUnavailable,
    MissingOutputReconfiguration,
    MissingOutputSink,
    OutputUnavailable,
    OutputMismatch,
    InvalidOptions,
}

public enum PlaybackIsoBmffRegularMovieTimedDeliveryDriverState
{
    Accepting,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
{
    None,
    VideoDelivered,
    AudioDelivered,
    VideoDropped,
    Empty,
    AudioRecoveryRequired,
    ClockNotStarted,
    ResetRequired,
    Cancelled,
    AlreadyRunning,
    WaitLimitReached,
    DeliveryFaulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
{
    Reset,
    NotRequired,
    InvalidGeneration,
    OutputUnavailable,
    OutputRejected,
    TimingRejected,
    Cancelled,
    AlreadyRunning,
    RollbackFaulted,
    Faulted,
    Disposed,
}

public sealed record PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions
{
    public const int MaximumDecisionCountLimit = 16;

    public static readonly TimeSpan MaximumTotalWaitLimit =
        TimeSpan.FromSeconds(2);

    public static readonly TimeSpan MaximumSingleDelayLimit =
        TimeSpan.FromSeconds(1);

    public int MaximumDecisionCount { get; init; } = 4;

    public TimeSpan MaximumTotalWait { get; init; } =
        TimeSpan.FromMilliseconds(500);

    public TimeSpan MinimumDelay { get; init; } =
        TimeSpan.FromMilliseconds(1);

    public TimeSpan MaximumSingleDelay { get; init; } =
        TimeSpan.FromMilliseconds(250);
}

public sealed record PlaybackIsoBmffRegularMovieTimedDeliveryDriverSnapshot(
    PlaybackIsoBmffRegularMovieTimedDeliveryDriverState State,
    PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult Result,
    int DecisionCount,
    int WaitCount,
    TimeSpan TotalWait,
    bool HasActiveDrive,
    bool HasActiveReset,
    bool HasCoordinatedReset,
    long ResetCount,
    PlaybackIsoBmffRegularMovieSchedulerDeliverySnapshot Delivery);

internal interface IPlaybackIsoBmffRegularMovieTimedDeliveryDelay
{
    ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackIsoBmffRegularMovieTimedDeliveryDelay :
    IPlaybackIsoBmffRegularMovieTimedDeliveryDelay
{
    public async ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }
}

internal interface IPlaybackIsoBmffRegularMovieTimedDeliveryResetOperations :
    IDisposable
{
    ValueTask<
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult>
        ReconfigureOutputAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken);

    bool CommitSink(long videoGeneration, long audioGeneration);
}

internal sealed class PlaybackIsoBmffRegularMovieTimedDeliveryResetOperations(
    PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration reconfiguration,
    PlaybackIsoBmffRegularMovieWindowsOutputSink sink) :
    IPlaybackIsoBmffRegularMovieTimedDeliveryResetOperations
{
    public ValueTask<
        PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult>
        ReconfigureOutputAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken) =>
        reconfiguration.ReconfigureAsync(
            videoGeneration,
            audioGeneration,
            cancellationToken);

    public bool CommitSink(long videoGeneration, long audioGeneration) =>
        sink.CommitReconfiguration(videoGeneration, audioGeneration);

    public void Dispose()
    {
        try
        {
            sink.Dispose();
        }
        finally
        {
            reconfiguration.Dispose();
        }
    }
}

public sealed class PlaybackIsoBmffRegularMovieTimedDeliveryDriver :
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _driveGate = new(1, 1);
    private readonly PlaybackIsoBmffRegularMovieSchedulerDelivery _delivery;
    private readonly IPlaybackIsoBmffRegularMovieTimedDeliveryDelay _delay;
    private readonly PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions
        _options;
    private readonly
        IPlaybackIsoBmffRegularMovieTimedDeliveryResetOperations?
        _resetOperations;
    private PlaybackIsoBmffRegularMovieTimedDeliveryDriverState _state =
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Accepting;
    private PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult _result =
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.None;
    private PlaybackIsoBmffRegularMovieSchedulerDeliverySnapshot
        _lastDeliverySnapshot;
    private CancellationTokenSource? _activeCancellation;
    private int _decisionCount;
    private int _waitCount;
    private TimeSpan _totalWait;
    private bool _hasActiveDrive;
    private bool _hasActiveReset;
    private long _resetCount;
    private int _ownedDeliveryDisposed;

    internal PlaybackIsoBmffRegularMovieTimedDeliveryDriver(
        PlaybackIsoBmffRegularMovieSchedulerDelivery delivery,
        IPlaybackIsoBmffRegularMovieTimedDeliveryDelay delay,
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions options,
        IPlaybackIsoBmffRegularMovieTimedDeliveryResetOperations?
            resetOperations = null)
    {
        _delivery = delivery;
        _delay = delay;
        _options = options;
        _resetOperations = resetOperations;
        _lastDeliverySnapshot = delivery.Snapshot;
    }

    public PlaybackIsoBmffRegularMovieTimedDeliveryDriverSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _decisionCount,
                    _waitCount,
                    _totalWait,
                    _hasActiveDrive,
                    _hasActiveReset,
                    _resetOperations is not null,
                    _resetCount,
                    _lastDeliverySnapshot);
            }
        }
    }

    public PlaybackIsoBmffRegularMovieSchedulerIngressResult Transfer(
        PlaybackIsoBmffRegularMovieNativeDecodeProgressAdvance? advance,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed)
            {
                advance?.Frame?.Dispose();
                return PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .Disposed;
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Faulted)
            {
                advance?.Frame?.Dispose();
                return PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .Faulted;
            }

            if (_hasActiveDrive || _hasActiveReset)
            {
                return PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .Cancelled;
            }

            var result = _delivery.Transfer(advance, cancellationToken);
            _lastDeliverySnapshot = _delivery.Snapshot;
            return result;
        }
    }

    public async ValueTask<
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult> DriveOneAsync(
            CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed)
            {
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                        .Disposed);
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Faulted)
            {
                return _result;
            }
        }

        bool entered;
        try
        {
            entered = await _driveGate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .Cancelled);
        }

        if (!entered)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .AlreadyRunning);
        }

        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed)
            {
                _driveGate.Release();
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                        .Disposed);
            }

            operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _activeCancellation = operation;
            _hasActiveDrive = true;
            _decisionCount = 0;
            _waitCount = 0;
            _totalWait = TimeSpan.Zero;
        }

        try
        {
            while (true)
            {
                var decisionNumber = IncrementDecisionCount();
                if (decisionNumber > _options.MaximumDecisionCount)
                {
                    return await FaultAsync(
                        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                            .WaitLimitReached).ConfigureAwait(false);
                }

                var delivered = await _delivery.DeliverOneAsync(
                    operation.Token).ConfigureAwait(false);
                var deliverySnapshot = _delivery.Snapshot;
                lock (_sync)
                {
                    _lastDeliverySnapshot = deliverySnapshot;
                }

                if (delivered !=
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                        .RetainedWait)
                {
                    return await MapDeliveryResultAsync(delivered)
                        .ConfigureAwait(false);
                }

                var timingDecision = deliverySnapshot.LastTimingDecision;
                if (timingDecision.Result !=
                        PlaybackAvTimingDecisionResult.Wait ||
                    timingDecision.WaitDuration < _options.MinimumDelay ||
                    timingDecision.WaitDuration >
                        _options.MaximumSingleDelay ||
                    decisionNumber == _options.MaximumDecisionCount)
                {
                    return await FaultAsync(
                        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                            .WaitLimitReached).ConfigureAwait(false);
                }

                TimeSpan nextTotal;
                try
                {
                    nextTotal = _totalWait + timingDecision.WaitDuration;
                }
                catch (OverflowException)
                {
                    return await FaultAsync(
                        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                            .DeliveryFaulted).ConfigureAwait(false);
                }

                if (nextTotal > _options.MaximumTotalWait)
                {
                    return await FaultAsync(
                        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                            .WaitLimitReached).ConfigureAwait(false);
                }

                lock (_sync)
                {
                    _waitCount = checked(_waitCount + 1);
                    _totalWait = nextTotal;
                }

                await _delay.DelayAsync(
                    timingDecision.WaitDuration,
                    operation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (
            operation.IsCancellationRequested)
        {
            return CancellationResult();
        }
        catch
        {
            return await FaultAsync(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .DeliveryFaulted).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeCancellation, operation))
                {
                    _activeCancellation = null;
                }

                _hasActiveDrive = false;
            }

            operation.Dispose();
            _driveGate.Release();
        }
    }

    public async ValueTask<
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult> ResetAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed)
            {
                return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                    .Disposed;
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Faulted)
            {
                return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                    .Faulted;
            }
        }

        if (videoGeneration < 0 || audioGeneration < 0)
        {
            return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                .InvalidGeneration;
        }

        if (_resetOperations is null)
        {
            return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                .OutputUnavailable;
        }

        bool entered;
        try
        {
            entered = await _driveGate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                .Cancelled;
        }

        if (!entered)
        {
            return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                .AlreadyRunning;
        }

        CancellationTokenSource operation;
        long oldVideoGeneration;
        long oldAudioGeneration;
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed)
            {
                _driveGate.Release();
                return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                    .Disposed;
            }

            var delivery = _delivery.Snapshot;
            if (delivery.State !=
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState
                    .ResetRequired)
            {
                _driveGate.Release();
                return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                    .NotRequired;
            }

            oldVideoGeneration = delivery.VideoGeneration;
            oldAudioGeneration = delivery.AudioGeneration;
            operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _activeCancellation = operation;
            _hasActiveReset = true;
        }

        try
        {
            var output = await _resetOperations.ReconfigureOutputAsync(
                videoGeneration,
                audioGeneration,
                operation.Token).ConfigureAwait(false);
            if (output !=
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                    .Reconfigured)
            {
                return output switch
                {
                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                            .Cancelled => ResetCancellationResult(),
                    PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                            .ReconfigurationRejected =>
                        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                            .OutputRejected,
                    _ => await ResetFaultAsync().ConfigureAwait(false),
                };
            }

            if (!_resetOperations.CommitSink(
                    videoGeneration,
                    audioGeneration))
            {
                await RollbackOutputAsync(
                    oldVideoGeneration,
                    oldAudioGeneration).ConfigureAwait(false);
                return await ResetFaultAsync().ConfigureAwait(false);
            }

            var timing = await _delivery.ResetTimingAsync(
                videoGeneration,
                audioGeneration,
                operation.Token).ConfigureAwait(false);
            lock (_sync)
            {
                _lastDeliverySnapshot = _delivery.Snapshot;
            }

            if (timing ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult.Reset)
            {
                lock (_sync)
                {
                    _resetCount = checked(_resetCount + 1);
                    _result =
                        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                            .None;
                }

                return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                    .Reset;
            }

            if (!await RollbackOutputAsync(
                    oldVideoGeneration,
                    oldAudioGeneration).ConfigureAwait(false))
            {
                return await ResetFaultAsync().ConfigureAwait(false);
            }

            return timing switch
            {
                PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                        .Cancelled => ResetCancellationResult(),
                PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                        .TimingRejected =>
                    PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                        .TimingRejected,
                _ => await ResetFaultAsync().ConfigureAwait(false),
            };
        }
        catch
        {
            if (!await RollbackOutputAsync(
                    oldVideoGeneration,
                    oldAudioGeneration).ConfigureAwait(false))
            {
                return await ResetFaultAsync().ConfigureAwait(false);
            }

            return operation.IsCancellationRequested
                ? ResetCancellationResult()
                : await ResetFaultAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeCancellation, operation))
                {
                    _activeCancellation = null;
                }

                _hasActiveReset = false;
            }

            operation.Dispose();
            _driveGate.Release();
        }
    }

    public void Dispose()
    {
        StopAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public async ValueTask StopAsync()
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed)
            {
                return;
            }

            _state =
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed;
            _result =
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.Disposed;
            _activeCancellation?.Cancel();
        }

        await _driveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeOwnedDeliveryAsync().ConfigureAwait(false);
        }
        finally
        {
            _driveGate.Release();
        }
    }

    private int IncrementDecisionCount()
    {
        lock (_sync)
        {
            _decisionCount = checked(_decisionCount + 1);
            return _decisionCount;
        }
    }

    private async ValueTask<
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult>
        MapDeliveryResultAsync(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult result)
    {
        var mapped = result switch
        {
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.VideoDelivered =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .VideoDelivered,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.AudioDelivered =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .AudioDelivered,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.VideoDropped =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .VideoDropped,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Empty =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.Empty,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                    .RetainedAudioRecoveryRequired =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .AudioRecoveryRequired,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.ClockNotStarted =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .ClockNotStarted,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.ResetRequired =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .ResetRequired,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Cancelled =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.Cancelled,
            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Disposed =>
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.Disposed,
            _ => PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                .DeliveryFaulted,
        };

        if (mapped ==
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                .DeliveryFaulted)
        {
            return await FaultAsync(mapped).ConfigureAwait(false);
        }

        if (mapped ==
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult.Disposed)
        {
            lock (_sync)
            {
                _state =
                    PlaybackIsoBmffRegularMovieTimedDeliveryDriverState
                        .Disposed;
            }
        }

        return SetResult(mapped);
    }

    private async ValueTask<
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult> FaultAsync(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult result)
    {
        lock (_sync)
        {
            _state =
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Faulted;
            SetResultLocked(result);
        }

        await DisposeOwnedDeliveryAsync().ConfigureAwait(false);
        return result;
    }

    private PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
        CancellationResult()
    {
        lock (_sync)
        {
            return SetResultLocked(
                _state ==
                    PlaybackIsoBmffRegularMovieTimedDeliveryDriverState
                        .Disposed
                    ? PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                        .Disposed
                    : PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                        .Cancelled);
        }
    }

    private PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult SetResult(
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult result)
    {
        lock (_sync)
        {
            return SetResultLocked(result);
        }
    }

    private PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
        SetResultLocked(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult result)
    {
        _result = result;
        return result;
    }

    private async ValueTask DisposeOwnedDeliveryAsync()
    {
        if (Interlocked.Exchange(ref _ownedDeliveryDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _delivery.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _resetOperations?.Dispose();
        }
    }

    private async ValueTask<bool> RollbackOutputAsync(
        long videoGeneration,
        long audioGeneration)
    {
        if (_resetOperations is null)
        {
            return false;
        }

        var output = await _resetOperations.ReconfigureOutputAsync(
            videoGeneration,
            audioGeneration,
            CancellationToken.None).ConfigureAwait(false);
        return output ==
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationResult
                    .Reconfigured &&
            _resetOperations.CommitSink(
                videoGeneration,
                audioGeneration);
    }

    private async ValueTask<
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult>
        ResetFaultAsync()
    {
        lock (_sync)
        {
            _state =
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Faulted;
            _result =
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverResult
                    .DeliveryFaulted;
        }

        await DisposeOwnedDeliveryAsync().ConfigureAwait(false);
        return PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
            .RollbackFaulted;
    }

    private PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
        ResetCancellationResult()
    {
        lock (_sync)
        {
            return _state ==
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverState.Disposed
                ? PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                    .Disposed
                : PlaybackIsoBmffRegularMovieTimedDeliveryDriverResetResult
                    .Cancelled;
        }
    }
}

public sealed record PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreation(
    PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult Result,
    PlaybackIsoBmffRegularMovieTimedDeliveryDriver? Driver);

public sealed class PlaybackIsoBmffRegularMovieTimedDeliveryDriverFactory
{
    private readonly IPlaybackIsoBmffRegularMovieTimedDeliveryDelay _delay;

    public PlaybackIsoBmffRegularMovieTimedDeliveryDriverFactory()
        : this(new PlaybackIsoBmffRegularMovieTimedDeliveryDelay())
    {
    }

    internal PlaybackIsoBmffRegularMovieTimedDeliveryDriverFactory(
        IPlaybackIsoBmffRegularMovieTimedDeliveryDelay delay)
    {
        ArgumentNullException.ThrowIfNull(delay);
        _delay = delay;
    }

    public PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreation Create(
        PlaybackIsoBmffRegularMovieSchedulerDelivery? delivery,
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions? options = null)
    {
        if (delivery is null)
        {
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .MissingDelivery,
                null);
        }

        var snapshot = delivery.Snapshot;
        if (snapshot.State !=
            PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Accepting)
        {
            delivery.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .DeliveryUnavailable,
                null);
        }

        if (!snapshot.HasTimingAdmission)
        {
            delivery.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .TimingUnavailable,
                null);
        }

        options ??=
            new PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions();
        if (!ValidateOptions(options))
        {
            delivery.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .InvalidOptions,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                .Created,
            new PlaybackIsoBmffRegularMovieTimedDeliveryDriver(
                delivery,
                _delay,
                options));
    }

    public PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreation Create(
        PlaybackIsoBmffRegularMovieSchedulerDelivery? delivery,
        PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration?
            outputReconfiguration,
        PlaybackIsoBmffRegularMovieWindowsOutputSink? outputSink,
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions? options = null)
    {
        if (delivery is null)
        {
            outputSink?.Dispose();
            outputReconfiguration?.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .MissingDelivery,
                null);
        }

        if (outputReconfiguration is null)
        {
            delivery.Dispose();
            outputSink?.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .MissingOutputReconfiguration,
                null);
        }

        if (outputSink is null)
        {
            delivery.Dispose();
            outputReconfiguration.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .MissingOutputSink,
                null);
        }

        var deliverySnapshot = delivery.Snapshot;
        if (deliverySnapshot.State !=
            PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Accepting)
        {
            DisposeResetOwned(delivery, outputReconfiguration, outputSink);
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .DeliveryUnavailable,
                null);
        }

        if (!deliverySnapshot.HasTimingAdmission)
        {
            DisposeResetOwned(delivery, outputReconfiguration, outputSink);
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .TimingUnavailable,
                null);
        }

        var outputSnapshot = outputReconfiguration.Snapshot;
        var sinkSnapshot = outputSink.Snapshot;
        if (outputSnapshot.State !=
                PlaybackIsoBmffRegularMovieWindowsOutputReconfigurationState
                    .Ready ||
            sinkSnapshot.State !=
                PlaybackIsoBmffRegularMovieWindowsOutputSinkState.Accepting)
        {
            DisposeResetOwned(delivery, outputReconfiguration, outputSink);
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .OutputUnavailable,
                null);
        }

        if (!delivery.UsesSink(outputSink) ||
            !outputReconfiguration.UsesSink(outputSink) ||
            deliverySnapshot.VideoGeneration != sinkSnapshot.VideoGeneration ||
            deliverySnapshot.AudioGeneration != sinkSnapshot.AudioGeneration ||
            deliverySnapshot.VideoGeneration !=
                outputSnapshot.Configuration.VideoBinding.Generation ||
            deliverySnapshot.AudioGeneration !=
                outputSnapshot.Configuration.AudioBinding.Generation)
        {
            DisposeResetOwned(delivery, outputReconfiguration, outputSink);
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .OutputMismatch,
                null);
        }

        options ??=
            new PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions();
        if (!ValidateOptions(options))
        {
            DisposeResetOwned(delivery, outputReconfiguration, outputSink);
            return new(
                PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                    .InvalidOptions,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverCreationResult
                .Created,
            new PlaybackIsoBmffRegularMovieTimedDeliveryDriver(
                delivery,
                _delay,
                options,
                new PlaybackIsoBmffRegularMovieTimedDeliveryResetOperations(
                    outputReconfiguration,
                    outputSink)));
    }

    private static bool ValidateOptions(
        PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions options) =>
        options.MaximumDecisionCount is >= 1 and <=
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions
                .MaximumDecisionCountLimit &&
        options.MaximumTotalWait > TimeSpan.Zero &&
        options.MaximumTotalWait <=
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions
                .MaximumTotalWaitLimit &&
        options.MinimumDelay > TimeSpan.Zero &&
        options.MaximumSingleDelay >= options.MinimumDelay &&
        options.MaximumSingleDelay <=
            PlaybackIsoBmffRegularMovieTimedDeliveryDriverOptions
                .MaximumSingleDelayLimit &&
        options.MaximumSingleDelay <= options.MaximumTotalWait;

    private static void DisposeResetOwned(
        PlaybackIsoBmffRegularMovieSchedulerDelivery delivery,
        PlaybackIsoBmffRegularMovieWindowsOutputReconfiguration
            outputReconfiguration,
        PlaybackIsoBmffRegularMovieWindowsOutputSink outputSink)
    {
        try
        {
            delivery.Dispose();
        }
        finally
        {
            try
            {
                outputSink.Dispose();
            }
            finally
            {
                outputReconfiguration.Dispose();
            }
        }
    }
}
