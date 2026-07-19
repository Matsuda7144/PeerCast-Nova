namespace PeerCast.Nova.Playback;

public enum PlaybackAudioRecoveryState
{
    Ready,
    Recovering,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackAudioRecoveryEffectResult
{
    Recovered,
    RetryRequired,
    ResetRequired,
    InvalidConfiguration,
    Cancelled,
    Disposed,
    Failure,
}

public enum PlaybackAudioRecoveryResult
{
    Recovered,
    AttemptLimitReached,
    ResetRequired,
    InvalidConfiguration,
    AlreadyRunning,
    Cancelled,
    Disposed,
    Failure,
}

public sealed record PlaybackAudioRecoveryOptions
{
    public const int MaximumAllowedAttempts = 16;
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(1);

    public int MaximumAttempts { get; init; } = 1;
    public TimeSpan RetryDelay { get; init; }
}

public readonly record struct PlaybackAudioRecoverySnapshot(
    PlaybackAudioRecoveryState State,
    int AttemptCount,
    long RecoveryGeneration,
    long VideoGeneration,
    long AudioGeneration);

public interface IPlaybackAudioRecoveryEffect
{
    ValueTask<PlaybackAudioRecoveryEffectResult> RecoverAsync(
        long audioGeneration,
        CancellationToken cancellationToken);
}

public interface IPlaybackAudioRecoveryReset
{
    ValueTask<bool> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken);
}

