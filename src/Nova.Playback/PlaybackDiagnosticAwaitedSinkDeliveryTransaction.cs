namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult
{
    Advanced,
    InvalidConfiguration,
    Cancelled,
    VideoSendRejected,
    VideoCopyRejected,
    VideoDeliveryRejected,
    AudioSendRejected,
    AudioCopyRejected,
    AudioDeliveryRejected,
    Faulted,
}

internal interface IPlaybackDiagnosticAwaitedSinkDeliveryOperations
{
    PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer);

    ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverVideoAsync(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken);

    PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer);

    ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverAudioAsync(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken);
}

internal sealed class PlaybackDiagnosticAwaitedSinkDeliveryOperations :
    IPlaybackDiagnosticAwaitedSinkDeliveryOperations
{
    internal const long VideoSequence = 0;
    internal const long AudioSequence = 1;

    private readonly PlaybackDecoderFfmpegPacketFrameBoundary _packetBoundary = new();
    private readonly PlaybackDecoderFfmpegManagedFrameCopyBoundary _copyBoundary = new();

    public PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.VideoContext, packet);

    public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer) =>
        _copyBoundary.ReceiveAndCopy(lease.VideoContext, consumer);

    public async ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverVideoAsync(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken)
    {
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        return await boundary.DeliverAsync(
            lease.VideoContext,
            frame,
            VideoSequence,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            cancellationToken).ConfigureAwait(false);
    }

    public PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.AudioContext, packet);

    public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer) =>
        _copyBoundary.ReceiveAndCopy(lease.AudioContext, consumer);

    public async ValueTask<PlaybackDecoderFfmpegAwaitedSinkDeliveryResult> DeliverAudioAsync(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecodedFrameSink sink,
        CancellationToken cancellationToken)
    {
        using var boundary = new PlaybackDecoderFfmpegAwaitedSinkDeliveryBoundary();
        return await boundary.DeliverAsync(
            lease.AudioContext,
            frame,
            AudioSequence,
            PlaybackDecodedFrameDiscontinuityKind.None,
            sink,
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed record PlaybackDiagnosticAwaitedSinkDeliveryTransactionCompletion(
    PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult Result,
    PlaybackDecoderFfmpegManagedFrameCopyResult? VideoCopyState,
    PlaybackDecoderFfmpegAwaitedSinkDeliveryResult? VideoDeliveryState,
    PlaybackDecoderFfmpegManagedFrameCopyResult? AudioCopyState,
    PlaybackDecoderFfmpegAwaitedSinkDeliveryResult? AudioDeliveryState,
    PlaybackDiagnosticNativeDecoderLease? Lease) : IDisposable
{
    private int _disposed;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Lease?.Dispose();
    }
}

/// <summary>
/// Diagnostic-only finite transaction that sends one H.264 packet and one AAC
/// packet, copies at most one managed frame per stream, and serially awaits the
/// existing decoded sink delivery boundary. Callback payload and copied frames
/// never survive transaction return. No queue or background task is introduced.
/// </summary>
public sealed class PlaybackDiagnosticAwaitedSinkDeliveryTransaction
{
    private readonly IPlaybackDiagnosticAwaitedSinkDeliveryOperations _operations;

    public PlaybackDiagnosticAwaitedSinkDeliveryTransaction()
        : this(new PlaybackDiagnosticAwaitedSinkDeliveryOperations())
    {
    }

    internal PlaybackDiagnosticAwaitedSinkDeliveryTransaction(
        IPlaybackDiagnosticAwaitedSinkDeliveryOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public async ValueTask<PlaybackDiagnosticAwaitedSinkDeliveryTransactionCompletion> ExecuteAsync(
        PlaybackDiagnosticNativeDecoderLease? lease,
        PlaybackDiagnosticEncodedInput? input,
        IPlaybackDecodedFrameSink? sink,
        CancellationToken cancellationToken)
    {
        if (lease is null || lease.IsDisposed || input is null || sink is null ||
            input.Video.Codec != BoundedDiagnosticPlaybackCodec.H264 ||
            input.Audio.Codec != BoundedDiagnosticPlaybackCodec.Aac ||
            input.Video.Payload.IsEmpty || input.Audio.Payload.IsEmpty)
        {
            lease?.Dispose();
            return Complete(
                PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.InvalidConfiguration);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            lease.Dispose();
            return Complete(PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Cancelled);
        }

        PlaybackDecoderFfmpegManagedFrame? videoFrame = null;
        PlaybackDecoderFfmpegManagedFrame? audioFrame = null;
        try
        {
            if (_operations.SendVideo(lease, input.Video.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.VideoSendRejected);
            }

            var videoConsumer = new FrameConsumer();
            var videoCopyState = _operations.ReceiveVideo(lease, videoConsumer);
            videoFrame = videoConsumer.Frame;
            if (!IsAcceptedCopyState(
                videoCopyState,
                PlaybackDecoderFfmpegFrameMediaKind.Video,
                videoFrame))
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.VideoCopyRejected,
                    videoCopyState: videoCopyState,
                    videoFrame: videoFrame);
            }

            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult? videoDeliveryState = null;
            if (videoFrame is not null)
            {
                videoDeliveryState = await _operations.DeliverVideoAsync(
                    lease,
                    videoFrame,
                    sink,
                    cancellationToken).ConfigureAwait(false);
                videoFrame.Dispose();
                videoFrame = null;
                if (videoDeliveryState == PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled)
                {
                    return Reject(
                        lease,
                        PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Cancelled,
                        videoCopyState,
                        videoDeliveryState);
                }

                if (videoDeliveryState != PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered)
                {
                    return Reject(
                        lease,
                        PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.VideoDeliveryRejected,
                        videoCopyState,
                        videoDeliveryState);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Cancelled,
                    videoCopyState,
                    videoDeliveryState);
            }

            if (_operations.SendAudio(lease, input.Audio.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.AudioSendRejected,
                    videoCopyState,
                    videoDeliveryState);
            }

            var audioConsumer = new FrameConsumer();
            var audioCopyState = _operations.ReceiveAudio(lease, audioConsumer);
            audioFrame = audioConsumer.Frame;
            if (!IsAcceptedCopyState(
                audioCopyState,
                PlaybackDecoderFfmpegFrameMediaKind.Audio,
                audioFrame))
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.AudioCopyRejected,
                    videoCopyState,
                    videoDeliveryState,
                    audioCopyState,
                    audioFrame: audioFrame);
            }

            PlaybackDecoderFfmpegAwaitedSinkDeliveryResult? audioDeliveryState = null;
            if (audioFrame is not null)
            {
                audioDeliveryState = await _operations.DeliverAudioAsync(
                    lease,
                    audioFrame,
                    sink,
                    cancellationToken).ConfigureAwait(false);
                audioFrame.Dispose();
                audioFrame = null;
                if (audioDeliveryState == PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Cancelled)
                {
                    return Reject(
                        lease,
                        PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Cancelled,
                        videoCopyState,
                        videoDeliveryState,
                        audioCopyState,
                        audioDeliveryState);
                }

                if (audioDeliveryState != PlaybackDecoderFfmpegAwaitedSinkDeliveryResult.Delivered)
                {
                    return Reject(
                        lease,
                        PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.AudioDeliveryRejected,
                        videoCopyState,
                        videoDeliveryState,
                        audioCopyState,
                        audioDeliveryState);
                }
            }

            return new(
                PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Advanced,
                videoCopyState,
                videoDeliveryState,
                audioCopyState,
                audioDeliveryState,
                lease);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            audioFrame?.Dispose();
            videoFrame?.Dispose();
            lease.Dispose();
            return Complete(PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Cancelled);
        }
        catch
        {
            audioFrame?.Dispose();
            videoFrame?.Dispose();
            lease.Dispose();
            return Complete(PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult.Faulted);
        }
    }

    private static bool IsAcceptedCopyState(
        PlaybackDecoderFfmpegManagedFrameCopyResult state,
        PlaybackDecoderFfmpegFrameMediaKind expectedKind,
        PlaybackDecoderFfmpegManagedFrame? frame) =>
        state switch
        {
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied =>
                frame is not null && !frame.IsDisposed &&
                frame.Metadata.MediaKind == expectedKind,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput => frame is null,
            _ => false,
        };

    private static PlaybackDiagnosticAwaitedSinkDeliveryTransactionCompletion Reject(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult result,
        PlaybackDecoderFfmpegManagedFrameCopyResult? videoCopyState = null,
        PlaybackDecoderFfmpegAwaitedSinkDeliveryResult? videoDeliveryState = null,
        PlaybackDecoderFfmpegManagedFrameCopyResult? audioCopyState = null,
        PlaybackDecoderFfmpegAwaitedSinkDeliveryResult? audioDeliveryState = null,
        PlaybackDecoderFfmpegManagedFrame? videoFrame = null,
        PlaybackDecoderFfmpegManagedFrame? audioFrame = null)
    {
        audioFrame?.Dispose();
        videoFrame?.Dispose();
        lease.Dispose();
        return new(
            result,
            videoCopyState,
            videoDeliveryState,
            audioCopyState,
            audioDeliveryState,
            null);
    }

    private static PlaybackDiagnosticAwaitedSinkDeliveryTransactionCompletion Complete(
        PlaybackDiagnosticAwaitedSinkDeliveryTransactionResult result) =>
        new(result, null, null, null, null, null);

    private sealed class FrameConsumer : IPlaybackDecoderFfmpegManagedFrameConsumer
    {
        public PlaybackDecoderFfmpegManagedFrame? Frame { get; private set; }

        public bool Consume(PlaybackDecoderFfmpegManagedFrame frame)
        {
            if (Frame is not null)
            {
                return false;
            }

            Frame = frame;
            return true;
        }
    }
}
