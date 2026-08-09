namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
{
    Created,
    MissingIngress,
    MissingSink,
    IngressUnavailable,
    SchedulerUnavailable,
    SessionUnavailable,
    MissingTiming,
    TimingUnavailable,
    InvalidGeneration,
}

public enum PlaybackIsoBmffRegularMovieSchedulerDeliveryState
{
    Accepting,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
{
    None,
    VideoDelivered,
    AudioDelivered,
    VideoDropped,
    Empty,
    RetainedWait,
    RetainedAudioRecoveryRequired,
    ClockNotStarted,
    ResetRequired,
    Cancelled,
    AlreadyRunning,
    InvalidFrame,
    ContextUnavailable,
    DeliveryRejected,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
{
    Reset,
    NotRequired,
    InvalidGeneration,
    Cancelled,
    AlreadyRunning,
    TimingRejected,
    Faulted,
    Disposed,
}

public enum PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind
{
    Video,
    Audio,
}

public sealed record PlaybackIsoBmffRegularMovieSchedulerDeliverySnapshot(
    PlaybackIsoBmffRegularMovieSchedulerDeliveryState State,
    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult Result,
    PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind PreferredMediaKind,
    long DeliveredFrameCount,
    long DroppedVideoCount,
    bool HasActiveDelivery,
    bool HasTimingAdmission,
    long VideoGeneration,
    long AudioGeneration,
    PlaybackAvTimingDecision LastTimingDecision,
    PlaybackIsoBmffRegularMovieSchedulerIngressSnapshot Ingress);

internal interface IPlaybackIsoBmffRegularMovieSchedulerTimingOperations
{
    ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item,
        CancellationToken cancellationToken);

    ValueTask<PlaybackAvTimingResetResult> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackIsoBmffRegularMovieSchedulerTimingOperations(
    PlaybackAvTimingDecisionBoundary timing) :
    IPlaybackIsoBmffRegularMovieSchedulerTimingOperations
{
    public ValueTask<PlaybackAvTimingDecision> DecideAsync(
        PlaybackAvTimingItem item,
        CancellationToken cancellationToken) =>
        timing.DecideAsync(item, cancellationToken);

    public ValueTask<PlaybackAvTimingResetResult> ResetAsync(
        long videoGeneration,
        long audioGeneration,
        CancellationToken cancellationToken) =>
        timing.ResetAsync(
            videoGeneration,
            audioGeneration,
            cancellationToken);
}

internal interface IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations :
    IDisposable
{
    ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverAsync(
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackIsoBmffRegularMovieSchedulerDeliveryOperations :
    IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations
{
    private readonly PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary
        _delivery = new();
    private int _isDisposed;

    public ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult>
        DeliverAsync(
            PlaybackIsoBmffRegularMovieNativeDecoderSession session,
            PlaybackDecoderFfmpegManagedFrame frame,
            long sequence,
            PlaybackDecodedFrameDiscontinuityKind discontinuity,
            IPlaybackDecodedFrameSink sink,
            CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) != 0 || session.IsDisposed ||
            frame.IsDisposed)
        {
            return ValueTask.FromResult(
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult
                    .ContextUnavailable);
        }

        PlaybackDecoderFfmpegContextLease? context;
        var expectedCodec = frame.Metadata.MediaKind switch
        {
            PlaybackDecoderFfmpegFrameMediaKind.Video =>
                PlaybackDecoderFfmpegCodecId.H264,
            PlaybackDecoderFfmpegFrameMediaKind.Audio =>
                PlaybackDecoderFfmpegCodecId.Aac,
            _ => (PlaybackDecoderFfmpegCodecId)(-1),
        };
        try
        {
            context = frame.Metadata.MediaKind switch
            {
                PlaybackDecoderFfmpegFrameMediaKind.Video when
                    session.HasVideo => session.VideoContext,
                PlaybackDecoderFfmpegFrameMediaKind.Audio when
                    session.HasAudio => session.AudioContext,
                _ => null,
            };
        }
        catch (ObjectDisposedException)
        {
            context = null;
        }

        if (!Enum.IsDefined(expectedCodec) || context is null ||
            context.IsDisposed || context.CodecId != expectedCodec)
        {
            return ValueTask.FromResult(
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult
                    .ContextUnavailable);
        }

        return _delivery.DeliverAsync(
            context,
            frame,
            sequence,
            discontinuity,
            sink,
            cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _delivery.Dispose();
        }
    }
}

public sealed class PlaybackIsoBmffRegularMovieSchedulerDelivery :
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _deliveryGate = new(1, 1);
    private readonly IPlaybackDecodedFrameSink _sink;
    private readonly IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations
        _operations;
    private readonly IPlaybackIsoBmffRegularMovieSchedulerTimingOperations?
        _timingOperations;
    private long _videoGeneration;
    private long _audioGeneration;
    private PlaybackIsoBmffRegularMovieSchedulerIngress? _ingress;
    private BoundedPlaybackScheduler? _scheduler;
    private PlaybackIsoBmffRegularMovieNativeDecoderSession? _session;
    private PlaybackIsoBmffRegularMovieSchedulerDeliveryState _state =
        PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Accepting;
    private PlaybackIsoBmffRegularMovieSchedulerDeliveryResult _result =
        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.None;
    private PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind
        _preferredMediaKind =
            PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind.Video;
    private long _deliveredFrameCount;
    private long _droppedVideoCount;
    private int _ownedResourcesDisposed;
    private CancellationTokenSource? _activeCancellation;
    private bool _hasActiveDelivery;
    private PlaybackIsoBmffRegularMovieSchedulerIngressSnapshot
        _lastIngressSnapshot;
    private PlaybackAvTimingDecision _lastTimingDecision;

    internal PlaybackIsoBmffRegularMovieSchedulerDelivery(
        PlaybackIsoBmffRegularMovieSchedulerIngress ingress,
        BoundedPlaybackScheduler scheduler,
        PlaybackIsoBmffRegularMovieNativeDecoderSession session,
        IPlaybackDecodedFrameSink sink,
        IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations operations,
        IPlaybackIsoBmffRegularMovieSchedulerTimingOperations?
            timingOperations = null,
        long videoGeneration = 0,
        long audioGeneration = 0)
    {
        _ingress = ingress;
        _scheduler = scheduler;
        _session = session;
        _sink = sink;
        _operations = operations;
        _timingOperations = timingOperations;
        _videoGeneration = videoGeneration;
        _audioGeneration = audioGeneration;
        _lastIngressSnapshot = ingress.Snapshot;
    }

    public PlaybackIsoBmffRegularMovieSchedulerDeliverySnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _preferredMediaKind,
                    _deliveredFrameCount,
                    _droppedVideoCount,
                    _hasActiveDelivery,
                    _timingOperations is not null,
                    _videoGeneration,
                    _audioGeneration,
                    _lastTimingDecision,
                    _ingress?.Snapshot ?? _lastIngressSnapshot);
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
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed)
            {
                advance?.Frame?.Dispose();
                return PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .Disposed;
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Faulted)
            {
                advance?.Frame?.Dispose();
                return PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .Faulted;
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState
                    .ResetRequired)
            {
                return PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .Cancelled;
            }

            if (_hasActiveDelivery)
            {
                return PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .Cancelled;
            }

            var result = _ingress!.Transfer(advance, cancellationToken);
            _lastIngressSnapshot = _ingress.Snapshot;
            if (result is
                PlaybackIsoBmffRegularMovieSchedulerIngressResult.Faulted or
                PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .InvalidCompletion or
                PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .SequenceExhausted or
                PlaybackIsoBmffRegularMovieSchedulerIngressResult
                    .EnqueueRejected)
            {
                _state =
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Faulted;
                _result =
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Faulted;
                DisposeOwnedResources();
            }

            return result;
        }
    }

    internal bool UsesSink(IPlaybackDecodedFrameSink sink) =>
        ReferenceEquals(_sink, sink);

    internal async ValueTask<
        PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult>
        ResetTimingAsync(
            long videoGeneration,
            long audioGeneration,
            CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed)
            {
                return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                    .Disposed;
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Faulted)
            {
                return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                    .Faulted;
            }

            if (_state !=
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState
                    .ResetRequired)
            {
                return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                    .NotRequired;
            }
        }

        if (videoGeneration < 0 || audioGeneration < 0)
        {
            return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                .InvalidGeneration;
        }

        if (_timingOperations is null)
        {
            return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                .TimingRejected;
        }

        bool entered;
        try
        {
            entered = await _deliveryGate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                .Cancelled;
        }

        if (!entered)
        {
            return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                .AlreadyRunning;
        }

        try
        {
            lock (_sync)
            {
                if (_state ==
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed)
                {
                    return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                        .Disposed;
                }

                if (_state !=
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryState
                        .ResetRequired)
                {
                    return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                        .NotRequired;
                }
            }

            PlaybackAvTimingResetResult reset;
            try
            {
                reset = await _timingOperations.ResetAsync(
                    videoGeneration,
                    audioGeneration,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                    .Cancelled;
            }
            catch
            {
                reset = PlaybackAvTimingResetResult.Failure;
            }

            if (reset == PlaybackAvTimingResetResult.Cancelled)
            {
                return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                    .Cancelled;
            }

            if (reset == PlaybackAvTimingResetResult.Reset)
            {
                lock (_sync)
                {
                    _videoGeneration = videoGeneration;
                    _audioGeneration = audioGeneration;
                    _preferredMediaKind =
                        PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind
                            .Video;
                    _lastTimingDecision = default;
                    _state =
                        PlaybackIsoBmffRegularMovieSchedulerDeliveryState
                            .Accepting;
                    _result =
                        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.None;
                }

                return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                    .Reset;
            }

            if (reset is PlaybackAvTimingResetResult.Failure)
            {
                return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                    .TimingRejected;
            }

            lock (_sync)
            {
                _state =
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Faulted;
                _result =
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Faulted;
            }

            DisposeOwnedResources();
            return PlaybackIsoBmffRegularMovieSchedulerDeliveryResetResult
                .Faulted;
        }
        finally
        {
            _deliveryGate.Release();
        }
    }

    public async ValueTask<
        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult> DeliverOneAsync(
            CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed)
            {
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                        .Disposed);
            }

            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Faulted)
            {
                return _result;
            }


            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState
                    .ResetRequired)
            {
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                        .ResetRequired);
            }
        }

        bool entered;
        try
        {
            entered = await _deliveryGate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Cancelled);
        }

        if (!entered)
        {
            return SetResult(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                    .AlreadyRunning);
        }

        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_state ==
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed)
            {
                _deliveryGate.Release();
                return SetResultLocked(
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                        .Disposed);
            }

            operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _activeCancellation = operation;
            _hasActiveDelivery = true;
        }

        PlaybackSchedulerOutputReservation? reservation = null;
        var completion = PlaybackSchedulerOutputCompletion.Retained;
        var faulted = false;
        try
        {
            BoundedPlaybackScheduler scheduler;
            PlaybackIsoBmffRegularMovieNativeDecoderSession session;
            PlaybackSchedulerOutputKind preferred;
            lock (_sync)
            {
                if (_state ==
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed)
                {
                    return SetResultLocked(
                        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .Disposed);
                }

                scheduler = _scheduler!;
                session = _session!;
                preferred = _preferredMediaKind ==
                    PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind.Video
                        ? PlaybackSchedulerOutputKind.Video
                        : PlaybackSchedulerOutputKind.Audio;
            }

            reservation = await scheduler.ReserveOutputAsync(
                preferred,
                operation.Token).ConfigureAwait(false);
            if (reservation is null)
            {
                if (operation.IsCancellationRequested)
                {
                    return CancellationResult();
                }

                if (scheduler.IsDisposed || session.IsDisposed)
                {
                    faulted = true;
                    return SetFault(
                        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .ContextUnavailable);
                }

                return SetResult(
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Empty);
            }

            if (!IsValidReservation(reservation, session))
            {
                faulted = true;
                return SetFault(
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                        .InvalidFrame);
            }

            if (_timingOperations is not null)
            {
                if (!TryCreateTimingItem(reservation, out var timingItem))
                {
                    faulted = true;
                    return SetFault(
                        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .InvalidFrame);
                }

                var decision = await _timingOperations.DecideAsync(
                    timingItem,
                    operation.Token).ConfigureAwait(false);
                lock (_sync)
                {
                    _lastTimingDecision = decision;
                }

                if (!Enum.IsDefined(decision.Result) ||
                    decision.WaitDuration < TimeSpan.Zero)
                {
                    faulted = true;
                    return SetFault(
                        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .Faulted);
                }

                switch (decision.Result)
                {
                    case PlaybackAvTimingDecisionResult.ProcessNow:
                        break;
                    case PlaybackAvTimingDecisionResult.Wait:
                        return SetResult(
                            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                .RetainedWait);
                    case PlaybackAvTimingDecisionResult.VideoLate:
                        if (reservation.Frame.Metadata.MediaKind !=
                            PlaybackDecoderFfmpegFrameMediaKind.Video)
                        {
                            faulted = true;
                            return SetFault(
                                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                    .InvalidFrame);
                        }

                        completion = PlaybackSchedulerOutputCompletion.Dropped;
                        lock (_sync)
                        {
                            _droppedVideoCount = checked(
                                _droppedVideoCount + 1);
                            _preferredMediaKind =
                                PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind
                                    .Audio;
                            return SetResultLocked(
                                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                    .VideoDropped);
                        }
                    case PlaybackAvTimingDecisionResult.AudioLate:
                        if (reservation.Frame.Metadata.MediaKind !=
                            PlaybackDecoderFfmpegFrameMediaKind.Audio)
                        {
                            faulted = true;
                            return SetFault(
                                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                    .InvalidFrame);
                        }

                        return SetResult(
                            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                .RetainedAudioRecoveryRequired);
                    case PlaybackAvTimingDecisionResult.ClockNotStarted:
                        return SetResult(
                            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                .ClockNotStarted);
                    case PlaybackAvTimingDecisionResult.Cancelled:
                        return CancellationResult();
                    case PlaybackAvTimingDecisionResult
                            .DiscontinuityResetRequired:
                    case PlaybackAvTimingDecisionResult.GenerationMismatch:
                        lock (_sync)
                        {
                            _state =
                                PlaybackIsoBmffRegularMovieSchedulerDeliveryState
                                    .ResetRequired;
                            return SetResultLocked(
                                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                    .ResetRequired);
                        }
                    default:
                        faulted = true;
                        return SetFault(
                            PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                .InvalidFrame);
                }
            }

            var delivered = await _operations.DeliverAsync(
                session,
                reservation.Frame,
                reservation.Sequence,
                reservation.Discontinuity,
                _sink,
                operation.Token).ConfigureAwait(false);
            if (delivered ==
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled)
            {
                return CancellationResult();
            }

            if (delivered !=
                PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered)
            {
                faulted = true;
                return SetFault(
                    delivered ==
                        PlaybackDecoderFfmpegAwaitedSinkDeliveryResult
                            .ContextUnavailable
                            ? PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                .ContextUnavailable
                            : PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                                .DeliveryRejected);
            }

            completion = PlaybackSchedulerOutputCompletion.Delivered;
            lock (_sync)
            {
                _deliveredFrameCount = checked(_deliveredFrameCount + 1);
                var video = reservation.Frame.Metadata.MediaKind ==
                    PlaybackDecoderFfmpegFrameMediaKind.Video;
                _preferredMediaKind = video
                    ? PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind
                        .Audio
                    : PlaybackIsoBmffRegularMovieSchedulerPreferredMediaKind
                        .Video;
                return SetResultLocked(
                    video
                        ? PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .VideoDelivered
                        : PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .AudioDelivered);
            }
        }
        catch (OperationCanceledException) when (
            operation.IsCancellationRequested)
        {
            return CancellationResult();
        }
        catch
        {
            faulted = true;
            return SetFault(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Faulted);
        }
        finally
        {
            try
            {
                reservation?.Complete(completion);
                reservation?.Dispose();
            }
            finally
            {
                if (faulted)
                {
                    DisposeOwnedResources();
                }

                lock (_sync)
                {
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
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed)
            {
                return;
            }

            _state =
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed;
            _result =
                PlaybackIsoBmffRegularMovieSchedulerDeliveryResult.Disposed;
            _activeCancellation?.Cancel();
        }

        await _deliveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            DisposeOwnedResources();
        }
        finally
        {
            _deliveryGate.Release();
        }
    }

    private static bool IsValidReservation(
        PlaybackSchedulerOutputReservation reservation,
        PlaybackIsoBmffRegularMovieNativeDecoderSession session)
    {
        if (reservation.Frame.IsDisposed || reservation.Sequence < 0 ||
            !Enum.IsDefined(reservation.Discontinuity))
        {
            return false;
        }

        return reservation.Frame.Metadata.MediaKind switch
        {
            PlaybackDecoderFfmpegFrameMediaKind.Video => session.HasVideo,
            PlaybackDecoderFfmpegFrameMediaKind.Audio => session.HasAudio,
            _ => false,
        };
    }

    private bool TryCreateTimingItem(
        PlaybackSchedulerOutputReservation reservation,
        out PlaybackAvTimingItem item)
    {
        item = default;
        var metadata = reservation.Frame.Metadata;
        if (!metadata.PresentationTimestamp.IsKnown ||
            metadata.PresentationTimestamp.Value < TimeSpan.Zero)
        {
            return false;
        }

        var mediaKind = metadata.MediaKind switch
        {
            PlaybackDecoderFfmpegFrameMediaKind.Video =>
                PlaybackPolicyMediaKind.Video,
            PlaybackDecoderFfmpegFrameMediaKind.Audio =>
                PlaybackPolicyMediaKind.Audio,
            _ => (PlaybackPolicyMediaKind)(-1),
        };
        if (!Enum.IsDefined(mediaKind))
        {
            return false;
        }

        item = new(
            mediaKind,
            PlaybackPolicyTime.Known(metadata.PresentationTimestamp.Value),
            metadata.Duration.IsKnown
                ? PlaybackPolicyTime.Known(metadata.Duration.Value)
                : PlaybackPolicyTime.Unknown,
            reservation.Sequence,
            reservation.Discontinuity,
            mediaKind == PlaybackPolicyMediaKind.Video
                ? _videoGeneration
                : _audioGeneration);
        return true;
    }

    private PlaybackIsoBmffRegularMovieSchedulerDeliveryResult SetResult(
        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult result)
    {
        lock (_sync)
        {
            return SetResultLocked(result);
        }
    }

    private PlaybackIsoBmffRegularMovieSchedulerDeliveryResult SetResultLocked(
        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult result)
    {
        _result = result;
        return result;
    }

    private PlaybackIsoBmffRegularMovieSchedulerDeliveryResult SetFault(
        PlaybackIsoBmffRegularMovieSchedulerDeliveryResult result)
    {
        lock (_sync)
        {
            _state =
                PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Faulted;
            return SetResultLocked(result);
        }
    }

    private PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
        CancellationResult()
    {
        lock (_sync)
        {
            return SetResultLocked(
                _state ==
                    PlaybackIsoBmffRegularMovieSchedulerDeliveryState.Disposed
                        ? PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .Disposed
                        : PlaybackIsoBmffRegularMovieSchedulerDeliveryResult
                            .Cancelled);
        }
    }

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(ref _ownedResourcesDisposed, 1) != 0)
        {
            return;
        }

        PlaybackIsoBmffRegularMovieSchedulerIngress? ingress;
        lock (_sync)
        {
            ingress = _ingress;
            if (ingress is not null)
            {
                _lastIngressSnapshot = ingress.Snapshot;
            }

            _ingress = null;
            _scheduler = null;
            _session = null;
        }

        try
        {
            _operations.Dispose();
        }
        finally
        {
            ingress?.Dispose();
        }
    }
}

