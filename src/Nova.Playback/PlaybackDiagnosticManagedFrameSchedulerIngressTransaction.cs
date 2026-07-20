namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult
{
    Advanced,
    InvalidCompletion,
    VideoEnqueueRejected,
    AudioEnqueueRejected,
    Faulted,
}

internal interface IPlaybackDiagnosticManagedFrameSchedulerIngressOperations
{
    PlaybackSchedulerEnqueueResult Enqueue(
        BoundedPlaybackScheduler scheduler,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity);
}

internal sealed class PlaybackDiagnosticManagedFrameSchedulerIngressOperations :
    IPlaybackDiagnosticManagedFrameSchedulerIngressOperations
{
    public PlaybackSchedulerEnqueueResult Enqueue(
        BoundedPlaybackScheduler scheduler,
        PlaybackDecoderFfmpegManagedFrame frame,
        long sequence,
        PlaybackDecodedFrameDiscontinuityKind discontinuity) =>
        scheduler.Enqueue(frame, sequence, discontinuity);
}

public sealed class PlaybackDiagnosticManagedFrameSchedulerIngressCompletion : IDisposable
{
    private PlaybackDiagnosticNativeDecoderLease? _lease;
    private int _disposed;

    internal PlaybackDiagnosticManagedFrameSchedulerIngressCompletion(
        PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult result,
        PlaybackSchedulerEnqueueResult? videoEnqueueResult,
        PlaybackSchedulerEnqueueResult? audioEnqueueResult,
        int acceptedFrameCount,
        PlaybackDiagnosticNativeDecoderLease? lease)
    {
        Result = result;
        VideoEnqueueResult = videoEnqueueResult;
        AudioEnqueueResult = audioEnqueueResult;
        AcceptedFrameCount = acceptedFrameCount;
        _lease = lease;
    }

    public PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult Result { get; }

    public PlaybackSchedulerEnqueueResult? VideoEnqueueResult { get; }

    public PlaybackSchedulerEnqueueResult? AudioEnqueueResult { get; }

    public int AcceptedFrameCount { get; }

    public PlaybackDiagnosticNativeDecoderLease? Lease => _lease;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}

/// <summary>
/// Diagnostic-only finite ownership transfer from the 0.0.8m managed frame copy
/// completion to the existing bounded scheduler. Video is attempted before audio
/// with fixed sequences 0 and 1. This boundary does not claim atomic two-stream
/// enqueue.
/// </summary>
public sealed class PlaybackDiagnosticManagedFrameSchedulerIngressTransaction
{
    private readonly IPlaybackDiagnosticManagedFrameSchedulerIngressOperations _operations;

    public PlaybackDiagnosticManagedFrameSchedulerIngressTransaction()
        : this(new PlaybackDiagnosticManagedFrameSchedulerIngressOperations())
    {
    }

    internal PlaybackDiagnosticManagedFrameSchedulerIngressTransaction(
        IPlaybackDiagnosticManagedFrameSchedulerIngressOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticManagedFrameSchedulerIngressCompletion Execute(
        PlaybackDiagnosticManagedFrameCopyTransactionCompletion? source,
        BoundedPlaybackScheduler? scheduler)
    {
        if (source is null || scheduler is null || source.IsDisposed ||
            source.Result != PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced ||
            source.Lease is null || source.Lease.IsDisposed ||
            !IsAcceptedSourceState(source.VideoState, source.VideoFrame) ||
            !IsAcceptedSourceState(source.AudioState, source.AudioFrame) ||
            !source.TryDetachOwnership(out var videoFrame, out var audioFrame, out var lease) ||
            lease is null)
        {
            source?.Dispose();
            return Complete(
                PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.InvalidCompletion);
        }

        PlaybackSchedulerEnqueueResult? videoResult = null;
        PlaybackSchedulerEnqueueResult? audioResult = null;
        var acceptedFrameCount = 0;
        try
        {
            if (videoFrame is not null)
            {
                videoResult = _operations.Enqueue(
                    scheduler,
                    videoFrame,
                    0,
                    PlaybackDecodedFrameDiscontinuityKind.None);
                videoFrame = null;
                if (!IsAcceptedEnqueueResult(videoResult.Value))
                {
                    audioFrame?.Dispose();
                    lease.Dispose();
                    return Complete(
                        PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.VideoEnqueueRejected,
                        videoResult,
                        null,
                        0);
                }

                acceptedFrameCount++;
            }

            if (audioFrame is not null)
            {
                audioResult = _operations.Enqueue(
                    scheduler,
                    audioFrame,
                    1,
                    PlaybackDecodedFrameDiscontinuityKind.None);
                audioFrame = null;
                if (!IsAcceptedEnqueueResult(audioResult.Value))
                {
                    if (acceptedFrameCount == 0)
                    {
                        lease.Dispose();
                        lease = null;
                    }

                    return Complete(
                        PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.AudioEnqueueRejected,
                        videoResult,
                        audioResult,
                        acceptedFrameCount,
                        lease);
                }

                acceptedFrameCount++;
            }

            return Complete(
                PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Advanced,
                videoResult,
                audioResult,
                acceptedFrameCount,
                lease);
        }
        catch
        {
            audioFrame?.Dispose();
            videoFrame?.Dispose();
            if (acceptedFrameCount == 0)
            {
                lease?.Dispose();
                lease = null;
            }

            return Complete(
                PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult.Faulted,
                videoResult,
                audioResult,
                acceptedFrameCount,
                lease);
        }
    }

    private static bool IsAcceptedSourceState(
        PlaybackDecoderFfmpegManagedFrameCopyResult? state,
        PlaybackDecoderFfmpegManagedFrame? frame) =>
        state switch
        {
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied =>
                frame is not null && !frame.IsDisposed,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput => frame is null,
            _ => false,
        };

    private static bool IsAcceptedEnqueueResult(PlaybackSchedulerEnqueueResult result) =>
        result is PlaybackSchedulerEnqueueResult.Enqueued or
            PlaybackSchedulerEnqueueResult.EnqueuedAfterDroppingOldest;

    private static PlaybackDiagnosticManagedFrameSchedulerIngressCompletion Complete(
        PlaybackDiagnosticManagedFrameSchedulerIngressTransactionResult result,
        PlaybackSchedulerEnqueueResult? videoEnqueueResult = null,
        PlaybackSchedulerEnqueueResult? audioEnqueueResult = null,
        int acceptedFrameCount = 0,
        PlaybackDiagnosticNativeDecoderLease? lease = null) =>
        new(result, videoEnqueueResult, audioEnqueueResult, acceptedFrameCount, lease);
}
