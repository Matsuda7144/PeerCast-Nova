namespace PeerCast.Nova.Playback;

public enum PlaybackLateItemDispositionState
{
    Ready,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackLateItemDisposition
{
    Retain,
    VideoDropCandidate,
    AudioRecoveryRequired,
    ResetRequired,
    InvalidInput,
    Disposed,
    Failure,
}

public enum PlaybackVideoLatePolicy
{
    Retain,
    MarkDropCandidate,
}

public enum PlaybackAudioLatePolicy
{
    Retain,
    RequireRecovery,
}

public sealed record PlaybackLateItemDispositionOptions
{
    public PlaybackVideoLatePolicy VideoLatePolicy { get; init; } =
        PlaybackVideoLatePolicy.Retain;
    public PlaybackAudioLatePolicy AudioLatePolicy { get; init; } =
        PlaybackAudioLatePolicy.Retain;
}

public readonly record struct PlaybackLateItemDispositionRequest(
    PlaybackPolicyMediaKind MediaKind,
    PlaybackAvTimingDecisionResult TimingResult,
    PlaybackDecodedFrameDiscontinuityKind Discontinuity,
    long Sequence,
    long Generation);

public readonly record struct PlaybackLateItemDispositionDecision(
    PlaybackLateItemDisposition Disposition,
    PlaybackAvTimingDecisionResult TimingResult);

public readonly record struct PlaybackLateItemDispositionSnapshot(
    long VideoGeneration,
    long AudioGeneration,
    PlaybackLateItemDispositionState State);

public sealed class PlaybackLateItemDispositionPolicyBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackLateItemDispositionOptions _options;
    private long _videoGeneration;
    private long _audioGeneration;
    private PlaybackLateItemDispositionState _state;
    private bool _isDisposed;

    public PlaybackLateItemDispositionPolicyBoundary(
        PlaybackLateItemDispositionOptions options,
        long videoGeneration,
        long audioGeneration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);
        ValidateOptions(options);
        _options = options;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
    }

    public PlaybackLateItemDispositionSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_videoGeneration, _audioGeneration, _state);
        }
    }

    public PlaybackLateItemDispositionDecision Decide(
        PlaybackLateItemDispositionRequest request)
    {
        lock (_sync)
        {
            if (_isDisposed)
                return Decision(PlaybackLateItemDisposition.Disposed, request.TimingResult);
            if (_state == PlaybackLateItemDispositionState.ResetRequired)
                return Decision(PlaybackLateItemDisposition.ResetRequired, request.TimingResult);
            if (_state == PlaybackLateItemDispositionState.Faulted)
                return Decision(PlaybackLateItemDisposition.Failure, request.TimingResult);
            if (!IsValid(request))
                return Decision(PlaybackLateItemDisposition.InvalidInput, request.TimingResult);

            var expectedGeneration = request.MediaKind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration : _audioGeneration;
            if (request.Generation != expectedGeneration)
            {
                _state = PlaybackLateItemDispositionState.ResetRequired;
                return Decision(PlaybackLateItemDisposition.ResetRequired, request.TimingResult);
            }
            if (request.Discontinuity != PlaybackDecodedFrameDiscontinuityKind.None)
            {
                _state = PlaybackLateItemDispositionState.ResetRequired;
                return Decision(PlaybackLateItemDisposition.ResetRequired, request.TimingResult);
            }

            if (request.TimingResult == PlaybackAvTimingDecisionResult.VideoLate)
                return Decision(
                    _options.VideoLatePolicy == PlaybackVideoLatePolicy.MarkDropCandidate
                        ? PlaybackLateItemDisposition.VideoDropCandidate
                        : PlaybackLateItemDisposition.Retain,
                    request.TimingResult);

            return Decision(
                _options.AudioLatePolicy == PlaybackAudioLatePolicy.RequireRecovery
                    ? PlaybackLateItemDisposition.AudioRecoveryRequired
                    : PlaybackLateItemDisposition.Retain,
                request.TimingResult);
        }
    }

    public bool Reset(long videoGeneration, long audioGeneration)
    {
        lock (_sync)
        {
            if (_isDisposed || videoGeneration < 0 || audioGeneration < 0)
                return false;
            _videoGeneration = videoGeneration;
            _audioGeneration = audioGeneration;
            _state = PlaybackLateItemDispositionState.Ready;
            return true;
        }
    }

    public void Fault()
    {
        lock (_sync)
        {
            if (!_isDisposed)
                _state = PlaybackLateItemDispositionState.Faulted;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackLateItemDispositionState.Disposed;
        }
    }

    private static PlaybackLateItemDispositionDecision Decision(
        PlaybackLateItemDisposition disposition,
        PlaybackAvTimingDecisionResult timingResult) => new(disposition, timingResult);

    private static bool IsValid(PlaybackLateItemDispositionRequest request)
    {
        if (!Enum.IsDefined(request.MediaKind) ||
            !Enum.IsDefined(request.TimingResult) ||
            !Enum.IsDefined(request.Discontinuity) ||
            request.Sequence < 0 || request.Generation < 0)
            return false;

        return request.MediaKind switch
        {
            PlaybackPolicyMediaKind.Video =>
                request.TimingResult == PlaybackAvTimingDecisionResult.VideoLate,
            PlaybackPolicyMediaKind.Audio =>
                request.TimingResult == PlaybackAvTimingDecisionResult.AudioLate,
            _ => false,
        };
    }

    private static void ValidateOptions(PlaybackLateItemDispositionOptions options)
    {
        if (!Enum.IsDefined(options.VideoLatePolicy) ||
            !Enum.IsDefined(options.AudioLatePolicy))
            throw new ArgumentOutOfRangeException(nameof(options));
    }
}
