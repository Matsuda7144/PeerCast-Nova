using System.Diagnostics;

namespace PeerCast.Nova.Playback;

public enum PlaybackDecodedFrameWindowsPresentationState
{
    Accepting,
    Completing,
    Completed,
    Faulted,
    Disposed,
}

public enum PlaybackDecodedFrameWindowsPresentationCompletionResult
{
    Completed,
    AudioDrainTimedOut,
    AudioPaddingUnavailable,
    Cancelled,
    AlreadyCompleted,
    Faulted,
    Disposed,
}

public sealed record PlaybackDecodedFrameWindowsPresentationOptions
{
    public const int MaximumDecisionCountLimit = 16;

    public static readonly TimeSpan MaximumTotalWaitLimit = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumSingleWaitLimit = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumAudioDrainWaitLimit = TimeSpan.FromSeconds(5);

    public int MaximumDecisionCount { get; init; } = 4;

    public TimeSpan MaximumTotalWait { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan MinimumWait { get; init; } = TimeSpan.FromMilliseconds(1);

    public TimeSpan MaximumSingleWait { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaximumAudioDrainWait { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan AudioDrainPollInterval { get; init; } = TimeSpan.FromMilliseconds(10);
}

public sealed record PlaybackDecodedFrameWindowsPresentationSnapshot(
    PlaybackDecodedFrameWindowsPresentationState State,
    long VideoFrameSubmittedCount,
    long AudioFrameSubmittedCount,
    long VideoPresentedCount,
    long AudioWrittenCount,
    long LateVideoDropCount,
    long LateAudioDeliveryCount,
    long ResetCount,
    TimeSpan? FirstVideoPresentationTimestamp,
    TimeSpan? LastVideoPresentationTimestamp,
    TimeSpan? FirstAudioPresentationTimestamp,
    TimeSpan? LastAudioPresentationTimestamp,
    int CurrentOutputInFlight,
    int MaximumOutputInFlight,
    PlaybackWindowsAudioPaddingRead AudioPaddingAtCompletion);

internal interface IPlaybackDecodedFrameWindowsPresentationDelay
{
    ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

internal sealed class PlaybackDecodedFrameWindowsPresentationDelay :
    IPlaybackDecodedFrameWindowsPresentationDelay
{
    public async ValueTask DelayAsync(
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
}

/// <summary>
/// Adds timestamp pacing and completion draining to the existing decoded-frame
/// sink. Each awaited callback remains the only in-flight frame, and the actual
/// video/audio writes are dispatched to Nova's existing Windows output owner.
/// </summary>
public sealed class PlaybackDecodedFrameWindowsPresentationAdapter :
    IPlaybackDecodedFrameSink,
    IDisposable,
    IAsyncDisposable
{
    private const int MaximumConcurrentFrameCallbacks = 2;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPlaybackWindowsOutputDispatcher _dispatcher;
    private readonly IPlaybackDecodedFrameSink _outputSink;
    private readonly PlaybackAvTimingDecisionBoundary _timing;
    private readonly Func<long, long, CancellationToken, ValueTask<bool>> _resetOutput;
    private readonly Func<CancellationToken, ValueTask<PlaybackWindowsAudioPaddingRead>>
        _readAudioPadding;
    private readonly IPlaybackDecodedFrameWindowsPresentationDelay _delay;
    private readonly PlaybackDecodedFrameWindowsPresentationOptions _options;
    private long _videoGeneration;
    private long _audioGeneration;
    private PlaybackDecodedFrameWindowsPresentationState _state =
        PlaybackDecodedFrameWindowsPresentationState.Accepting;
    private long _videoFrameSubmittedCount;
    private long _audioFrameSubmittedCount;
    private long _videoPresentedCount;
    private long _audioWrittenCount;
    private long _lateVideoDropCount;
    private long _lateAudioDeliveryCount;
    private long _resetCount;
    private TimeSpan? _firstVideoPresentationTimestamp;
    private TimeSpan? _lastVideoPresentationTimestamp;
    private TimeSpan? _firstAudioPresentationTimestamp;
    private TimeSpan? _lastAudioPresentationTimestamp;
    private int _currentOutputInFlight;
    private int _maximumOutputInFlight;
    private int _activeFrameCallbacks;
    private PlaybackWindowsAudioPaddingRead _audioPaddingAtCompletion;
    private bool _disposed;

    public PlaybackDecodedFrameWindowsPresentationAdapter(
        IPlaybackWindowsOutputDispatcher dispatcher,
        IPlaybackDecodedFrameSink outputSink,
        PlaybackAvTimingDecisionBoundary timing,
        long videoGeneration,
        long audioGeneration,
        Func<long, long, CancellationToken, ValueTask<bool>> resetOutput,
        Func<CancellationToken, ValueTask<PlaybackWindowsAudioPaddingRead>>
            readAudioPadding,
        PlaybackDecodedFrameWindowsPresentationOptions? options = null)
        : this(
            dispatcher,
            outputSink,
            timing,
            videoGeneration,
            audioGeneration,
            resetOutput,
            readAudioPadding,
            new PlaybackDecodedFrameWindowsPresentationDelay(),
            options)
    {
    }

    internal PlaybackDecodedFrameWindowsPresentationAdapter(
        IPlaybackWindowsOutputDispatcher dispatcher,
        IPlaybackDecodedFrameSink outputSink,
        PlaybackAvTimingDecisionBoundary timing,
        long videoGeneration,
        long audioGeneration,
        Func<long, long, CancellationToken, ValueTask<bool>> resetOutput,
        Func<CancellationToken, ValueTask<PlaybackWindowsAudioPaddingRead>>
            readAudioPadding,
        IPlaybackDecodedFrameWindowsPresentationDelay delay,
        PlaybackDecodedFrameWindowsPresentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(outputSink);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(resetOutput);
        ArgumentNullException.ThrowIfNull(readAudioPadding);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentOutOfRangeException.ThrowIfNegative(videoGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(audioGeneration);

        _dispatcher = dispatcher;
        _outputSink = outputSink;
        _timing = timing;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _resetOutput = resetOutput;
        _readAudioPadding = readAudioPadding;
        _delay = delay;
        _options = CloneAndValidateOptions(options);
    }

    public PlaybackDecodedFrameWindowsPresentationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _videoFrameSubmittedCount,
                    _audioFrameSubmittedCount,
                    _videoPresentedCount,
                    _audioWrittenCount,
                    _lateVideoDropCount,
                    _lateAudioDeliveryCount,
                    _resetCount,
                    _firstVideoPresentationTimestamp,
                    _lastVideoPresentationTimestamp,
                    _firstAudioPresentationTimestamp,
                    _lastAudioPresentationTimestamp,
                    _currentOutputInFlight,
                    _maximumOutputInFlight,
                    _audioPaddingAtCompletion);
            }
        }
    }

    public ValueTask WriteAudioAsync(
        PlaybackDecodedAudioFrame frame,
        CancellationToken cancellationToken) =>
        WriteAsync(frame, cancellationToken);

    public ValueTask WriteVideoAsync(
        PlaybackDecodedVideoFrame frame,
        CancellationToken cancellationToken) =>
        WriteAsync(frame, cancellationToken);

    public async ValueTask<PlaybackDecodedFrameWindowsPresentationCompletionResult>
        CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            return SetCompletionFailure(
                PlaybackDecodedFrameWindowsPresentationCompletionResult.Cancelled);
        }

        try
        {
            lock (_sync)
            {
                if (_disposed || _state ==
                    PlaybackDecodedFrameWindowsPresentationState.Disposed)
                {
                    return PlaybackDecodedFrameWindowsPresentationCompletionResult.Disposed;
                }

                if (_state == PlaybackDecodedFrameWindowsPresentationState.Completed)
                {
                    return PlaybackDecodedFrameWindowsPresentationCompletionResult.AlreadyCompleted;
                }

                if (_state != PlaybackDecodedFrameWindowsPresentationState.Accepting)
                {
                    return PlaybackDecodedFrameWindowsPresentationCompletionResult.Faulted;
                }

                _state = PlaybackDecodedFrameWindowsPresentationState.Completing;
            }

            var drained = await DrainAudioAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _audioPaddingAtCompletion = drained.Padding;
                _state = drained.Result ==
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.Completed
                        ? PlaybackDecodedFrameWindowsPresentationState.Completed
                        : PlaybackDecodedFrameWindowsPresentationState.Faulted;
            }

            return drained.Result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (!await EnterAsync(CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                _disposed = true;
                _state = PlaybackDecodedFrameWindowsPresentationState.Disposed;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask WriteAsync<TFrame>(
        TFrame frame,
        CancellationToken cancellationToken)
        where TFrame : struct
    {
        if (Interlocked.Increment(ref _activeFrameCallbacks) >
            MaximumConcurrentFrameCallbacks)
        {
            Interlocked.Decrement(ref _activeFrameCallbacks);
            throw new InvalidOperationException(
                "Decoded-frame callbacks exceeded the one-frame bounded backpressure contract.");
        }

        if (!await EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _activeFrameCallbacks);
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_state != PlaybackDecodedFrameWindowsPresentationState.Accepting)
                {
                    throw new InvalidOperationException(
                        $"Decoded-frame presentation is not accepting frames: {_state}.");
                }
            }

            var mediaKind = GetMediaKind(frame);
            var timestamp = GetTimestamp(frame);
            var duration = GetDuration(frame);
            var sequence = GetSequence(frame);
            var discontinuity = GetDiscontinuity(frame);
            ValidateFrame(frame, timestamp, duration, sequence, discontinuity);

            if (discontinuity == PlaybackDecodedFrameDiscontinuityKind.DecoderReset)
            {
                await ResetAsync(cancellationToken).ConfigureAwait(false);
            }

            var generation = mediaKind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration
                : _audioGeneration;
            var shouldPresent = await PaceAsync(
                mediaKind,
                timestamp,
                duration,
                sequence,
                generation,
                cancellationToken).ConfigureAwait(false);
            if (!shouldPresent)
            {
                return;
            }

            IncrementSubmitted(mediaKind);
            await PresentOnOutputOwnerAsync(
                frame,
                mediaKind,
                cancellationToken).ConfigureAwait(false);
            RecordPresented(mediaKind, timestamp);
        }
        catch
        {
            lock (_sync)
            {
                if (_state != PlaybackDecodedFrameWindowsPresentationState.Disposed)
                {
                    _state = PlaybackDecodedFrameWindowsPresentationState.Faulted;
                }
            }

            throw;
        }
        finally
        {
            _gate.Release();
            Interlocked.Decrement(ref _activeFrameCallbacks);
        }
    }

