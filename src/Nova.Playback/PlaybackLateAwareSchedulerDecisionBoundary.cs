namespace PeerCast.Nova.Playback;

public enum PlaybackLateAwareSchedulerDecisionState
{
    Ready,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackLateAwareSchedulerDecisionResult
{
    ProcessNowRequired,
    WaitRequired,
    RetainedVideoLate,
    RetainedAudioLate,
    VideoDropConsumed,
    AudioRecoveryRequired,
    Empty,
    ClockNotStarted,
    ResetRequired,
    InvalidItem,
    InvalidEnvironment,
    AlreadyRunning,
    Cancelled,
    Disposed,
    Failure,
}

public readonly record struct PlaybackLateAwareSchedulerDecisionSnapshot(
    long VideoGeneration,
    long AudioGeneration,
    bool PreferVideoNext,
    PlaybackLateAwareSchedulerDecisionState State);

public sealed class PlaybackLateAwareSchedulerDecisionBoundary : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly IPlaybackWindowsOutputSchedulerSource _source;
    private readonly IPlaybackTimedSchedulerTimingDecider _timing;
    private readonly PlaybackLateReservationDispositionBoundary _disposition;
    private readonly Func<bool> _isSupportedPlatform;
    private CancellationTokenSource? _operationCancellation;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _preferVideoNext = true;
    private PlaybackLateAwareSchedulerDecisionState _state;
    private bool _isDisposed;