public sealed class PlaybackAudioRecoveryBoundary : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackAudioRecoveryEffect _effect;
    private readonly IPlaybackAudioRecoveryReset _reset;
    private readonly IPlaybackAudioRecoveryDelay _delay;
    private CancellationTokenSource? _operationCancellation;
    private PlaybackAudioRecoveryState _state;
    private int _attemptCount;
    private long _recoveryGeneration;
    private long _videoGeneration;
    private long _audioGeneration;
    private bool _isDisposed;

    public PlaybackAudioRecoveryBoundary(
        IPlaybackAudioRecoveryEffect effect,
        IPlaybackAudioRecoveryReset reset)
        : this(effect, reset, new TaskPlaybackAudioRecoveryDelay())
    {
    }

    internal PlaybackAudioRecoveryBoundary(
        IPlaybackAudioRecoveryEffect effect,
        IPlaybackAudioRecoveryReset reset,
        IPlaybackAudioRecoveryDelay delay)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(reset);
        ArgumentNullException.ThrowIfNull(delay);
        _effect = effect;
        _reset = reset;
        _delay = delay;
    }

    public PlaybackAudioRecoverySnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_state, _attemptCount, _recoveryGeneration,
                    _videoGeneration, _audioGeneration);
        }
    }

    public async ValueTask<PlaybackAudioRecoveryResult> RecoverAsync(
        long videoGeneration,
        long audioGeneration,
        PlaybackAudioRecoveryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!Validate(videoGeneration, audioGeneration, options))
            return PlaybackAudioRecoveryResult.InvalidConfiguration;
        lock (_sync)
        {
            if (_isDisposed) return PlaybackAudioRecoveryResult.Disposed;
            if (_state == PlaybackAudioRecoveryState.ResetRequired)
                return PlaybackAudioRecoveryResult.ResetRequired;
            if (_state == PlaybackAudioRecoveryState.Faulted)
                return PlaybackAudioRecoveryResult.Failure;
        }
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return PlaybackAudioRecoveryResult.AlreadyRunning;

        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_isDisposed)
            {
                _gate.Release();
                return PlaybackAudioRecoveryResult.Disposed;
            }
            _state = PlaybackAudioRecoveryState.Recovering;
            _attemptCount = 0;
            _videoGeneration = videoGeneration;
            _audioGeneration = audioGeneration;
            if (_recoveryGeneration < long.MaxValue) _recoveryGeneration++;
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _operationCancellation = operation;
        }

        try
        {
            for (var attempt = 1; attempt <= options.MaximumAttempts; attempt++)
            {
                lock (_sync) _attemptCount = attempt;
                var effect = await _effect.RecoverAsync(audioGeneration, operation.Token)
                    .ConfigureAwait(false);
                if (!Enum.IsDefined(effect)) return Fault();
                switch (effect)
                {
                    case PlaybackAudioRecoveryEffectResult.Recovered:
                        if (!await _reset.ResetAsync(videoGeneration, audioGeneration,
                                operation.Token).ConfigureAwait(false))
                            return Fault();
                        lock (_sync)
                            if (!_isDisposed) _state = PlaybackAudioRecoveryState.Ready;
                        return PlaybackAudioRecoveryResult.Recovered;
                    case PlaybackAudioRecoveryEffectResult.RetryRequired:
                        if (attempt == options.MaximumAttempts)
                            return SetResetRequired(PlaybackAudioRecoveryResult.AttemptLimitReached);
                        if (options.RetryDelay > TimeSpan.Zero)
                            await _delay.DelayAsync(options.RetryDelay, operation.Token)
                                .ConfigureAwait(false);
                        break;
                    case PlaybackAudioRecoveryEffectResult.ResetRequired:
                        return SetResetRequired(PlaybackAudioRecoveryResult.ResetRequired);
                    case PlaybackAudioRecoveryEffectResult.InvalidConfiguration:
                        return PlaybackAudioRecoveryResult.InvalidConfiguration;
                    case PlaybackAudioRecoveryEffectResult.Cancelled:
                        return SetResetRequired(PlaybackAudioRecoveryResult.Cancelled);
                    case PlaybackAudioRecoveryEffectResult.Disposed:
                        return PlaybackAudioRecoveryResult.Disposed;
                    case PlaybackAudioRecoveryEffectResult.Failure:
                        return Fault();
                    default:
                        return Fault();
                }
            }
            return SetResetRequired(PlaybackAudioRecoveryResult.AttemptLimitReached);
        }
        catch (OperationCanceledException)
        {
            return SetResetRequired(PlaybackAudioRecoveryResult.Cancelled);
        }
        catch
        {
            return Fault();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_operationCancellation, operation))
                    _operationCancellation = null;
            }
            operation.Dispose();
            _gate.Release();
        }
    }

    public async ValueTask<bool> ResetAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed) return false;
            _operationCancellation?.Cancel();
        }
        try { await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
        try
        {
            lock (_sync)
            {
                if (_isDisposed) return false;
                _state = PlaybackAudioRecoveryState.Ready;
                _attemptCount = 0;
            }
            return true;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _state = PlaybackAudioRecoveryState.Disposed;
            _operationCancellation?.Cancel();
        }
        _gate.Wait();
        _gate.Release();
    }

    private PlaybackAudioRecoveryResult Fault()
    {
        lock (_sync)
            if (!_isDisposed) _state = PlaybackAudioRecoveryState.Faulted;
        return PlaybackAudioRecoveryResult.Failure;
    }

    private PlaybackAudioRecoveryResult SetResetRequired(PlaybackAudioRecoveryResult result)
    {
        lock (_sync)
            if (!_isDisposed) _state = PlaybackAudioRecoveryState.ResetRequired;
        return result;
    }

    private static bool Validate(
        long videoGeneration,
        long audioGeneration,
        PlaybackAudioRecoveryOptions? options) =>
        videoGeneration >= 0 && audioGeneration >= 0 && options is not null &&
        options.MaximumAttempts is >= 1 and <= PlaybackAudioRecoveryOptions.MaximumAllowedAttempts &&
        options.RetryDelay >= TimeSpan.Zero &&
        options.RetryDelay <= PlaybackAudioRecoveryOptions.MaximumRetryDelay;
}

internal interface IPlaybackAudioRecoveryDelay
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class TaskPlaybackAudioRecoveryDelay : IPlaybackAudioRecoveryDelay
{
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}
