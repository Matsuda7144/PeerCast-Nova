namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticManagedFrameCopyTransactionResult
{
    Advanced,
    InvalidConfiguration,
    VideoSendRejected,
    VideoCopyRejected,
    AudioSendRejected,
    AudioCopyRejected,
    Faulted,
}

internal interface IPlaybackDiagnosticManagedFrameCopyOperations
{
    PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer);

    PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer);
}

internal sealed class PlaybackDiagnosticManagedFrameCopyOperations :
    IPlaybackDiagnosticManagedFrameCopyOperations
{
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

    public PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.AudioContext, packet);

    public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer) =>
        _copyBoundary.ReceiveAndCopy(lease.AudioContext, consumer);
}

public sealed class PlaybackDiagnosticManagedFrameCopyTransactionCompletion : IDisposable
{
    private int _disposed;

    internal PlaybackDiagnosticManagedFrameCopyTransactionCompletion(
        PlaybackDiagnosticManagedFrameCopyTransactionResult result,
        PlaybackDecoderFfmpegManagedFrameCopyResult? videoState,
        PlaybackDecoderFfmpegManagedFrame? videoFrame,
        PlaybackDecoderFfmpegManagedFrameCopyResult? audioState,
        PlaybackDecoderFfmpegManagedFrame? audioFrame,
        PlaybackDiagnosticNativeDecoderLease? lease)
    {
        Result = result;
        VideoState = videoState;
        VideoFrame = videoFrame;
        AudioState = audioState;
        AudioFrame = audioFrame;
        Lease = lease;
    }

    public PlaybackDiagnosticManagedFrameCopyTransactionResult Result { get; }

    public PlaybackDecoderFfmpegManagedFrameCopyResult? VideoState { get; }

    public PlaybackDecoderFfmpegManagedFrame? VideoFrame { get; }

    public PlaybackDecoderFfmpegManagedFrameCopyResult? AudioState { get; }

    public PlaybackDecoderFfmpegManagedFrame? AudioFrame { get; }

    public PlaybackDiagnosticNativeDecoderLease? Lease { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        AudioFrame?.Dispose();
        VideoFrame?.Dispose();
        Lease?.Dispose();
    }
}

/// <summary>
/// Diagnostic-only finite transaction that sends one H.264 packet and one AAC
/// packet and retains at most one owned managed frame copy per stream. Native
/// frame and plane/sample pointers remain callback-scoped in lower boundaries.
/// </summary>
public sealed class PlaybackDiagnosticManagedFrameCopyTransaction
{
    private readonly IPlaybackDiagnosticManagedFrameCopyOperations _operations;

    public PlaybackDiagnosticManagedFrameCopyTransaction()
        : this(new PlaybackDiagnosticManagedFrameCopyOperations())
    {
    }

    internal PlaybackDiagnosticManagedFrameCopyTransaction(
        IPlaybackDiagnosticManagedFrameCopyOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticManagedFrameCopyTransactionCompletion Execute(
        PlaybackDiagnosticNativeDecoderLease? lease,
        PlaybackDiagnosticEncodedInput? input)
    {
        if (lease is null || lease.IsDisposed || input is null ||
            input.Video.Codec != BoundedDiagnosticPlaybackCodec.H264 ||
            input.Audio.Codec != BoundedDiagnosticPlaybackCodec.Aac ||
            input.Video.Payload.IsEmpty || input.Audio.Payload.IsEmpty)
        {
            lease?.Dispose();
            return Complete(
                PlaybackDiagnosticManagedFrameCopyTransactionResult.InvalidConfiguration);
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
                    PlaybackDiagnosticManagedFrameCopyTransactionResult.VideoSendRejected);
            }

            var videoConsumer = new CapturingConsumer();
            var videoState = _operations.ReceiveVideo(lease, videoConsumer);
            videoFrame = videoConsumer.Frame;
            if (!IsAcceptedCopyState(videoState, videoFrame))
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticManagedFrameCopyTransactionResult.VideoCopyRejected,
                    videoState,
                    videoFrame);
            }

            if (_operations.SendAudio(lease, input.Audio.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticManagedFrameCopyTransactionResult.AudioSendRejected,
                    videoState,
                    videoFrame);
            }

            var audioConsumer = new CapturingConsumer();
            var audioState = _operations.ReceiveAudio(lease, audioConsumer);
            audioFrame = audioConsumer.Frame;
            if (!IsAcceptedCopyState(audioState, audioFrame))
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticManagedFrameCopyTransactionResult.AudioCopyRejected,
                    videoState,
                    videoFrame,
                    audioState,
                    audioFrame);
            }

            return new(
                PlaybackDiagnosticManagedFrameCopyTransactionResult.Advanced,
                videoState,
                videoFrame,
                audioState,
                audioFrame,
                lease);
        }
        catch
        {
            audioFrame?.Dispose();
            videoFrame?.Dispose();
            lease.Dispose();
            return Complete(PlaybackDiagnosticManagedFrameCopyTransactionResult.Faulted);
        }
    }

    private static bool IsAcceptedCopyState(
        PlaybackDecoderFfmpegManagedFrameCopyResult state,
        PlaybackDecoderFfmpegManagedFrame? frame) =>
        state switch
        {
            PlaybackDecoderFfmpegManagedFrameCopyResult.Copied =>
                frame is not null && !frame.IsDisposed,
            PlaybackDecoderFfmpegManagedFrameCopyResult.NeedMoreInput => frame is null,
            _ => false,
        };

    private static PlaybackDiagnosticManagedFrameCopyTransactionCompletion Reject(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDiagnosticManagedFrameCopyTransactionResult result,
        PlaybackDecoderFfmpegManagedFrameCopyResult? videoState = null,
        PlaybackDecoderFfmpegManagedFrame? videoFrame = null,
        PlaybackDecoderFfmpegManagedFrameCopyResult? audioState = null,
        PlaybackDecoderFfmpegManagedFrame? audioFrame = null)
    {
        audioFrame?.Dispose();
        videoFrame?.Dispose();
        lease.Dispose();
        return Complete(result, videoState, audioState);
    }

    private static PlaybackDiagnosticManagedFrameCopyTransactionCompletion Complete(
        PlaybackDiagnosticManagedFrameCopyTransactionResult result,
        PlaybackDecoderFfmpegManagedFrameCopyResult? videoState = null,
        PlaybackDecoderFfmpegManagedFrameCopyResult? audioState = null) =>
        new(result, videoState, null, audioState, null, null);

    private sealed class CapturingConsumer : IPlaybackDecoderFfmpegManagedFrameConsumer
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
