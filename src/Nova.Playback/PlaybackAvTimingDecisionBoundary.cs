namespace PeerCast.Nova.Playback;

public enum PlaybackAvTimingDecisionState
{
    Ready,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackAvTimingDecisionResult
{
    ProcessNow,
    Wait,
    VideoLate,
    AudioLate,
    DiscontinuityResetRequired,
    ClockNotStarted,
    GenerationMismatch,
    WaitLimitExceeded,
    InvalidTimestamp,
    InvalidConfiguration,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackAvTimingResetResult
{
    Reset,
    Cancelled,
    Disposed,
    Failure,
    PartialResetFault,
}

public sealed record PlaybackAvTimingTolerances
{
    public static readonly TimeSpan MaximumTolerance = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumWaitLimit = TimeSpan.FromSeconds(2);

    public TimeSpan VideoEarlyTolerance { get; init; } = TimeSpan.FromMilliseconds(10);
    public TimeSpan VideoLateTolerance { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan AudioEarlyTolerance { get; init; } = TimeSpan.FromMilliseconds(5);
    public TimeSpan AudioLateTolerance { get; init; } = TimeSpan.FromMilliseconds(20);
    public TimeSpan MaximumWait { get; init; } = TimeSpan.FromMilliseconds(250);
}

public readonly record struct PlaybackAvTimingItem(
    PlaybackPolicyMediaKind MediaKind,
    PlaybackPolicyTime PresentationTimestamp,
    PlaybackPolicyTime Duration,
    long Sequence,
    PlaybackDecodedFrameDiscontinuityKind Discontinuity,
    long Generation);

public readonly record struct PlaybackAvTimingDecision(
    PlaybackAvTimingDecisionResult Result,
    TimeSpan WaitDuration);

public readonly record struct PlaybackAvTimingDecisionSnapshot(
    long VideoGeneration,
    long AudioGeneration,
    PlaybackAvTimingDecisionState State);

public sealed class PlaybackAvTimingDecisionBoundary : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackAvTimingPositionSource _positionSource;
    private readonly IPlaybackAvTimingGenerationResetter _resetter;
    private readonly PlaybackAvTimingTolerances _tolerances;
    private readonly bool _anchorFirstPresentationTimestamp;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _hasPosition;
    private TimeSpan _lastPosition;
    private bool _hasPresentationAnchor;
    private TimeSpan _presentationAnchorTimestamp;
    private TimeSpan _presentationAnchorPosition;
    private PlaybackAvTimingDecisionState _state;
    private volatile bool _isDisposed;

    public PlaybackAvTimingDecisionBoundary(
        IPlaybackMonotonicClock clock,
        PlaybackAvTimingTolerances tolerances,
        long videoGeneration,
        long audioGeneration)
        : this(new MonotonicPlaybackAvTimingPositionSource(clock),
            new InMemoryPlaybackAvTimingGenerationResetter(), tolerances,
            videoGeneration, audioGeneration, anchorFirstPresentationTimestamp: true)
    {
    }

    internal PlaybackAvTimingDecisionBoundary(
        IPlaybackAvTimingPositionSource positionSource,
        IPlaybackAvTimingGenerationResetter resetter,
        PlaybackAvTimingTolerances tolerances,
        long videoGeneration,
        long audioGeneration)
        : this(positionSource, resetter, tolerances, videoGeneration, audioGeneration,
            anchorFirstPresentationTimestamp: false)
    {
    }

    internal PlaybackAvTimingDecisionBoundary(
        IPlaybackAvTimingPositionSource positionSource,
        IPlaybackAvTimingGenerationResetter resetter,
        PlaybackAvTimingTolerances tolerances,
        long videoGeneration,
        long audioGeneration,
        bool anchorFirstPresentationTimestamp)
    {
        ArgumentNullException.ThrowIfNull(positionSource);
        ArgumentNullException.ThrowIfNull(resetter);
        ArgumentNullException.ThrowIfNull(tolerances);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);
        ValidateTolerances(tolerances);
        _positionSource = positionSource;
        _resetter = resetter;
        _tolerances = tolerances;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _anchorFirstPresentationTimestamp = anchorFirstPresentationTimestamp;
    }

    public PlaybackAvTimingDecisionSnapshot Snapshot =>
        new(_videoGeneration, _audioGeneration, _state);

    public async ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return Decision(PlaybackAvTimingDecisionResult.Disposed);
        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
            return Decision(PlaybackAvTimingDecisionResult.Cancelled);
        try
        {
            if (_isDisposed) return Decision(PlaybackAvTimingDecisionResult.Disposed);
            if (_state == PlaybackAvTimingDecisionState.ResetRequired)
                return Decision(PlaybackAvTimingDecisionResult.DiscontinuityResetRequired);
            if (_state == PlaybackAvTimingDecisionState.Faulted)
                return Decision(PlaybackAvTimingDecisionResult.Failure);
            if (!IsValid(item))
                return Decision(PlaybackAvTimingDecisionResult.InvalidTimestamp);

            var expectedGeneration = item.MediaKind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration : _audioGeneration;
            if (item.Generation != expectedGeneration)
                return Decision(PlaybackAvTimingDecisionResult.GenerationMismatch);
            if (item.Discontinuity != PlaybackDecodedFrameDiscontinuityKind.None)
            {
                _state = PlaybackAvTimingDecisionState.ResetRequired;
                return Decision(PlaybackAvTimingDecisionResult.DiscontinuityResetRequired);
            }

            PlaybackAvTimingPositionRead read;
            try { read = await _positionSource.ReadAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return Decision(PlaybackAvTimingDecisionResult.Cancelled); }
            catch { _state = PlaybackAvTimingDecisionState.Faulted; return Decision(PlaybackAvTimingDecisionResult.Failure); }
            if (!Enum.IsDefined(read.Result))
                return Fault();
            if (read.Result == PlaybackAvTimingPositionResult.NotStarted)
                return Decision(PlaybackAvTimingDecisionResult.ClockNotStarted);
            if (read.Result == PlaybackAvTimingPositionResult.Cancelled)
                return Decision(PlaybackAvTimingDecisionResult.Cancelled);
            if (read.Result != PlaybackAvTimingPositionResult.Started)
                return Fault();
            if (read.Position < TimeSpan.Zero)
                return Fault();
            if (_hasPosition && read.Position < _lastPosition)
                return Fault();

            long deltaTicks;
            try
            {
                deltaTicks = CalculateDeltaTicks(item.PresentationTimestamp.Value, read.Position);
            }
            catch (OverflowException)
            {
                return Fault();
            }
            _hasPosition = true;
            _lastPosition = read.Position;

            var early = item.MediaKind == PlaybackPolicyMediaKind.Video
                ? _tolerances.VideoEarlyTolerance : _tolerances.AudioEarlyTolerance;
            var late = item.MediaKind == PlaybackPolicyMediaKind.Video
                ? _tolerances.VideoLateTolerance : _tolerances.AudioLateTolerance;
            if (deltaTicks > early.Ticks)
            {
                if (deltaTicks > _tolerances.MaximumWait.Ticks)
                    return Decision(PlaybackAvTimingDecisionResult.WaitLimitExceeded);
                return new(PlaybackAvTimingDecisionResult.Wait, TimeSpan.FromTicks(deltaTicks));
            }
            if (deltaTicks < -late.Ticks)
                return Decision(item.MediaKind == PlaybackPolicyMediaKind.Video
                    ? PlaybackAvTimingDecisionResult.VideoLate
                    : PlaybackAvTimingDecisionResult.AudioLate);
            return Decision(PlaybackAvTimingDecisionResult.ProcessNow);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<PlaybackAvTimingResetResult> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return PlaybackAvTimingResetResult.Disposed;
        if (videoGeneration < 0 || audioGeneration < 0)
            return PlaybackAvTimingResetResult.Failure;
        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
            return PlaybackAvTimingResetResult.Cancelled;
        try
        {
            if (_isDisposed) return PlaybackAvTimingResetResult.Disposed;
            var audio = await InvokeResetAsync(false, audioGeneration, cancellationToken).ConfigureAwait(false);
            if (audio != PlaybackAvTimingGenerationResetResult.Reset) return MapReset(audio);
            var video = await InvokeResetAsync(true, videoGeneration, cancellationToken).ConfigureAwait(false);
            if (video != PlaybackAvTimingGenerationResetResult.Reset)
            {
                var rollback = await InvokeResetAsync(false, _audioGeneration, CancellationToken.None)
                    .ConfigureAwait(false);
                if (rollback == PlaybackAvTimingGenerationResetResult.Reset) return MapReset(video);
                _audioGeneration = audioGeneration;
                _state = PlaybackAvTimingDecisionState.Faulted;
                return PlaybackAvTimingResetResult.PartialResetFault;
            }

            var position = await InvokePositionResetAsync(cancellationToken).ConfigureAwait(false);
            if (position != PlaybackAvTimingPositionResetResult.Reset)
            {
                var videoRollback = await InvokeResetAsync(true, _videoGeneration,
                    CancellationToken.None).ConfigureAwait(false);
                var audioRollback = await InvokeResetAsync(false, _audioGeneration,
                    CancellationToken.None).ConfigureAwait(false);
                if (videoRollback != PlaybackAvTimingGenerationResetResult.Reset)
                    _videoGeneration = videoGeneration;
                if (audioRollback != PlaybackAvTimingGenerationResetResult.Reset)
                    _audioGeneration = audioGeneration;
                _state = PlaybackAvTimingDecisionState.Faulted;
                return PlaybackAvTimingResetResult.PartialResetFault;
            }
            _videoGeneration = videoGeneration;
            _audioGeneration = audioGeneration;
            _hasPosition = false;
            _lastPosition = default;
            ClearPresentationAnchor();
            _state = PlaybackAvTimingDecisionState.Ready;
            return PlaybackAvTimingResetResult.Reset;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _gate.Wait();
        try { _state = PlaybackAvTimingDecisionState.Disposed; }
        finally { _gate.Release(); }
    }

    private long CalculateDeltaTicks(TimeSpan presentationTimestamp, TimeSpan position)
    {
        if (!_anchorFirstPresentationTimestamp)
        {
            return checked(presentationTimestamp.Ticks - position.Ticks);
        }

        if (!_hasPresentationAnchor)
        {
            _hasPresentationAnchor = true;
            _presentationAnchorTimestamp = presentationTimestamp;
            _presentationAnchorPosition = position;
            return 0;
        }

        var timestampOffsetTicks = checked(
            presentationTimestamp.Ticks - _presentationAnchorTimestamp.Ticks);
        var targetPositionTicks = checked(
            _presentationAnchorPosition.Ticks + timestampOffsetTicks);
        return checked(targetPositionTicks - position.Ticks);
    }

    private void ClearPresentationAnchor()
    {
        _hasPresentationAnchor = false;
        _presentationAnchorTimestamp = default;
        _presentationAnchorPosition = default;
    }

    private static bool IsValid(PlaybackAvTimingItem item) =>
        Enum.IsDefined(item.MediaKind) && Enum.IsDefined(item.Discontinuity) &&
        item.Sequence >= 0 && item.Generation >= 0 && item.PresentationTimestamp.IsKnown &&
        item.PresentationTimestamp.Value >= TimeSpan.Zero &&
        (!item.Duration.IsKnown || item.Duration.Value > TimeSpan.Zero &&
            item.Duration.Value <= PlaybackDecodedVideoFrame.MaximumDuration);

    private static void ValidateTolerances(PlaybackAvTimingTolerances tolerances)
    {
        ValidateBound(tolerances.VideoEarlyTolerance, PlaybackAvTimingTolerances.MaximumTolerance);
        ValidateBound(tolerances.VideoLateTolerance, PlaybackAvTimingTolerances.MaximumTolerance);
        ValidateBound(tolerances.AudioEarlyTolerance, PlaybackAvTimingTolerances.MaximumTolerance);
        ValidateBound(tolerances.AudioLateTolerance, PlaybackAvTimingTolerances.MaximumTolerance);
        ValidateBound(tolerances.MaximumWait, PlaybackAvTimingTolerances.MaximumWaitLimit);
        if (tolerances.MaximumWait == TimeSpan.Zero ||
            tolerances.VideoEarlyTolerance > tolerances.MaximumWait ||
            tolerances.AudioEarlyTolerance > tolerances.MaximumWait)
            throw new ArgumentOutOfRangeException(nameof(tolerances));
    }

    private static void ValidateBound(TimeSpan value, TimeSpan maximum)
    {
        if (value < TimeSpan.Zero || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private PlaybackAvTimingDecision Fault()
    {
        _state = PlaybackAvTimingDecisionState.Faulted;
        return Decision(PlaybackAvTimingDecisionResult.Failure);
    }

    private static PlaybackAvTimingDecision Decision(PlaybackAvTimingDecisionResult result) =>
        new(result, TimeSpan.Zero);

    private async ValueTask<PlaybackAvTimingGenerationResetResult> InvokeResetAsync(
        bool video, long generation, CancellationToken cancellationToken)
    {
        try
        {
            var result = video
                ? await _resetter.ResetVideoAsync(generation, cancellationToken).ConfigureAwait(false)
                : await _resetter.ResetAudioAsync(generation, cancellationToken).ConfigureAwait(false);
            return Enum.IsDefined(result) ? result : PlaybackAvTimingGenerationResetResult.Failure;
        }
        catch (OperationCanceledException) { return PlaybackAvTimingGenerationResetResult.Cancelled; }
        catch { return PlaybackAvTimingGenerationResetResult.Failure; }
    }

    private async ValueTask<PlaybackAvTimingPositionResetResult> InvokePositionResetAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _positionSource.ResetAsync(cancellationToken).ConfigureAwait(false);
            return Enum.IsDefined(result) ? result : PlaybackAvTimingPositionResetResult.Failure;
        }
        catch (OperationCanceledException) { return PlaybackAvTimingPositionResetResult.Cancelled; }
        catch { return PlaybackAvTimingPositionResetResult.Failure; }
    }

    private static PlaybackAvTimingResetResult MapReset(PlaybackAvTimingGenerationResetResult result) =>
        result switch
        {
            PlaybackAvTimingGenerationResetResult.Cancelled => PlaybackAvTimingResetResult.Cancelled,
            PlaybackAvTimingGenerationResetResult.Disposed => PlaybackAvTimingResetResult.Disposed,
            _ => PlaybackAvTimingResetResult.Failure,
        };

    private async ValueTask<bool> EnterAsync(CancellationToken cancellationToken)
    {
        try { await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) { return false; }
    }
}

internal enum PlaybackAvTimingPositionResult { Started, NotStarted, Cancelled, Failure }
internal enum PlaybackAvTimingPositionResetResult { Reset, Cancelled, Disposed, Failure }
internal readonly record struct PlaybackAvTimingPositionRead(
    PlaybackAvTimingPositionResult Result, TimeSpan Position);
internal interface IPlaybackAvTimingPositionSource
{
    ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken);
    ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(CancellationToken cancellationToken);
}
internal sealed class MonotonicPlaybackAvTimingPositionSource :
    IPlaybackAvTimingPositionSource
{
    private readonly object _sync = new();
    private readonly IPlaybackMonotonicClock _clock;
    private bool _hasOrigin;
    private TimeSpan _origin;

    public MonotonicPlaybackAvTimingPositionSource(IPlaybackMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    public ValueTask<PlaybackAvTimingPositionRead> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var now = _clock.GetTimestamp();
            if (!_hasOrigin)
            {
                _hasOrigin = true;
                _origin = now;
                return ValueTask.FromResult(new PlaybackAvTimingPositionRead(
                    PlaybackAvTimingPositionResult.Started, TimeSpan.Zero));
            }

            var elapsedTicks = checked(now.Ticks - _origin.Ticks);
            return ValueTask.FromResult(new PlaybackAvTimingPositionRead(
                PlaybackAvTimingPositionResult.Started, TimeSpan.FromTicks(elapsedTicks)));
        }
    }

