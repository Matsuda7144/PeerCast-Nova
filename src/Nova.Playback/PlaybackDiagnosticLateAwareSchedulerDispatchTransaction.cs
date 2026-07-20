namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult
{
    Advanced,
    NoFrames,
    InvalidCompletion,
    InvalidScheduler,
    InvalidConfiguration,
    IngressRejected,
    PartiallyAdvanced,
    DispatchStopped,
    Cancelled,
    Faulted,
}

internal interface IPlaybackDiagnosticLateAwareSchedulerDispatchOperations
{
    ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
        BoundedPlaybackScheduler scheduler,
        PlaybackTimedSchedulerDispatchOptions options,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackDiagnosticLateAwareSchedulerDispatchOperations(
    PlaybackLateAwareTimedDispatchBoundary dispatch) :
    IPlaybackDiagnosticLateAwareSchedulerDispatchOperations
{
    public ValueTask<PlaybackLateAwareTimedDispatchResult> DispatchOneAsync(
        BoundedPlaybackScheduler scheduler,
        PlaybackTimedSchedulerDispatchOptions options,
        CancellationToken cancellationToken) =>
        dispatch.DispatchOneAsync(options, cancellationToken);
}

public sealed class PlaybackDiagnosticLateAwareSchedulerDispatchCompletion : IDisposable
{
    private BoundedPlaybackScheduler? _scheduler;
    private PlaybackDiagnosticManagedFrameSchedulerIngressCompletion? _ingress;
    private int _disposed;

    internal PlaybackDiagnosticLateAwareSchedulerDispatchCompletion(
        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult result,
        PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult? ingressResult,
        PlaybackLateAwareTimedDispatchResult? lastDispatchResult,
        int acceptedFrameCount,
        int consumedFrameCount,
        bool outstandingVideo,
        bool outstandingAudio,
        BoundedPlaybackScheduler? scheduler,
        PlaybackDiagnosticManagedFrameSchedulerIngressCompletion? ingress)
    {
        Result = result;
        IngressResult = ingressResult;
        LastDispatchResult = lastDispatchResult;
        AcceptedFrameCount = acceptedFrameCount;
        ConsumedFrameCount = consumedFrameCount;
        OutstandingVideo = outstandingVideo;
        OutstandingAudio = outstandingAudio;
        _scheduler = scheduler;
        _ingress = ingress;
    }

    public PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult Result { get; }

    public PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult? IngressResult { get; }

    public PlaybackLateAwareTimedDispatchResult? LastDispatchResult { get; }

    public int AcceptedFrameCount { get; }

    public int ConsumedFrameCount { get; }

    public bool OutstandingVideo { get; }

    public bool OutstandingAudio { get; }

    public PlaybackDiagnosticNativeDecoderLease? Lease => _ingress?.Lease;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _scheduler, null)?.Dispose();
        Interlocked.Exchange(ref _ingress, null)?.Dispose();
    }
}

/// <summary>
/// Diagnostic-only finite composition from the 0.0.8m managed-frame completion,
/// through the existing 0.0.8r scheduler ingress, to at most two calls to the
/// late-aware timed dispatch boundary. It does not start the continuous pump or
/// audio recovery and does not activate decoder or output resources.
/// </summary>
public sealed class PlaybackDiagnosticLateAwareSchedulerDispatchTransaction
{
    private readonly PlaybackDiagnosticManagedFrameSchedulerIngressTransaction _ingress;
    private readonly IPlaybackDiagnosticLateAwareSchedulerDispatchOperations _dispatch;

    public PlaybackDiagnosticLateAwareSchedulerDispatchTransaction(
        PlaybackLateAwareTimedDispatchBoundary dispatch)
        : this(
            new PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(),
            CreateDispatchOperations(dispatch))
    {
    }

