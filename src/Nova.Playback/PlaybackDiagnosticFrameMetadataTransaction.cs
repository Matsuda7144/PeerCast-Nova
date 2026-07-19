namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticFrameMetadataTransactionResult
{
    Advanced,
    InvalidConfiguration,
    VideoSendRejected,
    VideoMetadataRejected,
    AudioSendRejected,
    AudioMetadataRejected,
    Faulted,
}

internal interface IPlaybackDiagnosticFrameMetadataOperations
{
    PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegFrameMetadataResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameMetadataConsumer consumer);

    PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegFrameMetadataResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameMetadataConsumer consumer);
}

internal sealed class PlaybackDiagnosticFrameMetadataOperations :
    IPlaybackDiagnosticFrameMetadataOperations
{
    private readonly PlaybackDecoderFfmpegPacketFrameBoundary _packetBoundary = new();
    private readonly PlaybackDecoderFfmpegFrameMetadataBoundary _metadataBoundary = new();

    public PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.VideoContext, packet);

    public PlaybackDecoderFfmpegFrameMetadataResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameMetadataConsumer consumer) =>
        _metadataBoundary.ReceiveAndExtract(lease.VideoContext, consumer);

    public PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.AudioContext, packet);

    public PlaybackDecoderFfmpegFrameMetadataResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameMetadataConsumer consumer) =>
        _metadataBoundary.ReceiveAndExtract(lease.AudioContext, consumer);
}

public sealed record PlaybackDiagnosticFrameMetadataTransactionCompletion(
    PlaybackDiagnosticFrameMetadataTransactionResult Result,
    PlaybackDecoderFfmpegFrameMetadataResult? VideoState,
    PlaybackDecoderFfmpegFrameMetadata? VideoMetadata,
    PlaybackDecoderFfmpegFrameMetadataResult? AudioState,
    PlaybackDecoderFfmpegFrameMetadata? AudioMetadata,
    PlaybackDiagnosticNativeDecoderLease? Lease);

/// <summary>
/// Diagnostic-only finite transaction that submits one H.264 packet and one AAC
/// packet, then receives at most one callback-scoped native frame per stream and
/// retains only bounded managed metadata. Native frame pointers, plane/sample
/// data, packet loops, output delivery, and flush remain outside this boundary.
/// </summary>
public sealed class PlaybackDiagnosticFrameMetadataTransaction
{
    private readonly IPlaybackDiagnosticFrameMetadataOperations _operations;

    public PlaybackDiagnosticFrameMetadataTransaction()
        : this(new PlaybackDiagnosticFrameMetadataOperations())
    {
    }

    internal PlaybackDiagnosticFrameMetadataTransaction(
        IPlaybackDiagnosticFrameMetadataOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticFrameMetadataTransactionCompletion Execute(
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
                PlaybackDiagnosticFrameMetadataTransactionResult.InvalidConfiguration);
        }

        try
        {
            if (_operations.SendVideo(lease, input.Video.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                lease.Dispose();
                return Complete(
                    PlaybackDiagnosticFrameMetadataTransactionResult.VideoSendRejected);
            }

            var videoConsumer = new CapturingConsumer();
            var videoState = _operations.ReceiveVideo(lease, videoConsumer);
            if (!IsAcceptedMetadataState(videoState, videoConsumer.Metadata))
            {
                lease.Dispose();
                return Complete(
                    PlaybackDiagnosticFrameMetadataTransactionResult.VideoMetadataRejected,
                    videoState,
                    videoConsumer.Metadata);
            }

            if (_operations.SendAudio(lease, input.Audio.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                lease.Dispose();
                return Complete(
                    PlaybackDiagnosticFrameMetadataTransactionResult.AudioSendRejected,
                    videoState,
                    videoConsumer.Metadata);
            }

            var audioConsumer = new CapturingConsumer();
            var audioState = _operations.ReceiveAudio(lease, audioConsumer);
            if (!IsAcceptedMetadataState(audioState, audioConsumer.Metadata))
            {
                lease.Dispose();
                return Complete(
                    PlaybackDiagnosticFrameMetadataTransactionResult.AudioMetadataRejected,
                    videoState,
                    videoConsumer.Metadata,
                    audioState,
                    audioConsumer.Metadata);
            }

            return new(
                PlaybackDiagnosticFrameMetadataTransactionResult.Advanced,
                videoState,
                videoConsumer.Metadata,
                audioState,
                audioConsumer.Metadata,
                lease);
        }
        catch
        {
            lease.Dispose();
            return Complete(PlaybackDiagnosticFrameMetadataTransactionResult.Faulted);
        }
    }

    private static bool IsAcceptedMetadataState(
        PlaybackDecoderFfmpegFrameMetadataResult state,
        PlaybackDecoderFfmpegFrameMetadata? metadata) =>
        state switch
        {
            PlaybackDecoderFfmpegFrameMetadataResult.Extracted => metadata.HasValue,
            PlaybackDecoderFfmpegFrameMetadataResult.NeedMoreInput => !metadata.HasValue,
            _ => false,
        };

    private static PlaybackDiagnosticFrameMetadataTransactionCompletion Complete(
        PlaybackDiagnosticFrameMetadataTransactionResult result,
        PlaybackDecoderFfmpegFrameMetadataResult? videoState = null,
        PlaybackDecoderFfmpegFrameMetadata? videoMetadata = null,
        PlaybackDecoderFfmpegFrameMetadataResult? audioState = null,
        PlaybackDecoderFfmpegFrameMetadata? audioMetadata = null) =>
        new(result, videoState, videoMetadata, audioState, audioMetadata, null);

    private sealed class CapturingConsumer : IPlaybackDecoderFfmpegFrameMetadataConsumer
    {
        public PlaybackDecoderFfmpegFrameMetadata? Metadata { get; private set; }

        public bool Consume(PlaybackDecoderFfmpegFrameMetadata metadata)
        {
            Metadata = metadata;
            return true;
        }
    }
}