    public ValueTask<PlaybackAvTimingPositionResetResult> ResetAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _hasOrigin = false;
            _origin = default;
            return ValueTask.FromResult(PlaybackAvTimingPositionResetResult.Reset);
        }
    }
}

internal enum PlaybackAvTimingGenerationResetResult { Reset, Cancelled, Disposed, Failure }
internal interface IPlaybackAvTimingGenerationResetter
{
    ValueTask<PlaybackAvTimingGenerationResetResult> ResetAudioAsync(long generation, CancellationToken cancellationToken);
    ValueTask<PlaybackAvTimingGenerationResetResult> ResetVideoAsync(long generation, CancellationToken cancellationToken);
}
internal sealed class InMemoryPlaybackAvTimingGenerationResetter : IPlaybackAvTimingGenerationResetter
{
    public ValueTask<PlaybackAvTimingGenerationResetResult> ResetAudioAsync(long generation, CancellationToken cancellationToken) =>
        Complete(generation, cancellationToken);
    public ValueTask<PlaybackAvTimingGenerationResetResult> ResetVideoAsync(long generation, CancellationToken cancellationToken) =>
        Complete(generation, cancellationToken);
    private static ValueTask<PlaybackAvTimingGenerationResetResult> Complete(
        long generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(generation < 0
            ? PlaybackAvTimingGenerationResetResult.Failure
            : PlaybackAvTimingGenerationResetResult.Reset);
    }
}
