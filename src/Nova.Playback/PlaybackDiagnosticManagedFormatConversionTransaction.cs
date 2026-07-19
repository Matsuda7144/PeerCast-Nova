namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticManagedFormatConversionTransactionResult
{
    Advanced,
    InvalidConfiguration,
    VideoSendRejected,
    VideoCopyRejected,
    VideoConversionRejected,
    AudioSendRejected,
    AudioCopyRejected,
    AudioConversionRejected,
    Faulted,
}

public enum PlaybackDiagnosticConvertedOutputKind
{
    Video,
    Audio,
}

public readonly record struct PlaybackDiagnosticConvertedOutputSummary(
    PlaybackDiagnosticConvertedOutputKind Kind,
    long Sequence);

internal interface IPlaybackDiagnosticManagedFormatConversionOperations
{
    PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer);

    PlaybackDecoderFfmpegManagedFormatConversionResult ConvertVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecoderFfmpegDecodedOutputConsumer consumer);

    PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer);

    PlaybackDecoderFfmpegManagedFormatConversionResult ConvertAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecoderFfmpegDecodedOutputConsumer consumer);
}

internal sealed class PlaybackDiagnosticManagedFormatConversionOperations :
    IPlaybackDiagnosticManagedFormatConversionOperations
{
    internal const long VideoSequence = 0;
    internal const long AudioSequence = 1;

    private readonly PlaybackDecoderFfmpegPacketFrameBoundary _packetBoundary = new();
    private readonly PlaybackDecoderFfmpegManagedFrameCopyBoundary _copyBoundary = new();
    private readonly PlaybackDecoderFfmpegManagedFormatConversionBoundary _conversion = new();

    public PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.VideoContext, packet);

    public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer) =>
        _copyBoundary.ReceiveAndCopy(lease.VideoContext, consumer);

    public PlaybackDecoderFfmpegManagedFormatConversionResult ConvertVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecoderFfmpegDecodedOutputConsumer consumer) =>
        _conversion.Convert(
            lease.VideoContext,
            frame,
            VideoSequence,
            PlaybackDecodedFrameDiscontinuityKind.None,
            consumer);

    public PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.AudioContext, packet);

    public PlaybackDecoderFfmpegManagedFrameCopyResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegManagedFrameConsumer consumer) =>
        _copyBoundary.ReceiveAndCopy(lease.AudioContext, consumer);

    public PlaybackDecoderFfmpegManagedFormatConversionResult ConvertAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDecoderFfmpegManagedFrame frame,
        IPlaybackDecoderFfmpegDecodedOutputConsumer consumer) =>
        _conversion.Convert(
            lease.AudioContext,
            frame,
            AudioSequence,
            PlaybackDecodedFrameDiscontinuityKind.None,
            consumer);
}

