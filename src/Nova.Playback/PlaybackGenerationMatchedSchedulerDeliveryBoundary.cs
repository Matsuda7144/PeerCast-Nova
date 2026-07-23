namespace PeerCast.Nova.Playback;

public enum PlaybackGenerationMatchedSchedulerDeliveryState
{
    Accepting,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackGenerationMatchedSchedulerDeliveryResult
{
    None,
    VideoDelivered,
    AudioDelivered,
    VideoDropped,
    Empty,
    RetainedWait,
    RetainedWouldBlock,
    RetainedInitializationRequired,
    RetainedBindingRequired,
    RetainedResizeRequired,
    RetainedAudioRecoveryRequired,
    ClockNotStarted,
    ResetRequired,
    InvalidItem,
    ContextUnavailable,
    ConversionFailed,
    OutputFailure,
    AlreadyRunning,
    Cancelled,
    Disposed,
    Faulted,
}

public enum PlaybackGenerationMatchedSchedulerResetResult
{
    Reset,
    Cancelled,
    Disposed,
    Faulted,
    Failure,
}

public enum PlaybackGenerationMatchedSchedulerReservationCompletion
{
    None,
    Retained,
    Delivered,
    Dropped,
}

public readonly record struct PlaybackGenerationMatchedSchedulerDeliveryObservation(
    long Sequence,
    PlaybackPolicyMediaKind MediaKind,
    bool HasPresentationTimestampOffset,
    long PresentationTimestampOffsetTicks,
    bool HasTimingDecision,
    PlaybackAvTimingDecisionResult TimingResult,
    long TimingWaitTicks,
    PlaybackGenerationMatchedSchedulerDeliveryResult Result,
    PlaybackGenerationMatchedSchedulerReservationCompletion Completion);

public readonly record struct PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot(
    int Count,
    bool Overflowed,
    PlaybackGenerationMatchedSchedulerDeliveryObservation First,
    PlaybackGenerationMatchedSchedulerDeliveryObservation Second,
    PlaybackGenerationMatchedSchedulerDeliveryObservation Third,
    PlaybackGenerationMatchedSchedulerDeliveryObservation Fourth)
{
    public const int MaximumObservationCount = 4;

    public PlaybackGenerationMatchedSchedulerDeliveryObservation GetObservation(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index switch
        {
            0 => First,
            1 => Second,
            2 => Third,
            3 => Fourth,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }
}

public sealed record PlaybackGenerationMatchedSchedulerDeliverySnapshot(
    PlaybackGenerationMatchedSchedulerDeliveryState State,
    PlaybackGenerationMatchedSchedulerDeliveryResult Result,
    int DecoderGeneration,
    int DiscontinuityGeneration,
    bool PreferVideoNext,
    bool DecoderResetPending,
    bool HasCompletedSequence,
    long LastCompletedSequence,
    bool HasActiveDelivery,
    PlaybackDecodedCompletionSchedulerIngressSnapshot Ingress)
{
    public PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot DeliveryTrace { get; init; }
}

internal enum PlaybackGenerationMatchedCanonicalDeliveryResult
{
    VideoProcessed,
    AudioProcessed,
    WouldBlock,
    InitializationRequired,
    BindingRequired,
    ResizeRequired,
    DeviceLost,
    InvalidItem,
    ContextUnavailable,
    ConversionFailed,
    Cancelled,
    Disposed,
    Failure,
}

internal enum PlaybackGenerationMatchedOutputResetResult
{
    Reset,
    Cancelled,
    Disposed,
    Failure,
    PartialResetFault,
}

internal interface IPlaybackGenerationMatchedSchedulerDeliveryOperations : IDisposable
{
    bool ValidateContextRole(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind);

    ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item,
        CancellationToken cancellationToken);

    ValueTask<PlaybackAvTimingResetResult> ResetTimingAsync(
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackGenerationMatchedOutputResetResult> ResetOutputAsync(
        long generation,
        CancellationToken cancellationToken);

    ValueTask<PlaybackGenerationMatchedCanonicalDeliveryResult> DeliverAsync(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        long generation,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackGenerationMatchedSchedulerDeliveryOperations :
    IPlaybackGenerationMatchedSchedulerDeliveryOperations
{
    private readonly PlaybackAvTimingDecisionBoundary _timing;
    private readonly PlaybackWindowsOutputBufferProcessingBoundary _output;
    private readonly PlaybackDecoderFfmpegDecodedOutputAdapter _adapter = new();
    private readonly PlaybackDecoderFfmpegManagedFormatConversionBoundary _conversion = new();
    private int _isDisposed;

    internal PlaybackGenerationMatchedSchedulerDeliveryOperations(
        PlaybackAvTimingDecisionBoundary timing,
        PlaybackWindowsOutputBufferProcessingBoundary output)
    {
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(output);
        _timing = timing;
        _output = output;
    }

    public bool ValidateContextRole(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackBoundedDemuxMediaKind mediaKind)
    {
        if (Volatile.Read(ref _isDisposed) != 0 || nativeSession.IsDisposed ||
            !Enum.IsDefined(mediaKind))
        {
            return false;
        }

        try
        {
            var context = mediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? nativeSession.VideoContext
                : nativeSession.AudioContext;
            var expectedCodec = mediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? PlaybackDecoderFfmpegCodecId.H264
                : PlaybackDecoderFfmpegCodecId.Aac;
            return !context.IsDisposed && context.CodecId == expectedCodec;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item,
        CancellationToken cancellationToken) =>
        _timing.DecideAsync(item, cancellationToken);

    public ValueTask<PlaybackAvTimingResetResult> ResetTimingAsync(
        long generation,
        CancellationToken cancellationToken) =>
        _timing.ResetAsync(generation, generation, cancellationToken);

    public async ValueTask<PlaybackGenerationMatchedOutputResetResult> ResetOutputAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        var audio = await InvokeResetAsync(video: false, generation, cancellationToken)
            .ConfigureAwait(false);
        if (!IsReset(audio))
        {
            return MapReset(audio);
        }

        var video = await InvokeResetAsync(video: true, generation, cancellationToken)
            .ConfigureAwait(false);
        return IsReset(video)
            ? PlaybackGenerationMatchedOutputResetResult.Reset
            : PlaybackGenerationMatchedOutputResetResult.PartialResetFault;
    }

    public async ValueTask<PlaybackGenerationMatchedCanonicalDeliveryResult> DeliverAsync(
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        long generation,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.Disposed;
        }

        PlaybackDecoderFfmpegContextLease context;
        try
        {
            context = frame.Metadata.MediaKind == PlaybackDecoderFfmpegFrameMediaKind.Video
                ? nativeSession.VideoContext
                : nativeSession.AudioContext;
        }
        catch (ObjectDisposedException)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable;
        }

        if (context.IsDisposed)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable;
        }

        var consumer = new OutputConsumer(_output, generation);
        try
        {
            var adapted = await _adapter.AdaptAsync(
                context,
                frame,
                sequence,
                discontinuity,
                consumer,
                cancellationToken).ConfigureAwait(false);
            if (adapted == PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConversionRequired)
            {
                var converted = await _conversion.ConvertAsync(
                    context,
                    frame,
                    sequence,
                    discontinuity,
                    consumer,
                    cancellationToken).ConfigureAwait(false);
                return Map(converted, consumer);
            }

            return Map(adapted, consumer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled;
        }
        catch
        {
            return PlaybackGenerationMatchedCanonicalDeliveryResult.Failure;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _timing.Dispose();
        }
    }

    private async ValueTask<PlaybackWindowsOutputBufferResetResult> InvokeResetAsync(
        bool video,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            return video
                ? await _output.ResetVideoAsync(generation, cancellationToken).ConfigureAwait(false)
                : await _output.ResetAudioAsync(generation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlaybackWindowsOutputBufferResetResult.Cancelled;
        }
        catch
        {
            return PlaybackWindowsOutputBufferResetResult.Failure;
        }
    }

    private static bool IsReset(PlaybackWindowsOutputBufferResetResult result) =>
        result is PlaybackWindowsOutputBufferResetResult.Reset or
            PlaybackWindowsOutputBufferResetResult.DeviceLost;

    private static PlaybackGenerationMatchedOutputResetResult MapReset(
        PlaybackWindowsOutputBufferResetResult result) => result switch
        {
            PlaybackWindowsOutputBufferResetResult.Cancelled =>
                PlaybackGenerationMatchedOutputResetResult.Cancelled,
            PlaybackWindowsOutputBufferResetResult.Disposed =>
                PlaybackGenerationMatchedOutputResetResult.Disposed,
            _ => PlaybackGenerationMatchedOutputResetResult.Failure,
        };

    private static PlaybackGenerationMatchedCanonicalDeliveryResult Map(
        PlaybackDecoderFfmpegDecodedOutputAdapterResult result,
        OutputConsumer consumer) => result switch
        {
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.Converted when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ConsumerRejected when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.ContextUnavailable =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable,
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.DisposedFrame or
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.InvalidFrame or
            PlaybackDecoderFfmpegDecodedOutputAdapterResult.AllocationFailed =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.InvalidItem,
            _ => PlaybackGenerationMatchedCanonicalDeliveryResult.Failure,
        };

    private static PlaybackGenerationMatchedCanonicalDeliveryResult Map(
        PlaybackDecoderFfmpegManagedFormatConversionResult result,
        OutputConsumer consumer) => result switch
        {
            PlaybackDecoderFfmpegManagedFormatConversionResult.Converted when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConsumerRejected when consumer.WasInvoked =>
                consumer.Result,
            PlaybackDecoderFfmpegManagedFormatConversionResult.ContextUnavailable =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable,
            PlaybackDecoderFfmpegManagedFormatConversionResult.DisposedFrame or
            PlaybackDecoderFfmpegManagedFormatConversionResult.InvalidFrame or
            PlaybackDecoderFfmpegManagedFormatConversionResult.UnsupportedFormat =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.InvalidItem,
            PlaybackDecoderFfmpegManagedFormatConversionResult.AllocationFailed or
            PlaybackDecoderFfmpegManagedFormatConversionResult.ConversionFailed =>
                PlaybackGenerationMatchedCanonicalDeliveryResult.ConversionFailed,
            _ => PlaybackGenerationMatchedCanonicalDeliveryResult.Failure,
        };

    private sealed class OutputConsumer(
        PlaybackWindowsOutputBufferProcessingBoundary output,
        long generation) : IPlaybackDecoderFfmpegDecodedOutputAsyncConsumer
    {
        public bool WasInvoked { get; private set; }

        public PlaybackGenerationMatchedCanonicalDeliveryResult Result { get; private set; } =
            PlaybackGenerationMatchedCanonicalDeliveryResult.Failure;

        public async ValueTask<bool> ConsumeAudioAsync(
            PlaybackDecodedAudioFrame frame,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            var result = await output.ProcessAudioAsync(
                new(
                    frame.SampleFormat,
                    frame.SampleRate,
                    frame.ChannelCount,
                    frame.SampleCount,
                    frame.Payload,
                    generation),
                cancellationToken).ConfigureAwait(false);
            Result = MapOutput(result, video: false);
            return Result == PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed;
        }

        public async ValueTask<bool> ConsumeVideoAsync(
            PlaybackDecodedVideoFrame frame,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            var result = await output.ProcessVideoAsync(
                new(
                    frame.PixelFormat,
                    frame.Width,
                    frame.Height,
                    frame.Stride,
                    frame.Payload,
                    generation),
                cancellationToken).ConfigureAwait(false);
            Result = MapOutput(result, video: true);
            return Result == PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed;
        }

        private static PlaybackGenerationMatchedCanonicalDeliveryResult MapOutput(
            PlaybackWindowsOutputBufferProcessingResult result,
            bool video) => result switch
            {
                PlaybackWindowsOutputBufferProcessingResult.Processed => video
                    ? PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed
                    : PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed,
                PlaybackWindowsOutputBufferProcessingResult.WouldBlock =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.WouldBlock,
                PlaybackWindowsOutputBufferProcessingResult.InitializationRequired =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.InitializationRequired,
                PlaybackWindowsOutputBufferProcessingResult.BindingRequired =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.BindingRequired,
                PlaybackWindowsOutputBufferProcessingResult.ResizeRequired =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.ResizeRequired,
                PlaybackWindowsOutputBufferProcessingResult.DeviceLost =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.DeviceLost,
                PlaybackWindowsOutputBufferProcessingResult.Cancelled =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled,
                PlaybackWindowsOutputBufferProcessingResult.Disposed =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.Disposed,
                PlaybackWindowsOutputBufferProcessingResult.FormatUnsupported or
                PlaybackWindowsOutputBufferProcessingResult.InvalidConfiguration =>
                    PlaybackGenerationMatchedCanonicalDeliveryResult.InvalidItem,
                _ => PlaybackGenerationMatchedCanonicalDeliveryResult.Failure,
            };
    }
}

/// <summary>
/// Finite generation owner for one-shot decoded completion ingress and one reserved
/// scheduler delivery. It owns the scheduler and source-bound decode lifetime through
/// the ingress boundary, but borrows device-wide Windows output resources.
/// </summary>
public sealed class PlaybackGenerationMatchedSchedulerDeliveryBoundary :
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private readonly PlaybackBoundedDemuxSession _sourceSession;
    private readonly PlaybackSourceBoundOneFrameDecodeBoundary _decodeBoundary;
    private readonly PlaybackBoundedFlvNativeDecoderSession _nativeSession;
    private readonly BoundedPlaybackScheduler _scheduler;
    private readonly PlaybackDecodedCompletionSchedulerIngressBoundary _ingress;
    private readonly IPlaybackGenerationMatchedSchedulerDeliveryOperations _operations;
    private readonly int _decoderGeneration;
    private readonly int _discontinuityGeneration;
    private readonly long _initialSchedulerSequence;
    private readonly PlaybackGenerationMatchedSchedulerDeliveryObservation[]? _deliveryObservations;

    private CancellationTokenSource? _activeCancellation;
    private PlaybackGenerationMatchedSchedulerDeliveryState _state =
        PlaybackGenerationMatchedSchedulerDeliveryState.Accepting;
    private PlaybackGenerationMatchedSchedulerDeliveryResult _result =
        PlaybackGenerationMatchedSchedulerDeliveryResult.None;
    private bool _preferVideoNext = true;
    private bool _decoderResetPending;
    private bool _hasCompletedSequence;
    private long _lastCompletedSequence;
    private bool _hasActiveDelivery;
    private int _deliveryObservationCount;
    private bool _deliveryObservationOverflowed;
    private bool _hasDeliveryObservationTimestampAnchor;
    private long _deliveryObservationTimestampAnchorTicks;
    private bool _isDisposed;

    public PlaybackGenerationMatchedSchedulerDeliveryBoundary(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackSourceBoundOneFrameDecodeBoundary decodeBoundary,
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        int decoderGeneration,
        int discontinuityGeneration,
        BoundedPlaybackScheduler scheduler,
        PlaybackAvTimingDecisionBoundary timing,
        PlaybackWindowsOutputBufferProcessingBoundary output,
        long initialSchedulerSequence = 0,
        bool captureDeliveryObservations = false)
        : this(
            sourceSession,
            decodeBoundary,
            nativeSession,
            decoderGeneration,
            discontinuityGeneration,
            scheduler,
            initialSchedulerSequence,
            new PlaybackGenerationMatchedSchedulerDeliveryOperations(timing, output),
            captureDeliveryObservations)
    {
    }

    internal PlaybackGenerationMatchedSchedulerDeliveryBoundary(
        PlaybackBoundedDemuxSession sourceSession,
        PlaybackSourceBoundOneFrameDecodeBoundary decodeBoundary,
        PlaybackBoundedFlvNativeDecoderSession nativeSession,
        int decoderGeneration,
        int discontinuityGeneration,
        BoundedPlaybackScheduler scheduler,
        long initialSchedulerSequence,
        IPlaybackGenerationMatchedSchedulerDeliveryOperations operations,
        bool captureDeliveryObservations = false)
    {
        ArgumentNullException.ThrowIfNull(sourceSession);
        ArgumentNullException.ThrowIfNull(decodeBoundary);
        ArgumentNullException.ThrowIfNull(nativeSession);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentOutOfRangeException.ThrowIfNegative(decoderGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(discontinuityGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(initialSchedulerSequence);

        var decode = decodeBoundary.Snapshot;
        if (sourceSession.IsDisposed || decodeBoundary.IsDisposed || nativeSession.IsDisposed ||
            scheduler.IsDisposed || !decodeBoundary.ReferencesSourceSession(sourceSession) ||
            !nativeSession.ReferencesSourceSession(sourceSession) ||
            decode.DecoderGeneration != decoderGeneration ||
            decode.DiscontinuityGeneration != discontinuityGeneration ||
            discontinuityGeneration > 0 && initialSchedulerSequence != 0)
        {
            operations.Dispose();
            throw new ArgumentException(
                "The generation delivery boundary requires one exact live source-bound decoder generation.",
                nameof(decodeBoundary));
        }

        if (!operations.ValidateContextRole(nativeSession, PlaybackBoundedDemuxMediaKind.Video) ||
            !operations.ValidateContextRole(nativeSession, PlaybackBoundedDemuxMediaKind.Audio))
        {
            operations.Dispose();
            throw new ArgumentException(
                "The generation delivery boundary requires matching live H.264 and AAC contexts.",
                nameof(nativeSession));
        }

        _sourceSession = sourceSession;
        _decodeBoundary = decodeBoundary;
        _nativeSession = nativeSession;
        _decoderGeneration = decoderGeneration;
        _discontinuityGeneration = discontinuityGeneration;
        _scheduler = scheduler;
        _initialSchedulerSequence = initialSchedulerSequence;
        _operations = operations;
        _decoderResetPending = discontinuityGeneration > 0;
        _deliveryObservations = captureDeliveryObservations
            ? new PlaybackGenerationMatchedSchedulerDeliveryObservation[
                PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot.MaximumObservationCount]
            : null;
        try
        {
            _ingress = new(
                sourceSession,
                decoderGeneration,
                discontinuityGeneration,
                scheduler,
                decodeBoundary,
                initialSchedulerSequence);
        }
        catch
        {
            operations.Dispose();
            throw;
        }
    }

    public PlaybackGenerationMatchedSchedulerDeliverySnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _decoderGeneration,
                    _discontinuityGeneration,
                    _preferVideoNext,
                    _decoderResetPending,
                    _hasCompletedSequence,
                    _lastCompletedSequence,
                    _hasActiveDelivery,
                    _ingress.Snapshot)
                {
                    DeliveryTrace = CreateDeliveryTraceSnapshotLocked(),
                };
            }
        }
    }

    public PlaybackDecodedCompletionSchedulerIngressResult Transfer(
        PlaybackSourceBoundDecodedFrameCompletion? completion,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                completion?.Dispose();
                return PlaybackDecodedCompletionSchedulerIngressResult.Disposed;
            }

            if (_state == PlaybackGenerationMatchedSchedulerDeliveryState.Faulted)
            {
                completion?.Dispose();
                return PlaybackDecodedCompletionSchedulerIngressResult.Faulted;
            }

            if (_hasActiveDelivery)
            {
                return PlaybackDecodedCompletionSchedulerIngressResult.Cancelled;
            }

            var result = _ingress.Transfer(completion, cancellationToken);
            if (result is PlaybackDecodedCompletionSchedulerIngressResult.InvalidCompletion or
                PlaybackDecodedCompletionSchedulerIngressResult.SequenceExhausted or
                PlaybackDecodedCompletionSchedulerIngressResult.EnqueueRejected or
                PlaybackDecodedCompletionSchedulerIngressResult.Faulted)
            {
                _state = PlaybackGenerationMatchedSchedulerDeliveryState.Faulted;
                _result = PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted;
            }
            else if (result == PlaybackDecodedCompletionSchedulerIngressResult.Disposed)
            {
                _state = PlaybackGenerationMatchedSchedulerDeliveryState.Disposed;
                _result = PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed;
                _isDisposed = true;
            }

            return result;
        }
    }

    public async ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> DeliverOneAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResultLocked(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed);
            }

            if (_state == PlaybackGenerationMatchedSchedulerDeliveryState.Faulted)
            {
                return SetResultLocked(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
            }

            if (_state == PlaybackGenerationMatchedSchedulerDeliveryState.ResetRequired)
            {
                return SetResultLocked(PlaybackGenerationMatchedSchedulerDeliveryResult.ResetRequired);
            }
        }

        bool entered;
        try
        {
            entered = await _deliveryGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled);
        }

        if (!entered)
        {
            return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.AlreadyRunning);
        }

        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_isDisposed)
            {
                _deliveryGate.Release();
                return SetResultLocked(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed);
            }

            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = operation;
            _hasActiveDelivery = true;
        }

        PlaybackSchedulerOutputReservation? reservation = null;
        var completion = PlaybackSchedulerOutputCompletion.Retained;
        var completed = false;
        var hasObservationReservation = false;
        var observationTimingItem = default(PlaybackAvTimingItem);
        var hasObservationTimingDecision = false;
        var observationTimingDecision = default(PlaybackAvTimingDecision);
        try
        {
            var preferred = GetPreferredKind();
            reservation = await _scheduler.ReserveOutputAsync(preferred, operation.Token)
                .ConfigureAwait(false);
            if (reservation is null)
            {
                return MapMissingReservation(operation.Token);
            }

            if (!ValidateReservation(reservation, out var mediaKind, out var timingItem))
            {
                return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.InvalidItem);
            }

            hasObservationReservation = true;
            observationTimingItem = timingItem;
            var effectiveDiscontinuity = reservation.Discontinuity;
            if (effectiveDiscontinuity == PlaybackDecodedFrameDiscontinuityKind.DecoderReset)
            {
                var reset = await AdmitDecoderResetAsync(reservation, operation.Token)
                    .ConfigureAwait(false);
                if (reset != PlaybackGenerationMatchedSchedulerDeliveryResult.None)
                {
                    return reset;
                }

                effectiveDiscontinuity = PlaybackDecodedFrameDiscontinuityKind.None;
                timingItem = timingItem with { Discontinuity = effectiveDiscontinuity };
                observationTimingItem = timingItem;
            }
            else if (IsDecoderResetPending())
            {
                return SetResetRequired();
            }

            var decision = await _operations.DecideAsync(timingItem, operation.Token)
                .ConfigureAwait(false);
            hasObservationTimingDecision = true;
            observationTimingDecision = decision;
            if (!Enum.IsDefined(decision.Result))
            {
                return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
            }

            switch (decision.Result)
            {
                case PlaybackAvTimingDecisionResult.ProcessNow:
                    break;
                case PlaybackAvTimingDecisionResult.Wait:
                    return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWait);
                case PlaybackAvTimingDecisionResult.VideoLate:
                    if (mediaKind != PlaybackBoundedDemuxMediaKind.Video)
                    {
                        return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.InvalidItem);
                    }

                    completion = PlaybackSchedulerOutputCompletion.Dropped;
                    reservation.Complete(completion);
                    completed = true;
                    CommitCompletedSequence(reservation.Sequence, preferVideoNext: false);
                    return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDropped);
                case PlaybackAvTimingDecisionResult.AudioLate:
                    return SetResult(
                        PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedAudioRecoveryRequired);
                case PlaybackAvTimingDecisionResult.ClockNotStarted:
                    return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.ClockNotStarted);
                case PlaybackAvTimingDecisionResult.DiscontinuityResetRequired:
                case PlaybackAvTimingDecisionResult.GenerationMismatch:
                    return SetResetRequired();
                case PlaybackAvTimingDecisionResult.Cancelled:
                    return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled);
                case PlaybackAvTimingDecisionResult.Disposed:
                    return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed);
                case PlaybackAvTimingDecisionResult.WaitLimitExceeded:
                case PlaybackAvTimingDecisionResult.InvalidTimestamp:
                case PlaybackAvTimingDecisionResult.InvalidConfiguration:
                    return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.InvalidItem);
                default:
                    return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
            }

            var delivered = await _operations.DeliverAsync(
                _nativeSession,
                reservation.Frame,
                reservation.Sequence,
                effectiveDiscontinuity,
                _decoderGeneration,
                operation.Token).ConfigureAwait(false);
            if (!Enum.IsDefined(delivered))
            {
                return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
            }

            var mapped = MapDelivery(delivered, mediaKind);
            if (mapped is PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered or
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered)
            {
                completion = PlaybackSchedulerOutputCompletion.Delivered;
                reservation.Complete(completion);
                completed = true;
                CommitCompletedSequence(
                    reservation.Sequence,
                    preferVideoNext: mapped ==
                        PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered);
            }
            else if (mapped == PlaybackGenerationMatchedSchedulerDeliveryResult.ResetRequired)
            {
                SetState(PlaybackGenerationMatchedSchedulerDeliveryState.ResetRequired);
            }
            else if (mapped is PlaybackGenerationMatchedSchedulerDeliveryResult.InvalidItem or
                PlaybackGenerationMatchedSchedulerDeliveryResult.ContextUnavailable or
                PlaybackGenerationMatchedSchedulerDeliveryResult.ConversionFailed or
                PlaybackGenerationMatchedSchedulerDeliveryResult.OutputFailure or
                PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted)
            {
                SetState(PlaybackGenerationMatchedSchedulerDeliveryState.Faulted);
            }

            return SetResult(mapped);
        }
        catch (OperationCanceledException)
        {
            return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled);
        }
        catch
        {
            return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
        }
        finally
        {
            if (reservation is not null)
            {
                if (!completed)
                {
                    try
                    {
                        reservation.Complete(completion);
                    }
                    catch
                    {
                        SetState(PlaybackGenerationMatchedSchedulerDeliveryState.Faulted);
                    }
                }

                reservation.Dispose();
            }

            lock (_sync)
            {
                RecordDeliveryObservationLocked(
                    hasObservationReservation,
                    observationTimingItem,
                    hasObservationTimingDecision,
                    observationTimingDecision,
                    _result,
                    ToObservationCompletion(completed, completion));
                if (ReferenceEquals(_activeCancellation, operation))
                {
                    _activeCancellation = null;
                }

                _hasActiveDelivery = false;
            }

            operation.Dispose();
            _deliveryGate.Release();
        }
    }

    public async ValueTask<PlaybackGenerationMatchedSchedulerResetResult> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return PlaybackGenerationMatchedSchedulerResetResult.Disposed;
            }

            if (_state == PlaybackGenerationMatchedSchedulerDeliveryState.Faulted)
            {
                return PlaybackGenerationMatchedSchedulerResetResult.Faulted;
            }

            _activeCancellation?.Cancel();
        }

        try
        {
            await _deliveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackGenerationMatchedSchedulerResetResult.Cancelled;
        }

        try
        {
            var result = await ResetGenerationAsync(cancellationToken).ConfigureAwait(false);
            if (result == PlaybackGenerationMatchedSchedulerDeliveryResult.None)
            {
                lock (_sync)
                {
                    _state = PlaybackGenerationMatchedSchedulerDeliveryState.Accepting;
                    _result = PlaybackGenerationMatchedSchedulerDeliveryResult.None;
                    _preferVideoNext = true;
                }

                return PlaybackGenerationMatchedSchedulerResetResult.Reset;
            }

            return result switch
            {
                PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled =>
                    PlaybackGenerationMatchedSchedulerResetResult.Cancelled,
                PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed =>
                    PlaybackGenerationMatchedSchedulerResetResult.Disposed,
                PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted =>
                    PlaybackGenerationMatchedSchedulerResetResult.Faulted,
                _ => PlaybackGenerationMatchedSchedulerResetResult.Failure,
            };
        }
        finally
        {
            _deliveryGate.Release();
        }
    }

    public async ValueTask StopAsync()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _state = PlaybackGenerationMatchedSchedulerDeliveryState.Disposed;
            _result = PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed;
            _activeCancellation?.Cancel();
        }

        await _deliveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _ingress.Dispose();
            _operations.Dispose();
        }
        finally
        {
            _deliveryGate.Release();
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

    private PlaybackSchedulerOutputKind GetPreferredKind()
    {
        lock (_sync)
        {
            return _preferVideoNext
                ? PlaybackSchedulerOutputKind.Video
                : PlaybackSchedulerOutputKind.Audio;
        }
    }

    private bool ValidateReservation(
        PlaybackSchedulerOutputReservation reservation,
        out PlaybackBoundedDemuxMediaKind mediaKind,
        out PlaybackAvTimingItem timingItem)
    {
        mediaKind = default;
        timingItem = default;
        var decode = _decodeBoundary.Snapshot;
        if (_sourceSession.IsDisposed || _decodeBoundary.IsDisposed || _nativeSession.IsDisposed ||
            _scheduler.IsDisposed || !decodeBoundaryMatches() || reservation.Frame.IsDisposed ||
            reservation.Sequence < 0 || !Enum.IsDefined(reservation.Discontinuity) ||
            _hasCompletedSequence && reservation.Sequence <= _lastCompletedSequence)
        {
            return false;
        }

        var metadata = reservation.Frame.Metadata;
        if (!Enum.IsDefined(metadata.MediaKind) ||
            !metadata.PresentationTimestamp.IsKnown ||
            metadata.PresentationTimestamp.Value < TimeSpan.Zero)
        {
            return false;
        }

        mediaKind = metadata.MediaKind switch
        {
            PlaybackDecoderFfmpegFrameMediaKind.Video => PlaybackBoundedDemuxMediaKind.Video,
            PlaybackDecoderFfmpegFrameMediaKind.Audio => PlaybackBoundedDemuxMediaKind.Audio,
            _ => (PlaybackBoundedDemuxMediaKind)(-1),
        };
        if (!Enum.IsDefined(mediaKind) || !_operations.ValidateContextRole(_nativeSession, mediaKind))
        {
            return false;
        }

        timingItem = new(
            mediaKind == PlaybackBoundedDemuxMediaKind.Video
                ? PlaybackPolicyMediaKind.Video
                : PlaybackPolicyMediaKind.Audio,
            PlaybackPolicyTime.Known(metadata.PresentationTimestamp.Value),
            metadata.Duration.IsKnown
                ? PlaybackPolicyTime.Known(metadata.Duration.Value)
                : PlaybackPolicyTime.Unknown,
            reservation.Sequence,
            reservation.Discontinuity,
            _decoderGeneration);
        return true;

        bool decodeBoundaryMatches() =>
            _decodeBoundary.ReferencesSourceSession(_sourceSession) &&
            _nativeSession.ReferencesSourceSession(_sourceSession) &&
            decode.DecoderGeneration == _decoderGeneration &&
            decode.DiscontinuityGeneration == _discontinuityGeneration;
    }

    private async ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> AdmitDecoderResetAsync(
        PlaybackSchedulerOutputReservation reservation,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_decoderResetPending || _hasCompletedSequence ||
                reservation.Sequence != _initialSchedulerSequence)
            {
                return FaultLocked(PlaybackGenerationMatchedSchedulerDeliveryResult.InvalidItem);
            }
        }

        var reset = await ResetGenerationAsync(cancellationToken).ConfigureAwait(false);
        if (reset != PlaybackGenerationMatchedSchedulerDeliveryResult.None)
        {
            return reset;
        }

        lock (_sync)
        {
            _decoderResetPending = false;
        }

        return PlaybackGenerationMatchedSchedulerDeliveryResult.None;
    }

    private async ValueTask<PlaybackGenerationMatchedSchedulerDeliveryResult> ResetGenerationAsync(
        CancellationToken cancellationToken)
    {
        var timing = await _operations.ResetTimingAsync(_decoderGeneration, cancellationToken)
            .ConfigureAwait(false);
        if (!Enum.IsDefined(timing))
        {
            return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
        }

        switch (timing)
        {
            case PlaybackAvTimingResetResult.Reset:
                break;
            case PlaybackAvTimingResetResult.Cancelled:
                return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled);
            case PlaybackAvTimingResetResult.Disposed:
                return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed);
            default:
                return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
        }

        var output = await _operations.ResetOutputAsync(_decoderGeneration, cancellationToken)
            .ConfigureAwait(false);
        if (!Enum.IsDefined(output))
        {
            return Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
        }

        return output switch
        {
            PlaybackGenerationMatchedOutputResetResult.Reset =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.None,
            PlaybackGenerationMatchedOutputResetResult.Cancelled =>
                SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled),
            PlaybackGenerationMatchedOutputResetResult.Disposed =>
                SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed),
            _ => Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted),
        };
    }

    private PlaybackGenerationMatchedSchedulerDeliveryResult MapMissingReservation(
        CancellationToken operationToken)
    {
        if (operationToken.IsCancellationRequested)
        {
            return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled);
        }

        if (_scheduler.IsDisposed)
        {
            return SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed);
        }

        return _scheduler.GetSnapshot().QueueLength == 0
            ? SetResult(PlaybackGenerationMatchedSchedulerDeliveryResult.Empty)
            : Fault(PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted);
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryResult MapDelivery(
        PlaybackGenerationMatchedCanonicalDeliveryResult result,
        PlaybackBoundedDemuxMediaKind mediaKind) => result switch
        {
            PlaybackGenerationMatchedCanonicalDeliveryResult.VideoProcessed
                when mediaKind == PlaybackBoundedDemuxMediaKind.Video =>
                    PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered,
            PlaybackGenerationMatchedCanonicalDeliveryResult.AudioProcessed
                when mediaKind == PlaybackBoundedDemuxMediaKind.Audio =>
                    PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            PlaybackGenerationMatchedCanonicalDeliveryResult.WouldBlock =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock,
            PlaybackGenerationMatchedCanonicalDeliveryResult.InitializationRequired =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedInitializationRequired,
            PlaybackGenerationMatchedCanonicalDeliveryResult.BindingRequired =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedBindingRequired,
            PlaybackGenerationMatchedCanonicalDeliveryResult.ResizeRequired =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedResizeRequired,
            PlaybackGenerationMatchedCanonicalDeliveryResult.DeviceLost =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.ResetRequired,
            PlaybackGenerationMatchedCanonicalDeliveryResult.InvalidItem =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.InvalidItem,
            PlaybackGenerationMatchedCanonicalDeliveryResult.ContextUnavailable =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.ContextUnavailable,
            PlaybackGenerationMatchedCanonicalDeliveryResult.ConversionFailed =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.ConversionFailed,
            PlaybackGenerationMatchedCanonicalDeliveryResult.Cancelled =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.Cancelled,
            PlaybackGenerationMatchedCanonicalDeliveryResult.Disposed =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.Disposed,
            PlaybackGenerationMatchedCanonicalDeliveryResult.Failure =>
                PlaybackGenerationMatchedSchedulerDeliveryResult.OutputFailure,
            _ => PlaybackGenerationMatchedSchedulerDeliveryResult.Faulted,
        };

    private PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot
        CreateDeliveryTraceSnapshotLocked()
    {
        if (_deliveryObservations is null)
        {
            return default;
        }

        return new(
            _deliveryObservationCount,
            _deliveryObservationOverflowed,
            _deliveryObservationCount > 0 ? _deliveryObservations[0] : default,
            _deliveryObservationCount > 1 ? _deliveryObservations[1] : default,
            _deliveryObservationCount > 2 ? _deliveryObservations[2] : default,
            _deliveryObservationCount > 3 ? _deliveryObservations[3] : default);
    }

    private void RecordDeliveryObservationLocked(
        bool hasReservation,
        PlaybackAvTimingItem timingItem,
        bool hasTimingDecision,
        PlaybackAvTimingDecision timingDecision,
        PlaybackGenerationMatchedSchedulerDeliveryResult result,
        PlaybackGenerationMatchedSchedulerReservationCompletion completion)
    {
        if (_deliveryObservations is null || !hasReservation)
        {
            return;
        }

        if (_deliveryObservationCount >= _deliveryObservations.Length)
        {
            _deliveryObservationOverflowed = true;
            return;
        }

        var hasTimestampOffset = timingItem.PresentationTimestamp.IsKnown;
        var timestampOffsetTicks = 0L;
        if (hasTimestampOffset)
        {
            var timestampTicks = timingItem.PresentationTimestamp.Value.Ticks;
            if (!_hasDeliveryObservationTimestampAnchor)
            {
                _hasDeliveryObservationTimestampAnchor = true;
                _deliveryObservationTimestampAnchorTicks = timestampTicks;
            }

            try
            {
                timestampOffsetTicks = checked(
                    timestampTicks - _deliveryObservationTimestampAnchorTicks);
            }
            catch (OverflowException)
            {
                _deliveryObservationOverflowed = true;
                return;
            }
        }

        _deliveryObservations[_deliveryObservationCount++] = new(
            timingItem.Sequence,
            timingItem.MediaKind,
            hasTimestampOffset,
            timestampOffsetTicks,
            hasTimingDecision,
            hasTimingDecision ? timingDecision.Result : default,
            hasTimingDecision ? timingDecision.WaitDuration.Ticks : 0,
            result,
            completion);
    }

    private static PlaybackGenerationMatchedSchedulerReservationCompletion
        ToObservationCompletion(
            bool completed,
            PlaybackSchedulerOutputCompletion completion)
    {
        if (!completed)
        {
            return PlaybackGenerationMatchedSchedulerReservationCompletion.Retained;
        }

        return completion switch
        {
            PlaybackSchedulerOutputCompletion.Delivered =>
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered,
            PlaybackSchedulerOutputCompletion.Dropped =>
                PlaybackGenerationMatchedSchedulerReservationCompletion.Dropped,
            _ => PlaybackGenerationMatchedSchedulerReservationCompletion.Retained,
        };
    }

    private void CommitCompletedSequence(long sequence, bool preferVideoNext)
    {
        lock (_sync)
        {
            _hasCompletedSequence = true;
            _lastCompletedSequence = sequence;
            _preferVideoNext = preferVideoNext;
        }
    }

    private bool IsDecoderResetPending()
    {
        lock (_sync)
        {
            return _decoderResetPending;
        }
    }

    private PlaybackGenerationMatchedSchedulerDeliveryResult SetResetRequired()
    {
        lock (_sync)
        {
            _state = PlaybackGenerationMatchedSchedulerDeliveryState.ResetRequired;
            return SetResultLocked(PlaybackGenerationMatchedSchedulerDeliveryResult.ResetRequired);
        }
    }

    private PlaybackGenerationMatchedSchedulerDeliveryResult Fault(
        PlaybackGenerationMatchedSchedulerDeliveryResult result)
    {
        lock (_sync)
        {
            return FaultLocked(result);
        }
    }

    private PlaybackGenerationMatchedSchedulerDeliveryResult FaultLocked(
        PlaybackGenerationMatchedSchedulerDeliveryResult result)
    {
        _state = PlaybackGenerationMatchedSchedulerDeliveryState.Faulted;
        _result = result;
        return result;
    }

    private void SetState(PlaybackGenerationMatchedSchedulerDeliveryState state)
    {
        lock (_sync)
        {
            if (!_isDisposed)
            {
                _state = state;
            }
        }
    }

    private PlaybackGenerationMatchedSchedulerDeliveryResult SetResult(
        PlaybackGenerationMatchedSchedulerDeliveryResult result)
    {
        lock (_sync)
        {
            return SetResultLocked(result);
        }
    }

    private PlaybackGenerationMatchedSchedulerDeliveryResult SetResultLocked(
        PlaybackGenerationMatchedSchedulerDeliveryResult result)
    {
        _result = result;
        return result;
    }
}
