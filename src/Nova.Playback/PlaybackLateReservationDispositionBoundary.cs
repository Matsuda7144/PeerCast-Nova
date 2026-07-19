namespace PeerCast.Nova.Playback;

public enum PlaybackLateReservationDispositionState
{
    Ready,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackLateReservationDispositionResult
{
    RetainedVideoLate,
    RetainedAudioLate,
    VideoDropConsumed,
    AudioRecoveryRequired,
    ResetRequired,
    InvalidReservation,
    Disposed,
    Failure,
}

public readonly record struct PlaybackLateReservationDispositionSnapshot(
    PlaybackLateReservationDispositionState State,
    long VideoDropCount);

public sealed class PlaybackLateReservationDispositionBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackLateDispatchCoordinationBoundary _coordination;
    private PlaybackLateReservationDispositionState _state;
    private long _videoDropCount;
    private bool _isDisposed;

    public PlaybackLateReservationDispositionBoundary(
        PlaybackLateDispatchCoordinationBoundary coordination)
    {
        ArgumentNullException.ThrowIfNull(coordination);
        _coordination = coordination;
    }

    public PlaybackLateReservationDispositionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_state, _videoDropCount);
        }
    }

    internal PlaybackLateReservationDispositionResult Apply(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        PlaybackTimedSchedulerDispatchResult dispatchResult,
        long generation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (_sync)
        {
            if (_isDisposed)
                return CompleteRetained(reservation,
                    PlaybackLateReservationDispositionResult.Disposed);
            if (_state == PlaybackLateReservationDispositionState.ResetRequired)
                return CompleteRetained(reservation,
                    PlaybackLateReservationDispositionResult.ResetRequired);
            if (_state == PlaybackLateReservationDispositionState.Faulted)
                return CompleteRetained(reservation,
                    PlaybackLateReservationDispositionResult.Failure);
            if (!IsValidReservation(reservation, dispatchResult, generation))
                return CompleteRetained(reservation,
                    PlaybackLateReservationDispositionResult.InvalidReservation);

            var mediaKind = reservation.Kind == PlaybackSchedulerOutputKind.Video
                ? PlaybackPolicyMediaKind.Video
                : PlaybackPolicyMediaKind.Audio;
            var coordinated = _coordination.Coordinate(new(
                dispatchResult,
                mediaKind,
                reservation.Discontinuity,
                reservation.Sequence,
                generation));

            switch (coordinated)
            {
                case PlaybackLateDispatchCoordinationResult.RetainedVideoLate:
                    return CompleteRetained(reservation,
                        PlaybackLateReservationDispositionResult.RetainedVideoLate);
                case PlaybackLateDispatchCoordinationResult.RetainedAudioLate:
                    return CompleteRetained(reservation,
                        PlaybackLateReservationDispositionResult.RetainedAudioLate);
                case PlaybackLateDispatchCoordinationResult.VideoDropCandidate:
                    reservation.Complete(true);
                    if (_videoDropCount < long.MaxValue)
                        _videoDropCount++;
                    return PlaybackLateReservationDispositionResult.VideoDropConsumed;
                case PlaybackLateDispatchCoordinationResult.AudioRecoveryRequired:
                    return CompleteRetained(reservation,
                        PlaybackLateReservationDispositionResult.AudioRecoveryRequired);
                case PlaybackLateDispatchCoordinationResult.ResetRequired:
                    _state = PlaybackLateReservationDispositionState.ResetRequired;
                    return CompleteRetained(reservation,
                        PlaybackLateReservationDispositionResult.ResetRequired);
                case PlaybackLateDispatchCoordinationResult.Disposed:
                    return CompleteRetained(reservation,
                        PlaybackLateReservationDispositionResult.Disposed);
                case PlaybackLateDispatchCoordinationResult.InvalidInput:
                    return CompleteRetained(reservation,
                        PlaybackLateReservationDispositionResult.InvalidReservation);
                default:
                    _state = PlaybackLateReservationDispositionState.Faulted;
                    return CompleteRetained(reservation,
                        PlaybackLateReservationDispositionResult.Failure);
            }
        }
    }

    public bool Reset(long videoGeneration, long audioGeneration)
    {
        lock (_sync)
        {
            if (_isDisposed || videoGeneration < 0 || audioGeneration < 0)
                return false;
            if (!_coordination.Reset(videoGeneration, audioGeneration))
            {
                _state = PlaybackLateReservationDispositionState.Faulted;
                return false;
            }
            _state = PlaybackLateReservationDispositionState.Ready;
            _videoDropCount = 0;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackLateReservationDispositionState.Disposed;
        }
    }

    private static PlaybackLateReservationDispositionResult CompleteRetained(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        PlaybackLateReservationDispositionResult result)
    {
        reservation.Complete(false);
        return result;
    }

    private static bool IsValidReservation(
        IPlaybackWindowsOutputSchedulerReservation reservation,
        PlaybackTimedSchedulerDispatchResult dispatchResult,
        long generation)
    {
        if (!Enum.IsDefined(reservation.Kind) ||
            !Enum.IsDefined(reservation.Discontinuity) ||
            reservation.Sequence < 0 || generation < 0 ||
            reservation.Frame.IsDisposed || !Enum.IsDefined(dispatchResult))
            return false;

        return dispatchResult switch
        {
            PlaybackTimedSchedulerDispatchResult.VideoLate =>
                reservation.Kind == PlaybackSchedulerOutputKind.Video,
            PlaybackTimedSchedulerDispatchResult.AudioLate =>
                reservation.Kind == PlaybackSchedulerOutputKind.Audio,
            _ => false,
        };
    }
}