public sealed record PlaybackDiagnosticManagedFormatConversionTransactionCompletion(
    PlaybackDiagnosticManagedFormatConversionTransactionResult Result,
    PlaybackDecoderFfmpegManagedFrameCopyResult? VideoCopyState,
    PlaybackDecoderFfmpegManagedFormatConversionResult? VideoConversionState,
    PlaybackDiagnosticConvertedOutputSummary? VideoOutput,
    PlaybackDecoderFfmpegManagedFrameCopyResult? AudioCopyState,
    PlaybackDecoderFfmpegManagedFormatConversionResult? AudioConversionState,
    PlaybackDiagnosticConvertedOutputSummary? AudioOutput,
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
/// packet, copies at most one managed frame per stream, converts callback-scoped
/// canonical output, and retains summaries only. Converted payload never escapes
/// the conversion callback and copied input frames are disposed before return.
/// </summary>
public sealed class PlaybackDiagnosticManagedFormatConversionTransaction
{
    private readonly IPlaybackDiagnosticManagedFormatConversionOperations _operations;

    public PlaybackDiagnosticManagedFormatConversionTransaction()
        : this(new PlaybackDiagnosticManagedFormatConversionOperations())
    {
    }

    internal PlaybackDiagnosticManagedFormatConversionTransaction(
        IPlaybackDiagnosticManagedFormatConversionOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticManagedFormatConversionTransactionCompletion Execute(
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
                PlaybackDiagnosticManagedFormatConversionTransactionResult.InvalidConfiguration);
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
                    PlaybackDiagnosticManagedFormatConversionTransactionResult.VideoSendRejected);
            }

            var videoFrameConsumer = new FrameConsumer();
            var videoCopyState = _operations.ReceiveVideo(lease, videoFrameConsumer);
            videoFrame = videoFrameConsumer.Frame;
            if (!IsAcceptedCopyState(videoCopyState, videoFrame))
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticManagedFormatConversionTransactionResult.VideoCopyRejected,
                    videoCopyState: videoCopyState,
                    videoFrame: videoFrame);
            }

            PlaybackDecoderFfmpegManagedFormatConversionResult? videoConversionState = null;
            PlaybackDiagnosticConvertedOutputSummary? videoOutput = null;
            if (videoFrame is not null)
            {
                var videoOutputConsumer = new OutputConsumer();
                videoConversionState = _operations.ConvertVideo(
                    lease,
                    videoFrame,
                    videoOutputConsumer);
                videoOutput = videoOutputConsumer.Summary;
                videoFrame.Dispose();
                videoFrame = null;
                if (!IsAcceptedConversionState(
                    videoConversionState.Value,
                    PlaybackDiagnosticConvertedOutputKind.Video,
                    videoOutput))
                {
                    return Reject(
                        lease,
                        PlaybackDiagnosticManagedFormatConversionTransactionResult.VideoConversionRejected,
                        videoCopyState,
                        videoConversionState,
                        videoOutput);
                }
            }

            if (_operations.SendAudio(lease, input.Audio.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticManagedFormatConversionTransactionResult.AudioSendRejected,
                    videoCopyState,
                    videoConversionState,
                    videoOutput);
            }

            var audioFrameConsumer = new FrameConsumer();
            var audioCopyState = _operations.ReceiveAudio(lease, audioFrameConsumer);
            audioFrame = audioFrameConsumer.Frame;
            if (!IsAcceptedCopyState(audioCopyState, audioFrame))
            {
                return Reject(
                    lease,
                    PlaybackDiagnosticManagedFormatConversionTransactionResult.AudioCopyRejected,
                    videoCopyState,
                    videoConversionState,
                    videoOutput,
                    audioCopyState,
                    audioFrame: audioFrame);
            }

            PlaybackDecoderFfmpegManagedFormatConversionResult? audioConversionState = null;
            PlaybackDiagnosticConvertedOutputSummary? audioOutput = null;
            if (audioFrame is not null)
            {
                var audioOutputConsumer = new OutputConsumer();
                audioConversionState = _operations.ConvertAudio(
                    lease,
                    audioFrame,
                    audioOutputConsumer);
                audioOutput = audioOutputConsumer.Summary;
                audioFrame.Dispose();
                audioFrame = null;
                if (!IsAcceptedConversionState(
                    audioConversionState.Value,
                    PlaybackDiagnosticConvertedOutputKind.Audio,
                    audioOutput))
                {
                    return Reject(
                        lease,
                        PlaybackDiagnosticManagedFormatConversionTransactionResult.AudioConversionRejected,
                        videoCopyState,
                        videoConversionState,
                        videoOutput,
                        audioCopyState,
                        audioConversionState,
                        audioOutput);
                }
            }

            return new(
                PlaybackDiagnosticManagedFormatConversionTransactionResult.Advanced,
                videoCopyState,
                videoConversionState,
                videoOutput,
                audioCopyState,
                audioConversionState,
                audioOutput,
                lease);
        }
        catch
        {
            audioFrame?.Dispose();
            videoFrame?.Dispose();
            lease.Dispose();
            return Complete(PlaybackDiagnosticManagedFormatConversionTransactionResult.Faulted);
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

    private static bool IsAcceptedConversionState(
        PlaybackDecoderFfmpegManagedFormatConversionResult state,
        PlaybackDiagnosticConvertedOutputKind expectedKind,
        PlaybackDiagnosticConvertedOutputSummary? output) =>
        state == PlaybackDecoderFfmpegManagedFormatConversionResult.Converted &&
        output is { } summary && summary.Kind == expectedKind;

    private static PlaybackDiagnosticManagedFormatConversionTransactionCompletion Reject(
        PlaybackDiagnosticNativeDecoderLease lease,
        PlaybackDiagnosticManagedFormatConversionTransactionResult result,
        PlaybackDecoderFfmpegManagedFrameCopyResult? videoCopyState = null,
        PlaybackDecoderFfmpegManagedFormatConversionResult? videoConversionState = null,
        PlaybackDiagnosticConvertedOutputSummary? videoOutput = null,
        PlaybackDecoderFfmpegManagedFrameCopyResult? audioCopyState = null,
        PlaybackDecoderFfmpegManagedFormatConversionResult? audioConversionState = null,
        PlaybackDiagnosticConvertedOutputSummary? audioOutput = null,
        PlaybackDecoderFfmpegManagedFrame? videoFrame = null,
        PlaybackDecoderFfmpegManagedFrame? audioFrame = null)
    {
        audioFrame?.Dispose();
        videoFrame?.Dispose();
        lease.Dispose();
        return new(
            result,
            videoCopyState,
            videoConversionState,
            videoOutput,
            audioCopyState,
            audioConversionState,
            audioOutput,
            null);
    }

    private static PlaybackDiagnosticManagedFormatConversionTransactionCompletion Complete(
        PlaybackDiagnosticManagedFormatConversionTransactionResult result) =>
        new(result, null, null, null, null, null, null, null);

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

    private sealed class OutputConsumer : IPlaybackDecoderFfmpegDecodedOutputConsumer
    {
        public PlaybackDiagnosticConvertedOutputSummary? Summary { get; private set; }

        public bool ConsumeAudio(PlaybackDecodedAudioFrame frame) =>
            Capture(PlaybackDiagnosticConvertedOutputKind.Audio,
                PlaybackDiagnosticManagedFormatConversionOperations.AudioSequence);

        public bool ConsumeVideo(PlaybackDecodedVideoFrame frame) =>
            Capture(PlaybackDiagnosticConvertedOutputKind.Video,
                PlaybackDiagnosticManagedFormatConversionOperations.VideoSequence);

        private bool Capture(PlaybackDiagnosticConvertedOutputKind kind, long sequence)
        {
            if (Summary.HasValue)
            {
                return false;
            }

            Summary = new(kind, sequence);
            return true;
        }
    }
}