    public PlaybackLateAwareSchedulerDecisionBoundary(
        BoundedPlaybackScheduler scheduler,
        PlaybackAvTimingDecisionBoundary timing,
        PlaybackLateReservationDispositionBoundary disposition,
        long videoGeneration,
        long audioGeneration)
        : this(new BoundedPlaybackWindowsOutputSchedulerSource(scheduler),
            new PlaybackTimedSchedulerTimingDecider(timing), disposition,
            videoGeneration, audioGeneration,
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackLateAwareSchedulerDecisionBoundary(
        IPlaybackWindowsOutputSchedulerSource source,
        IPlaybackTimedSchedulerTimingDecider timing,
        PlaybackLateReservationDispositionBoundary disposition,
        long videoGeneration,
        long audioGeneration,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(disposition);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);
        _source = source;
        _timing = timing;
        _disposition = disposition;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public PlaybackLateAwareSchedulerDecisionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_videoGeneration, _audioGeneration, _preferVideoNext, _state);
        }
    }

    public async ValueTask<PlaybackLateAwareSchedulerDecisionResult> DecideOneAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed) return PlaybackLateAwareSchedulerDecisionResult.Disposed;
            if (_state == PlaybackLateAwareSchedulerDecisionState.ResetRequired)
                return PlaybackLateAwareSchedulerDecisionResult.ResetRequired;
            if (_state == PlaybackLateAwareSchedulerDecisionState.Faulted)
                return PlaybackLateAwareSchedulerDecisionResult.Failure;
            if (!_isSupportedPlatform())
                return PlaybackLateAwareSchedulerDecisionResult.InvalidEnvironment;
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return PlaybackLateAwareSchedulerDecisionResult.AlreadyRunning;

        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_isDisposed)
            {
                _gate.Release();
                return PlaybackLateAwareSchedulerDecisionResult.Disposed;
            }
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _operationCancellation = operation;
        }

        IPlaybackWindowsOutputSchedulerReservation? reservation = null;
        var completed = false;
        try
        {
            PlaybackSchedulerOutputKind preferred;
            lock (_sync)
                preferred = _preferVideoNext
                    ? PlaybackSchedulerOutputKind.Video
                    : PlaybackSchedulerOutputKind.Audio;

            var taken = await _source.ReserveAsync(preferred, operation.Token)
                .ConfigureAwait(false);
            if (!Enum.IsDefined(taken.Result)) return Fault();
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Empty)
                return PlaybackLateAwareSchedulerDecisionResult.Empty;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Cancelled)
                return PlaybackLateAwareSchedulerDecisionResult.Cancelled;
            if (taken.Result == PlaybackWindowsOutputSchedulerTakeResult.Disposed)
                return PlaybackLateAwareSchedulerDecisionResult.Disposed;
            if (taken.Result != PlaybackWindowsOutputSchedulerTakeResult.Reserved ||
                taken.Reservation is null)
                return Fault();

            reservation = taken.Reservation;
            if (!TryCreateTimingItem(reservation, out var item, out var generation))
                return PlaybackLateAwareSchedulerDecisionResult.InvalidItem;

            var decision = await _timing.DecideAsync(item, operation.Token).ConfigureAwait(false);
            if (!Enum.IsDefined(decision.Result)) return Fault();

            if (decision.Result is PlaybackAvTimingDecisionResult.VideoLate or
                PlaybackAvTimingDecisionResult.AudioLate)
            {
                var dispatchResult = decision.Result == PlaybackAvTimingDecisionResult.VideoLate
                    ? PlaybackTimedSchedulerDispatchResult.VideoLate
                    : PlaybackTimedSchedulerDispatchResult.AudioLate;
                var disposition = _disposition.Apply(reservation, dispatchResult, generation);
                completed = true;
                if (disposition == PlaybackLateReservationDispositionResult.VideoDropConsumed)
                {
                    lock (_sync) _preferVideoNext = false;
                }
                return MapDisposition(disposition);
            }

            return MapDecision(decision.Result);
        }
        catch (OperationCanceledException)
        {
            return PlaybackLateAwareSchedulerDecisionResult.Cancelled;
        }
        catch
        {
            return Fault();
        }
        finally
        {
            if (reservation is not null)
            {
                if (!completed)
                {
                    try { reservation.Complete(false); }
                    catch { SetFaulted(); }
                }
                reservation.Dispose();
            }
            lock (_sync)
            {
                if (ReferenceEquals(_operationCancellation, operation))
                    _operationCancellation = null;
            }
            operation.Dispose();
            _gate.Release();
        }
    }

    public async ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken = default)
    {
        if (videoGeneration < 0 || audioGeneration < 0) return false;
        lock (_sync)
        {
            if (_isDisposed) return false;
            _operationCancellation?.Cancel();
        }
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetResetRequired();
            return false;
        }

        try
        {
            lock (_sync)
                if (_isDisposed) return false;

            var timing = await _timing.ResetAsync(videoGeneration, audioGeneration,
                cancellationToken).ConfigureAwait(false);
            if (timing != PlaybackAvTimingResetResult.Reset ||
                !_disposition.Reset(videoGeneration, audioGeneration))
            {
                SetFaulted();
                return false;
            }

            lock (_sync)
            {
                _videoGeneration = videoGeneration;
                _audioGeneration = audioGeneration;
                _preferVideoNext = true;
                _state = PlaybackLateAwareSchedulerDecisionState.Ready;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            SetResetRequired();
            return false;
        }
        catch
        {
            SetFaulted();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackLateAwareSchedulerDecisionState.Disposed;
            _operationCancellation?.Cancel();
        }
        _gate.Wait();
        _gate.Release();
    }

    private bool TryCreateTimingItem(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        out PlaybackAvTimingItem item,
        out long generation)
    {
        item = default;
        generation = 0;
        if (!Enum.IsDefined(reservation.Kind) || reservation.Sequence < 0 ||
            !Enum.IsDefined(reservation.Discontinuity) || reservation.Frame.IsDisposed)
            return false;
        var timestamp = reservation.Frame.Metadata.PresentationTimestamp;
        if (!timestamp.IsKnown || timestamp.Value < TimeSpan.Zero) return false;
        var kind = reservation.Kind == PlaybackSchedulerOutputKind.Video
            ? PlaybackPolicyMediaKind.Video
            : PlaybackPolicyMediaKind.Audio;
        lock (_sync)
            generation = kind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration
                : _audioGeneration;
        item = new(kind,
            PlaybackPolicyTime.Known(timestamp.Value),
            reservation.Frame.Metadata.Duration.IsKnown
                ? PlaybackPolicyTime.Known(reservation.Frame.Metadata.Duration.Value)
                : PlaybackPolicyTime.Unknown,
            reservation.Sequence,
            reservation.Discontinuity,
            generation);
        return true;
    }

    private PlaybackLateAwareSchedulerDecisionResult Fault()
    {
        SetFaulted();
        return PlaybackLateAwareSchedulerDecisionResult.Failure;
    }

    private void SetFaulted()
    {
        lock (_sync)
            if (!_isDisposed) _state = PlaybackLateAwareSchedulerDecisionState.Faulted;
    }

    private void SetResetRequired()
    {
        lock (_sync)
            if (!_isDisposed) _state = PlaybackLateAwareSchedulerDecisionState.ResetRequired;
    }

    private PlaybackLateAwareSchedulerDecisionResult MapDecision(
        PlaybackAvTimingDecisionResult result)
    {
        switch (result)
        {
            case PlaybackAvTimingDecisionResult.ProcessNow:
                return PlaybackLateAwareSchedulerDecisionResult.ProcessNowRequired;
            case PlaybackAvTimingDecisionResult.Wait:
                return PlaybackLateAwareSchedulerDecisionResult.WaitRequired;
            case PlaybackAvTimingDecisionResult.ClockNotStarted:
                return PlaybackLateAwareSchedulerDecisionResult.ClockNotStarted;
            case PlaybackAvTimingDecisionResult.DiscontinuityResetRequired:
            case PlaybackAvTimingDecisionResult.GenerationMismatch:
                SetResetRequired();
                return PlaybackLateAwareSchedulerDecisionResult.ResetRequired;
            case PlaybackAvTimingDecisionResult.InvalidTimestamp:
            case PlaybackAvTimingDecisionResult.InvalidConfiguration:
            case PlaybackAvTimingDecisionResult.WaitLimitExceeded:
                return PlaybackLateAwareSchedulerDecisionResult.InvalidItem;
            case PlaybackAvTimingDecisionResult.Cancelled:
                return PlaybackLateAwareSchedulerDecisionResult.Cancelled;
            case PlaybackAvTimingDecisionResult.Disposed:
                return PlaybackLateAwareSchedulerDecisionResult.Disposed;
            default:
                return Fault();
        }
    }

    private PlaybackLateAwareSchedulerDecisionResult MapDisposition(
        PlaybackLateReservationDispositionResult result) => result switch
        {
            PlaybackLateReservationDispositionResult.RetainedVideoLate =>
                PlaybackLateAwareSchedulerDecisionResult.RetainedVideoLate,
            PlaybackLateReservationDispositionResult.RetainedAudioLate =>
                PlaybackLateAwareSchedulerDecisionResult.RetainedAudioLate,
            PlaybackLateReservationDispositionResult.VideoDropConsumed =>
                PlaybackLateAwareSchedulerDecisionResult.VideoDropConsumed,
            PlaybackLateReservationDispositionResult.AudioRecoveryRequired =>
                PlaybackLateAwareSchedulerDecisionResult.AudioRecoveryRequired,
            PlaybackLateReservationDispositionResult.ResetRequired =>
                SetAndReturnResetRequired(),
            PlaybackLateReservationDispositionResult.InvalidReservation =>
                PlaybackLateAwareSchedulerDecisionResult.InvalidItem,
            PlaybackLateReservationDispositionResult.Disposed =>
                PlaybackLateAwareSchedulerDecisionResult.Disposed,
            _ => Fault(),
        };

    private PlaybackLateAwareSchedulerDecisionResult SetAndReturnResetRequired()
    {
        SetResetRequired();
        return PlaybackLateAwareSchedulerDecisionResult.ResetRequired;
    }
}