    private async ValueTask<bool> PaceAsync(
        PlaybackPolicyMediaKind mediaKind,
        TimeSpan timestamp,
        TimeSpan duration,
        long sequence,
        long generation,
        CancellationToken cancellationToken)
    {
        var item = new PlaybackAvTimingItem(
            mediaKind,
            PlaybackPolicyTime.Known(timestamp),
            PlaybackPolicyTime.Known(duration),
            sequence,
            PlaybackDecodedFrameDiscontinuityKind.None,
            generation);
        var totalWait = TimeSpan.Zero;
        for (var decisionIndex = 0;
                decisionIndex < _options.MaximumDecisionCount;
                decisionIndex++)
        {
            var decision = await _timing.DecideAsync(item, cancellationToken)
                .ConfigureAwait(false);
            if (!Enum.IsDefined(decision.Result) || decision.WaitDuration < TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    "The presentation timing boundary returned an invalid decision.");
            }

            switch (decision.Result)
            {
                case PlaybackAvTimingDecisionResult.ProcessNow:
                    return true;
                case PlaybackAvTimingDecisionResult.Wait:
                    if (decision.WaitDuration < _options.MinimumWait ||
                        decision.WaitDuration > _options.MaximumSingleWait ||
                        decisionIndex + 1 >= _options.MaximumDecisionCount)
                    {
                        throw new InvalidOperationException(
                            "The presentation timing wait exceeded its bounded decision contract.");
                    }

                    totalWait = checked(totalWait + decision.WaitDuration);
                    if (totalWait > _options.MaximumTotalWait)
                    {
                        throw new InvalidOperationException(
                            "The presentation timing wait exceeded its bounded total duration.");
                    }

                    await _delay.DelayAsync(decision.WaitDuration, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case PlaybackAvTimingDecisionResult.VideoLate
                    when mediaKind == PlaybackPolicyMediaKind.Video:
                    IncrementLateVideoDrop();
                    return false;
                case PlaybackAvTimingDecisionResult.AudioLate
                    when mediaKind == PlaybackPolicyMediaKind.Audio:
                    IncrementLateAudioDelivery();
                    return true;
                case PlaybackAvTimingDecisionResult.Cancelled:
                    throw new OperationCanceledException(cancellationToken);
                default:
                    throw new InvalidOperationException(
                        $"The presentation timing boundary rejected the frame: {decision.Result}.");
            }
        }

        throw new InvalidOperationException(
            "The presentation timing decision limit was reached without a presentation decision.");
    }