public sealed record PlaybackIsoBmffRegularMovieSchedulerDeliveryCreation(
    PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult Result,
    PlaybackIsoBmffRegularMovieSchedulerDelivery? Delivery);

public sealed class PlaybackIsoBmffRegularMovieSchedulerDeliveryFactory
{
    private readonly IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations
        _operations;

    public PlaybackIsoBmffRegularMovieSchedulerDeliveryFactory()
        : this(new PlaybackIsoBmffRegularMovieSchedulerDeliveryOperations())
    {
    }

    internal PlaybackIsoBmffRegularMovieSchedulerDeliveryFactory(
        IPlaybackIsoBmffRegularMovieSchedulerDeliveryOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackIsoBmffRegularMovieSchedulerDeliveryCreation Create(
        PlaybackIsoBmffRegularMovieSchedulerIngress? ingress,
        IPlaybackDecodedFrameSink? sink)
    {
        if (ingress is null)
        {
            _operations.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .MissingIngress,
                null);
        }

        if (sink is null)
        {
            DisposeOwned(ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .MissingSink,
                null);
        }

        if (ingress.Snapshot.State !=
            PlaybackIsoBmffRegularMovieSchedulerIngressState.Accepting)
        {
            DisposeOwned(ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .IngressUnavailable,
                null);
        }

        BoundedPlaybackScheduler scheduler;
        PlaybackIsoBmffRegularMovieNativeDecoderSession session;
        try
        {
            scheduler = ingress.Scheduler;
            session = ingress.SourceSession;
        }
        catch (ObjectDisposedException)
        {
            DisposeOwned(ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .IngressUnavailable,
                null);
        }

        if (scheduler.IsDisposed)
        {
            DisposeOwned(ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .SchedulerUnavailable,
                null);
        }

        if (session.IsDisposed)
        {
            DisposeOwned(ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .SessionUnavailable,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult.Created,
            new PlaybackIsoBmffRegularMovieSchedulerDelivery(
                ingress,
                scheduler,
                session,
                sink,
                _operations));
    }

    private void DisposeOwned(
        PlaybackIsoBmffRegularMovieSchedulerIngress ingress)
    {
        try
        {
            _operations.Dispose();
        }
        finally
        {
            ingress.Dispose();
        }
    }
}

public static class PlaybackIsoBmffRegularMovieTimedSchedulerDeliveryFactory
{
    public static PlaybackIsoBmffRegularMovieSchedulerDeliveryCreation Create(
        PlaybackIsoBmffRegularMovieSchedulerIngress? ingress,
        IPlaybackDecodedFrameSink? sink,
        PlaybackAvTimingDecisionBoundary? timing,
        long videoGeneration,
        long audioGeneration)
    {
        var operations =
            new PlaybackIsoBmffRegularMovieSchedulerDeliveryOperations();
        if (ingress is null)
        {
            operations.Dispose();
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .MissingIngress,
                null);
        }

