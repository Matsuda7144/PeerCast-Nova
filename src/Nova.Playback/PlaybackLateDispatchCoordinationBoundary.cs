namespace PeerCast.Nova.Playback;

public enum PlaybackLateDispatchCoordinationState
{
    Ready,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackLateDispatchCoordinationResult
{
    RetainedVideoLate,
    RetainedAudioLate,
    VideoDropCandidate,
    AudioRecoveryRequired,
    ResetRequired,
    InvalidInput,
    Disposed,
    Failure,
}

public readonly record struct PlaybackLateDispatchCoordinationRequest(
    PlaybackTimedSchedulerDispatchResult DispatchResult,
    PlaybackPolicyMediaKind MediaKind,
    PlaybackDecodedFrameDiscontinuityKind Discontinuity,
    long Sequence,
    long Generation);

public readonly record struct PlaybackLateDispatchCoordinationSnapshot(
    long VideoGeneration,
    long AudioGeneration,
    PlaybackLateDispatchCoordinationState State);

public sealed class PlaybackLateDispatchCoordinationBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackLateItemDispositionPolicyBoundary _policy;
    private long _videoGeneration;
    private long _audioGeneration;
    private PlaybackLateDispatchCoordinationState _state;
    private bool _isDisposed;

    public PlaybackLateDispatchCoordinationBoundary(
        PlaybackLateItemDispositionPolicyBoundary policy,
        long videoGeneration,
        long audioGeneration)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);
        _policy = policy;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
    }

    public PlaybackLateDispatchCoordinationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_videoGeneration, _audioGeneration, _state);
        }
    }

    public PlaybackLateDispatchCoordinationResult Coordinate(
        PlaybackLateDispatchCoordinationRequest request)
    {
        lock (_sync)
        {
            if (_isDisposed)
                return PlaybackLateDispatchCoordinationResult.Disposed;
            if (_state == PlaybackLateDispatchCoordinationState.ResetRequired)
                return PlaybackLateDispatchCoordinationResult.ResetRequired;
            if (_state == PlaybackLateDispatchCoordinationState.Faulted)
                return PlaybackLateDispatchCoordinationResult.Failure;
            if (!IsValid(request))
                return PlaybackLateDispatchCoordinationResult.InvalidInput;

            var expectedGeneration = request.MediaKind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration
                : _audioGeneration;
            if (request.Generation != expectedGeneration ||
                request.Discontinuity != PlaybackDecodedFrameDiscontinuityKind.None)
            {
                _state = PlaybackLateDispatchCoordinationState.ResetRequired;
                return PlaybackLateDispatchCoordinationResult.ResetRequired;
            }

            var timingResult = request.DispatchResult == PlaybackTimedSchedulerDispatchResult.VideoLate
                ? PlaybackAvTimingDecisionResult.VideoLate
                : PlaybackAvTimingDecisionResult.AudioLate;
            var decision = _policy.Decide(new PlaybackLateItemDispositionRequest(
                request.MediaKind,
                timingResult,
                request.Discontinuity,
                request.Sequence,
                request.Generation));

            return decision.Disposition switch
            {
                PlaybackLateItemDisposition.Retain => request.MediaKind == PlaybackPolicyMediaKind.Video
                    ? PlaybackLateDispatchCoordinationResult.RetainedVideoLate
                    : PlaybackLateDispatchCoordinationResult.RetainedAudioLate,
                PlaybackLateItemDisposition.VideoDropCandidate =>
                    PlaybackLateDispatchCoordinationResult.VideoDropCandidate,
                PlaybackLateItemDisposition.AudioRecoveryRequired =>
                    PlaybackLateDispatchCoordinationResult.AudioRecoveryRequired,
                PlaybackLateItemDisposition.ResetRequired => LatchResetRequired(),
                PlaybackLateItemDisposition.Disposed =>
                    PlaybackLateDispatchCoordinationResult.Disposed,
                PlaybackLateItemDisposition.InvalidInput =>
                    PlaybackLateDispatchCoordinationResult.InvalidInput,
                PlaybackLateItemDisposition.Failure => Fault(),
                _ => Fault(),
            };
        }
    }

    public bool Reset(long videoGeneration, long audioGeneration)
    {
        lock (_sync)
        {
            if (_isDisposed || videoGeneration < 0 || audioGeneration < 0)
                return false;
            if (!_policy.Reset(videoGeneration, audioGeneration))
            {
                _state = PlaybackLateDispatchCoordinationState.Faulted;
                return false;
            }
            _videoGeneration = videoGeneration;
            _audioGeneration = audioGeneration;
            _state = PlaybackLateDispatchCoordinationState.Ready;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackLateDispatchCoordinationState.Disposed;
        }
    }

    private PlaybackLateDispatchCoordinationResult LatchResetRequired()
    {
        _state = PlaybackLateDispatchCoordinationState.ResetRequired;
        return PlaybackLateDispatchCoordinationResult.ResetRequired;
    }

    private PlaybackLateDispatchCoordinationResult Fault()
    {
        _state = PlaybackLateDispatchCoordinationState.Faulted;
        return PlaybackLateDispatchCoordinationResult.Failure;
    }

    private static bool IsValid(PlaybackLateDispatchCoordinationRequest request)
    {
        if (!Enum.IsDefined(request.DispatchResult) ||
            !Enum.IsDefined(request.MediaKind) ||
            !Enum.IsDefined(request.Discontinuity) ||
            request.Sequence < 0 || request.Generation < 0)
            return false;

        return request.DispatchResult switch
        {
            PlaybackTimedSchedulerDispatchResult.VideoLate =>
                request.MediaKind == PlaybackPolicyMediaKind.Video,
            PlaybackTimedSchedulerDispatchResult.AudioLate =>
                request.MediaKind == PlaybackPolicyMediaKind.Audio,
            _ => false,
        };
    }
}