    private async ValueTask ResetAsync(CancellationToken cancellationToken)
    {
        long nextVideoGeneration;
        long nextAudioGeneration;
        try
        {
            nextVideoGeneration = checked(_videoGeneration + 1);
            nextAudioGeneration = checked(_audioGeneration + 1);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "The presentation generation cannot be advanced safely.",
                exception);
        }

        if (!await _resetOutput(
                nextVideoGeneration,
                nextAudioGeneration,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The existing Windows presentation output rejected a decoder reset.");
        }

        var reset = await _timing.ResetAsync(
                nextVideoGeneration,
                nextAudioGeneration,
                cancellationToken)
            .ConfigureAwait(false);
        if (reset != PlaybackAvTimingResetResult.Reset)
        {
            throw new InvalidOperationException(
                $"The existing A/V timing boundary rejected a decoder reset: {reset}.");
        }

        _videoGeneration = nextVideoGeneration;
        _audioGeneration = nextAudioGeneration;
        lock (_sync)
        {
            _resetCount = checked(_resetCount + 1);
        }
    }

    private async ValueTask PresentOnOutputOwnerAsync<TFrame>(
        TFrame frame,
        PlaybackPolicyMediaKind mediaKind,
        CancellationToken cancellationToken)
        where TFrame : struct
    {
        var inFlight = Interlocked.Increment(ref _currentOutputInFlight);
        UpdateMaximumOutputInFlight(inFlight);
        try
        {
            var invocation = await _dispatcher.InvokeAsync(
                async outputToken =>
                {
                    if (frame is PlaybackDecodedVideoFrame video)
                    {
                        await _outputSink.WriteVideoAsync(video, outputToken)
                            .ConfigureAwait(false);
                    }
                    else if (frame is PlaybackDecodedAudioFrame audio)
                    {
                        await _outputSink.WriteAudioAsync(audio, outputToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "The decoded-frame presentation model is unsupported.");
                    }

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (invocation.Result == PlaybackWindowsOutputDispatcherInvocationResult.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (invocation.Result != PlaybackWindowsOutputDispatcherInvocationResult.Completed ||
                !invocation.Value)
            {
                throw new InvalidOperationException(
                    $"The Windows output owner rejected the {mediaKind} frame: {invocation.Result}.");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _currentOutputInFlight);
        }
    }

    private async ValueTask<(
        PlaybackDecodedFrameWindowsPresentationCompletionResult Result,
        PlaybackWindowsAudioPaddingRead Padding)> DrainAudioAsync(
            CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) <= _options.MaximumAudioDrainWait)
        {
            PlaybackWindowsAudioPaddingRead padding;
            try
            {
                padding = await _readAudioPadding(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.Cancelled,
                    default);
            }
            catch
            {
                return (
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.AudioPaddingUnavailable,
                    default);
            }

            if (!Enum.IsDefined(padding.Result) || padding.PaddingFrameCount < 0)
            {
                return (
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.AudioPaddingUnavailable,
                    padding);
            }

            if (padding.Result == PlaybackWindowsAudioPaddingReadResult.Available &&
                padding.PaddingFrameCount == 0)
            {
                return (
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.Completed,
                    padding);
            }

            if (padding.Result == PlaybackWindowsAudioPaddingReadResult.NotStarted &&
                _audioFrameSubmittedCount == 0)
            {
                return (
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.Completed,
                    padding);
            }

            if (padding.Result is not PlaybackWindowsAudioPaddingReadResult.Available and
                not PlaybackWindowsAudioPaddingReadResult.Busy)
            {
                return (
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.AudioPaddingUnavailable,
                    padding);
            }

            try
            {
                await _delay.DelayAsync(
                        _options.AudioDrainPollInterval,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (
                    PlaybackDecodedFrameWindowsPresentationCompletionResult.Cancelled,
                    padding);
            }
        }

        return (
            PlaybackDecodedFrameWindowsPresentationCompletionResult.AudioDrainTimedOut,
            default);
    }

    private void IncrementSubmitted(PlaybackPolicyMediaKind mediaKind)
    {
        lock (_sync)
        {
            if (mediaKind == PlaybackPolicyMediaKind.Video)
            {
                _videoFrameSubmittedCount = checked(_videoFrameSubmittedCount + 1);
            }
            else
            {
                _audioFrameSubmittedCount = checked(_audioFrameSubmittedCount + 1);
            }
        }
    }

    private void RecordPresented(PlaybackPolicyMediaKind mediaKind, TimeSpan timestamp)
    {
        lock (_sync)
        {
            if (mediaKind == PlaybackPolicyMediaKind.Video)
            {
                _videoPresentedCount = checked(_videoPresentedCount + 1);
                _firstVideoPresentationTimestamp ??= timestamp;
                _lastVideoPresentationTimestamp = timestamp;
            }
            else
            {
                _audioWrittenCount = checked(_audioWrittenCount + 1);
                _firstAudioPresentationTimestamp ??= timestamp;
                _lastAudioPresentationTimestamp = timestamp;
            }
        }
    }

    private void IncrementLateVideoDrop()
    {
        lock (_sync)
        {
            _lateVideoDropCount = checked(_lateVideoDropCount + 1);
        }
    }

    private void IncrementLateAudioDelivery()
    {
        lock (_sync)
        {
            _lateAudioDeliveryCount = checked(_lateAudioDeliveryCount + 1);
        }
    }

    private void UpdateMaximumOutputInFlight(int current)
    {
        lock (_sync)
        {
            _maximumOutputInFlight = Math.Max(_maximumOutputInFlight, current);
        }
    }

    private PlaybackDecodedFrameWindowsPresentationCompletionResult SetCompletionFailure(
        PlaybackDecodedFrameWindowsPresentationCompletionResult result)
    {
        lock (_sync)
        {
            if (_state != PlaybackDecodedFrameWindowsPresentationState.Disposed)
            {
                _state = PlaybackDecodedFrameWindowsPresentationState.Faulted;
            }
        }

        return result;
    }

    private async ValueTask<bool> EnterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static PlaybackDecodedFrameWindowsPresentationOptions CloneAndValidateOptions(
        PlaybackDecodedFrameWindowsPresentationOptions? options)
    {
        options ??= new();
        if (options.MaximumDecisionCount is < 1 or >
                PlaybackDecodedFrameWindowsPresentationOptions.MaximumDecisionCountLimit ||
            options.MaximumTotalWait <= TimeSpan.Zero ||
            options.MaximumTotalWait >
                PlaybackDecodedFrameWindowsPresentationOptions.MaximumTotalWaitLimit ||
            options.MinimumWait <= TimeSpan.Zero ||
            options.MinimumWait > options.MaximumSingleWait ||
            options.MaximumSingleWait <= TimeSpan.Zero ||
            options.MaximumSingleWait >
                PlaybackDecodedFrameWindowsPresentationOptions.MaximumSingleWaitLimit ||
            options.MaximumAudioDrainWait <= TimeSpan.Zero ||
            options.MaximumAudioDrainWait >
                PlaybackDecodedFrameWindowsPresentationOptions.MaximumAudioDrainWaitLimit ||
            options.AudioDrainPollInterval <= TimeSpan.Zero ||
            options.AudioDrainPollInterval > TimeSpan.FromMilliseconds(100))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        return options with { };
    }

    private static PlaybackPolicyMediaKind GetMediaKind<TFrame>(TFrame frame)
        where TFrame : struct => frame switch
        {
            PlaybackDecodedAudioFrame => PlaybackPolicyMediaKind.Audio,
            PlaybackDecodedVideoFrame => PlaybackPolicyMediaKind.Video,
            _ => throw new InvalidOperationException(
                "The decoded-frame presentation model is unsupported."),
        };

    private static TimeSpan GetTimestamp<TFrame>(TFrame frame)
        where TFrame : struct => frame switch
        {
            PlaybackDecodedAudioFrame audio => audio.Timestamp,
            PlaybackDecodedVideoFrame video => video.Timestamp,
            _ => default,
        };

    private static TimeSpan GetDuration<TFrame>(TFrame frame)
        where TFrame : struct => frame switch
        {
            PlaybackDecodedAudioFrame audio => audio.Duration,
            PlaybackDecodedVideoFrame video => video.Duration,
            _ => default,
        };

    private static long GetSequence<TFrame>(TFrame frame)
        where TFrame : struct => frame switch
        {
            PlaybackDecodedAudioFrame audio => audio.Sequence,
            PlaybackDecodedVideoFrame video => video.Sequence,
            _ => -1,
        };

    private static PlaybackDecodedFrameDiscontinuityKind GetDiscontinuity<TFrame>(TFrame frame)
        where TFrame : struct => frame switch
        {
            PlaybackDecodedAudioFrame audio => audio.Discontinuity,
            PlaybackDecodedVideoFrame video => video.Discontinuity,
            _ => (PlaybackDecodedFrameDiscontinuityKind)(-1),
        };

    private static void ValidateFrame<TFrame>(
        TFrame frame,
        TimeSpan timestamp,
        TimeSpan duration,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity)
        where TFrame : struct
    {
        if (timestamp < TimeSpan.Zero || duration <= TimeSpan.Zero ||
            duration > PlaybackDecodedVideoFrame.MaximumDuration || sequence < 0 ||
            !Enum.IsDefined(discontinuity))
        {
            throw new InvalidOperationException(
                "The decoded frame timing or discontinuity is invalid for presentation.");
        }

        if (frame is PlaybackDecodedVideoFrame video &&
            (!Enum.IsDefined(video.PixelFormat) ||
                video.PixelFormat != PlaybackDecodedVideoPixelFormat.Bgra32 ||
                video.Width <= 0 || video.Height <= 0 ||
                video.Stride != (long)video.Width * 4 ||
                video.Payload.Length != (long)video.Stride * video.Height))
        {
            throw new InvalidOperationException(
                "The decoded video frame is not a valid packed BGRA32 frame.");
        }

        if (frame is PlaybackDecodedAudioFrame audio &&
            (!Enum.IsDefined(audio.SampleFormat) ||
                audio.SampleFormat is not (
                    PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
                    PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved) ||
                audio.SampleRate is < PlaybackDecodedAudioFrame.MinimumSampleRate or
                    > PlaybackDecodedAudioFrame.MaximumSampleRate ||
                audio.ChannelCount is < PlaybackDecodedAudioFrame.MinimumChannelCount or
                    > PlaybackDecodedAudioFrame.MaximumChannelCount ||
                audio.SampleCount <= 0))
        {
            throw new InvalidOperationException(
                "The decoded audio frame does not use a supported interleaved PCM format.");
        }
    }
}