    internal PlaybackDiagnosticLateAwareSchedulerDispatchTransaction(
        PlaybackDiagnosticManagedFrameSchedulerIngressTransaction ingress,
        IPlaybackDiagnosticLateAwareSchedulerDispatchOperations dispatch)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(dispatch);
        _ingress = ingress;
        _dispatch = dispatch;
    }

    public async ValueTask<PlaybackDiagnosticLateAwareSchedulerDispatchCompletion> ExecuteAsync(
        PlaybackDiagnosticManagedFrameCopyTransactionCompletion? source,
        BoundedPlaybackScheduler? scheduler,
        PlaybackTimedSchedulerDispatchOptions? options,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidSource(source))
        {
            source?.Dispose();
            return Complete(
                PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidCompletion);
        }

        var validSource = source!;
        if (scheduler is null || scheduler.IsDisposed || !IsFreshScheduler(scheduler.GetSnapshot()))
        {
            validSource.Dispose();
            return Complete(
                PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidScheduler);
        }

        if (!ValidateOptions(options))
        {
            validSource.Dispose();
            return Complete(
                PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidConfiguration);
        }

        var sourceVideo = validSource.VideoState ==
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied;
        var sourceAudio = validSource.AudioState ==
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied;
        PlaybackDiagnosticManagedFrameSchedulerIngressCompletion? ingress = null;
        var outstandingVideo = false;
        var outstandingAudio = false;
        var consumedFrameCount = 0;
        PlaybackLateAwareTimedDispatchResult? lastDispatchResult = null;

        try
        {
            ingress = _ingress.Execute(validSource, scheduler);
            outstandingVideo = IsAcceptedEnqueue(ingress.VideoEnqueueResult);
            outstandingAudio = IsAcceptedEnqueue(ingress.AudioEnqueueResult);
            var acceptedFrameCount = Count(outstandingVideo, outstandingAudio);

            if (acceptedFrameCount != ingress.AcceptedFrameCount ||
                outstandingVideo && !sourceVideo || outstandingAudio && !sourceAudio)
            {
                return CompleteWithOwnership(
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                    ingress,
                    lastDispatchResult,
                    acceptedFrameCount,
                    consumedFrameCount,
                    outstandingVideo,
                    outstandingAudio,
                    scheduler);
            }

            if (acceptedFrameCount == 0)
            {
                var result = ingress.Result switch
                {
                    PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Advanced =>
                        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.NoFrames,
                    PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.InvalidCompletion =>
                        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.InvalidCompletion,
                    PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Faulted =>
                        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                    _ => PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.IngressRejected,
                };
                var ingressResult = ingress.Result;
                ingress.Dispose();
                ingress = null;
                return Complete(result, ingressResult);
            }

            if (ingress.Lease is null || ingress.Lease.IsDisposed ||
                !SchedulerMatchesOutstanding(scheduler, outstandingVideo, outstandingAudio))
            {
                return CompleteWithOwnership(
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                    ingress,
                    lastDispatchResult,
                    acceptedFrameCount,
                    consumedFrameCount,
                    outstandingVideo,
                    outstandingAudio,
                    scheduler);
            }

            if (ingress.Result ==
                PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Faulted)
            {
                return CompleteWithOwnership(
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                    ingress,
                    lastDispatchResult,
                    acceptedFrameCount,
                    consumedFrameCount,
                    outstandingVideo,
                    outstandingAudio,
                    scheduler);
            }

            if (ingress.Result is not
                    PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Advanced and not
                    PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.AudioEnqueueRejected)
            {
                return CompleteWithOwnership(
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                    ingress,
                    lastDispatchResult,
                    acceptedFrameCount,
                    consumedFrameCount,
                    outstandingVideo,
                    outstandingAudio,
                    scheduler);
            }

            for (var dispatchCount = 0; dispatchCount < acceptedFrameCount; dispatchCount++)
            {
                PlaybackLateAwareTimedDispatchResult dispatchResult;
                try
                {
                    dispatchResult = await _dispatch.DispatchOneAsync(
                        scheduler,
                        options!,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CompleteWithOwnership(
                        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Cancelled,
                        ingress,
                        lastDispatchResult,
                        acceptedFrameCount,
                        consumedFrameCount,
                        outstandingVideo,
                        outstandingAudio,
                        scheduler);
                }

                lastDispatchResult = dispatchResult;
                if (!Enum.IsDefined(dispatchResult))
                {
                    return CompleteWithOwnership(
                        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                        ingress,
                        lastDispatchResult,
                        acceptedFrameCount,
                        consumedFrameCount,
                        outstandingVideo,
                        outstandingAudio,
                        scheduler);
                }

                switch (dispatchResult)
                {
                    case PlaybackLateAwareTimedDispatchResult.VideoProcessed:
                    case PlaybackLateAwareTimedDispatchResult.VideoDropConsumed:
                        if (!outstandingVideo)
                        {
                            return CompleteWithOwnership(
                                PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                                ingress,
                                lastDispatchResult,
                                acceptedFrameCount,
                                consumedFrameCount,
                                outstandingVideo,
                                outstandingAudio,
                                scheduler);
                        }

                        outstandingVideo = false;
                        consumedFrameCount++;
                        break;
                    case PlaybackLateAwareTimedDispatchResult.AudioProcessed:
                        if (!outstandingAudio)
                        {
                            return CompleteWithOwnership(
                                PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                                ingress,
                                lastDispatchResult,
                                acceptedFrameCount,
                                consumedFrameCount,
                                outstandingVideo,
                                outstandingAudio,
                                scheduler);
                        }

                        outstandingAudio = false;
                        consumedFrameCount++;
                        break;
                    case PlaybackLateAwareTimedDispatchResult.Cancelled:
                        return CompleteWithOwnership(
                            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Cancelled,
                            ingress,
                            lastDispatchResult,
                            acceptedFrameCount,
                            consumedFrameCount,
                            outstandingVideo,
                            outstandingAudio,
                            scheduler);
                    case PlaybackLateAwareTimedDispatchResult.InvalidItem:
                    case PlaybackLateAwareTimedDispatchResult.InvalidConfiguration:
                    case PlaybackLateAwareTimedDispatchResult.AlreadyRunning:
                    case PlaybackLateAwareTimedDispatchResult.Failure:
                        return CompleteWithOwnership(
                            PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                            ingress,
                            lastDispatchResult,
                            acceptedFrameCount,
                            consumedFrameCount,
                            outstandingVideo,
                            outstandingAudio,
                            scheduler);
                    default:
                        return CompleteWithOwnership(
                            consumedFrameCount > 0
                                ? PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.PartiallyAdvanced
                                : PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.DispatchStopped,
                            ingress,
                            lastDispatchResult,
                            acceptedFrameCount,
                            consumedFrameCount,
                            outstandingVideo,
                            outstandingAudio,
                            scheduler);
                }

                if (!SchedulerMatchesOutstanding(scheduler, outstandingVideo, outstandingAudio))
                {
                    return CompleteWithOwnership(
                        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                        ingress,
                        lastDispatchResult,
                        acceptedFrameCount,
                        consumedFrameCount,
                        outstandingVideo,
                        outstandingAudio,
                        scheduler);
                }
            }

            if (outstandingVideo || outstandingAudio ||
                !SchedulerMatchesOutstanding(
                    scheduler,
                    outstandingVideo: false,
                    outstandingAudio: false))
            {
                return CompleteWithOwnership(
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                    ingress,
                    lastDispatchResult,
                    ingress.AcceptedFrameCount,
                    consumedFrameCount,
                    outstandingVideo,
                    outstandingAudio,
                    scheduler);
            }

            var finalIngressResult = ingress.Result;
            var finalAcceptedFrameCount = ingress.AcceptedFrameCount;
            ingress.Dispose();
            ingress = null;
            var finalResult = finalIngressResult switch
            {
                PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Advanced =>
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Advanced,
                PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.AudioEnqueueRejected =>
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.PartiallyAdvanced,
                _ => PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
            };
            return Complete(
                finalResult,
                finalIngressResult,
                lastDispatchResult,
                finalAcceptedFrameCount,
                consumedFrameCount);
        }
        catch (OperationCanceledException)
        {
            if (ingress is null)
            {
                validSource.Dispose();
                return Complete(
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Cancelled);
            }

            return CompleteWithOwnership(
                PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Cancelled,
                ingress,
                lastDispatchResult,
                ingress.AcceptedFrameCount,
                consumedFrameCount,
                outstandingVideo,
                outstandingAudio,
                scheduler);
        }
        catch
        {
            if (ingress is null)
            {
                validSource.Dispose();
                return Complete(
                    PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted);
            }

            return CompleteWithOwnership(
                PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult.Faulted,
                ingress,
                lastDispatchResult,
                ingress.AcceptedFrameCount,
                consumedFrameCount,
                outstandingVideo,
                outstandingAudio,
                scheduler);
        }
    }

    private static PlaybackDiagnosticLateAwareSchedulerDispatchOperations CreateDispatchOperations(
        PlaybackLateAwareTimedDispatchBoundary dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return new PlaybackDiagnosticLateAwareSchedulerDispatchOperations(dispatch);
    }

    private static bool IsValidSource(
        PlaybackDiagnosticManagedFrameCopyTransactionCompletion? source) =>
        source is not null && !source.IsDisposed &&
        source.Result == PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced &&
        source.Lease is not null && !source.Lease.IsDisposed &&
        IsTimingReady(
            source.VideoState,
            source.VideoFrame,
            PlaybackDecoderFfmpegFrameMediaKind.Video) &&
        IsTimingReady(
            source.AudioState,
            source.AudioFrame,
            PlaybackDecoderFfmpegFrameMediaKind.Audio);

    private static bool IsTimingReady(
        PlaybackDecoderFfmpegManagedFrameCopyResult? state,
        PlaybackDecoderFfmpegManagedFrame? frame,
        PlaybackDecoderFfmpegFrameMediaKind expectedKind) =>
        state switch
        {
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied =>
                frame is not null && !frame.IsDisposed &&
                frame.Metadata.MediaKind == expectedKind &&
                frame.Metadata.PresentationTimestamp.IsKnown &&
                frame.Metadata.PresentationTimestamp.Value >= TimeSpan.Zero,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput => frame is null,
            _ => false,
        };

    private static bool IsFreshScheduler(PlaybackSchedulerSnapshot snapshot) =>
        snapshot.QueueLength == 0 && snapshot.DropCount == 0 &&
        snapshot.DeliveryCount == 0 && snapshot.OwnedByteCount == 0;

    private static bool ValidateOptions(PlaybackTimedSchedulerDispatchOptions? options) =>
        options is not null &&
        options.MaximumDecisionCount is >= 1 and <=
            PlaybackTimedSchedulerDispatchOptions.MaximumDecisionCountLimit &&
        options.MaximumTotalWait > TimeSpan.Zero &&
        options.MaximumTotalWait <= PlaybackTimedSchedulerDispatchOptions.MaximumTotalWaitLimit &&
        options.MinimumDelay > TimeSpan.Zero &&
        options.MaximumSingleDelay >= options.MinimumDelay &&
        options.MaximumSingleDelay <= PlaybackTimedSchedulerDispatchOptions.MaximumSingleDelayLimit &&
        options.MaximumSingleDelay <= options.MaximumTotalWait;

    private static bool IsAcceptedEnqueue(PlaybackSchedulerEnqueueResult? result) =>
        result is PlaybackSchedulerEnqueueResult.Enqueued or
            PlaybackSchedulerEnqueueResult.EnqueuedAfterDroppingOldest;

    private static int Count(bool video, bool audio) =>
        (video ? 1 : 0) + (audio ? 1 : 0);

    private static bool SchedulerMatchesOutstanding(
        BoundedPlaybackScheduler scheduler,
        bool outstandingVideo,
        bool outstandingAudio)
    {
        var expectedCount = Count(outstandingVideo, outstandingAudio);
        var snapshot = scheduler.GetSnapshot();
        return snapshot.QueueLength == expectedCount &&
            (expectedCount == 0
                ? snapshot.OwnedByteCount == 0
                : snapshot.OwnedByteCount > 0);
    }

    private static PlaybackDiagnosticLateAwareSchedulerDispatchCompletion CompleteWithOwnership(
        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult result,
        PlaybackDiagnosticManagedFrameSchedulerIngressCompletion ingress,
        PlaybackLateAwareTimedDispatchResult? lastDispatchResult,
        int acceptedFrameCount,
        int consumedFrameCount,
        bool outstandingVideo,
        bool outstandingAudio,
        BoundedPlaybackScheduler scheduler) =>
        new(
            result,
            ingress.Result,
            lastDispatchResult,
            acceptedFrameCount,
            consumedFrameCount,
            outstandingVideo,
            outstandingAudio,
            scheduler,
            ingress);

    private static PlaybackDiagnosticLateAwareSchedulerDispatchCompletion Complete(
        PlaybackDiagnosticLateAwareSchedulerDispatchTransactionResult result,
        PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult? ingressResult = null,
        PlaybackLateAwareTimedDispatchResult? lastDispatchResult = null,
        int acceptedFrameCount = 0,
        int consumedFrameCount = 0) =>
        new(
            result,
            ingressResult,
            lastDispatchResult,
            acceptedFrameCount,
            consumedFrameCount,
            outstandingVideo: false,
            outstandingAudio: false,
            scheduler: null,
            ingress: null);
}