        if (sink is null)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .MissingSink,
                null);
        }

        if (timing is null)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .MissingTiming,
                null);
        }

        if (videoGeneration < 0 || audioGeneration < 0)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .InvalidGeneration,
                null);
        }

        if (timing.Snapshot.State != PlaybackAvTimingDecisionState.Ready)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .TimingUnavailable,
                null);
        }

        if (ingress.Snapshot.State !=
            PlaybackIsoBmffRegularMovieSchedulerIngressState.Accepting)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .IngressUnavailable,
                null);
        }

        BoundedPlaybackScheduler scheduler;
        PlaybackIsoBmffRegularMovieNativeDecoderSession session;
        try
        {
            scheduler = ingress.Scheduler;
            session = ingress.SourceSession;
        }
        catch (ObjectDisposedException)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .IngressUnavailable,
                null);
        }

        if (scheduler.IsDisposed)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .SchedulerUnavailable,
                null);
        }

        if (session.IsDisposed)
        {
            DisposeOwned(operations, ingress);
            return new(
                PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult
                    .SessionUnavailable,
                null);
        }

        return new(
            PlaybackIsoBmffRegularMovieSchedulerDeliveryCreationResult.Created,
            new PlaybackIsoBmffRegularMovieSchedulerDelivery(
                ingress,
                scheduler,
                session,
                sink,
                operations,
                new PlaybackIsoBmffRegularMovieSchedulerTimingOperations(
                    timing),
                videoGeneration,
                audioGeneration));
    }

    private static void DisposeOwned(
        PlaybackIsoBmffRegularMovieSchedulerDeliveryOperations operations,
        PlaybackIsoBmffRegularMovieSchedulerIngress ingress)
    {
        try
        {
            operations.Dispose();
        }
        finally
        {
            ingress.Dispose();
        }
    }
}
