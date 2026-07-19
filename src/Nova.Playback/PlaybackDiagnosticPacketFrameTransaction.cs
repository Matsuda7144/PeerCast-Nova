namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticPacketFrameTransactionResult
{
    Advanced,
    InvalidConfiguration,
    VideoSendRejected,
    VideoReceiveRejected,
    AudioSendRejected,
    AudioReceiveRejected,
    Faulted,
}

internal interface IPlaybackDiagnosticPacketFrameOperations
{
    PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegFrameReceiveResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease);

    PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegFrameReceiveResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease);
}

internal sealed class PlaybackDiagnosticPacketFrameOperations :
    IPlaybackDiagnosticPacketFrameOperations
{
    private readonly PlaybackDecoderFfmpegPacketFrameBoundary _boundary = new();

    public PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _boundary.SendPacket(lease.VideoContext, packet);

    public PlaybackDecoderFfmpegFrameReceiveResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease) =>
        _boundary.ReceiveFrame(lease.VideoContext);

    public PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _boundary.SendPacket(lease.AudioContext, packet);

    public PlaybackDecoderFfmpegFrameReceiveResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease) =>
        _boundary.ReceiveFrame(lease.AudioContext);
}

public sealed record PlaybackDiagnosticPacketFrameTransactionCompletion(
    PlaybackDiagnosticPacketFrameTransactionResult Result,
    PlaybackDecoderFfmpegFrameReceiveResult? VideoFrameState,
    PlaybackDecoderFfmpegFrameReceiveResult? AudioFrameState,
    PlaybackDiagnosticNativeDecoderLease? Lease);

/// <summary>
/// Diagnostic-only finite transaction that submits exactly one bounded H.264
/// packet and one bounded AAC packet to the independently owned, opened contexts
/// prepared by 0.0.8i. It performs one receive attempt after each send. Demux,
/// packet loops, frame extraction, output delivery, and flush are outside this
/// boundary.
/// </summary>
public sealed class PlaybackDiagnosticPacketFrameTransaction
{
    private readonly IPlaybackDiagnosticPacketFrameOperations _operations;

    public PlaybackDiagnosticPacketFrameTransaction()
        : this(new PlaybackDiagnosticPacketFrameOperations())
    {
    }

    internal PlaybackDiagnosticPacketFrameTransaction(
        IPlaybackDiagnosticPacketFrameOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticPacketFrameTransactionCompletion Execute(
        PlaybackDiagnosticNativeDecoderLease? lease,
        PlaybackDiagnosticEncodedInput? input)
    {
        if (lease is null || lease.IsDisposed || input is null ||
            input.Video.Codec != BoundedDiagnosticPlaybackCodec.H264 ||
            input.Audio.Codec != BoundedDiagnosticPlaybackCodec.Aac ||
            input.Video.Payload.IsEmpty || input.Audio.Payload.IsEmpty)
        {
            lease?.Dispose();
            return new(
                PlaybackDiagnosticPacketFrameTransactionResult.InvalidConfiguration,
                null,
                null,
                null);
        }

        try
        {
            if (_operations.SendVideo(lease, input.Video.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                lease.Dispose();
                return new(
                    PlaybackDiagnosticPacketFrameTransactionResult.VideoSendRejected,
                    null,
                    null,
                    null);
            }

            var videoFrameState = _operations.ReceiveVideo(lease);
            if (!IsAcceptedReceiveState(videoFrameState))
            {
                lease.Dispose();
                return new(
                    PlaybackDiagnosticPacketFrameTransactionResult.VideoReceiveRejected,
                    videoFrameState,
                    null,
                    null);
            }

            if (_operations.SendAudio(lease, input.Audio.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                lease.Dispose();
                return new(
                    PlaybackDiagnosticPacketFrameTransactionResult.AudioSendRejected,
                    videoFrameState,
                    null,
                    null);
            }

            var audioFrameState = _operations.ReceiveAudio(lease);
            if (!IsAcceptedReceiveState(audioFrameState))
            {
                lease.Dispose();
                return new(
                    PlaybackDiagnosticPacketFrameTransactionResult.AudioReceiveRejected,
                    videoFrameState,
                    audioFrameState,
                    null);
            }

            return new(
                PlaybackDiagnosticPacketFrameTransactionResult.Advanced,
                videoFrameState,
                audioFrameState,
                lease);
        }
        catch
        {
            lease.Dispose();
            return new(
                PlaybackDiagnosticPacketFrameTransactionResult.Faulted,
                null,
                null,
                null);
        }
    }

    private static bool IsAcceptedReceiveState(
        PlaybackDecoderFfmpegFrameReceiveResult result) =>
        result is PlaybackDecoderFfmpegFrameReceiveResult.FrameAvailable or
            PlaybackDecoderFfmpegFrameReceiveResult.NeedMoreInput;
}
