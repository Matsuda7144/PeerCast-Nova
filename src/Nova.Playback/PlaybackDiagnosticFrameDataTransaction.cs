namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticFrameDataTransactionResult
{
    Advanced,
    InvalidConfiguration,
    VideoSendRejected,
    VideoDataRejected,
    AudioSendRejected,
    AudioDataRejected,
    Faulted,
}

public sealed record PlaybackDiagnosticFrameDataSummary(
    PlaybackDecoderFfmpegFrameMetadata Metadata,
    int ViewCount,
    long ReferencedBytes);

public sealed record PlaybackDiagnosticFrameDataTransactionCompletion(
    PlaybackDiagnosticFrameDataTransactionResult Result,
    PlaybackDecoderFfmpegFrameDataResult? VideoState,
    PlaybackDiagnosticFrameDataSummary? VideoSummary,
    PlaybackDecoderFfmpegFrameDataResult? AudioState,
    PlaybackDiagnosticFrameDataSummary? AudioSummary,
    PlaybackDiagnosticNativeDecoderLease? Lease);

internal interface IPlaybackDiagnosticFrameDataOperations
{
    PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegFrameDataResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameDataConsumer consumer);

    PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet);

    PlaybackDecoderFfmpegFrameDataResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameDataConsumer consumer);
}

internal sealed class PlaybackDiagnosticFrameDataOperations :
    IPlaybackDiagnosticFrameDataOperations
{
    private readonly PlaybackDecoderFfmpegPacketFrameBoundary _packetBoundary = new();
    private readonly PlaybackDecoderFfmpegFrameDataBoundary _dataBoundary = new();

    public PlaybackDecoderFfmpegPacketSendResult SendVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.VideoContext, packet);

    public PlaybackDecoderFfmpegFrameDataResult ReceiveVideo(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameDataConsumer consumer) =>
        _dataBoundary.ReceiveAndConsume(lease.VideoContext, consumer);

    public PlaybackDecoderFfmpegPacketSendResult SendAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        ReadOnlyMemory<byte> packet) =>
        _packetBoundary.SendPacket(lease.AudioContext, packet);

    public PlaybackDecoderFfmpegFrameDataResult ReceiveAudio(
        PlaybackDiagnosticNativeDecoderLease lease,
        IPlaybackDecoderFfmpegFrameDataConsumer consumer) =>
        _dataBoundary.ReceiveAndConsume(lease.AudioContext, consumer);
}

public sealed class PlaybackDiagnosticFrameDataTransaction
{
    private readonly IPlaybackDiagnosticFrameDataOperations _operations;

    public PlaybackDiagnosticFrameDataTransaction()
        : this(new PlaybackDiagnosticFrameDataOperations())
    {
    }

    internal PlaybackDiagnosticFrameDataTransaction(
        IPlaybackDiagnosticFrameDataOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticFrameDataTransactionCompletion Execute(
        PlaybackDiagnosticNativeDecoderLease? lease,
        PlaybackDiagnosticEncodedInput? input)
    {
        if (lease is null || lease.IsDisposed || input is null ||
            input.Video.Codec != BoundedDiagnosticPlaybackCodec.H264 ||
            input.Audio.Codec != BoundedDiagnosticPlaybackCodec.Aac ||
            input.Video.Payload.IsEmpty || input.Audio.Payload.IsEmpty)
        {
            lease?.Dispose();
            return Complete(PlaybackDiagnosticFrameDataTransactionResult.InvalidConfiguration);
        }

        try
        {
            if (_operations.SendVideo(lease, input.Video.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                lease.Dispose();
                return Complete(PlaybackDiagnosticFrameDataTransactionResult.VideoSendRejected);
            }

            var videoConsumer = new SummaryConsumer();
            var videoState = _operations.ReceiveVideo(lease, videoConsumer);
            if (!IsAccepted(videoState, videoConsumer.Summary))
            {
                lease.Dispose();
                return Complete(
                    PlaybackDiagnosticFrameDataTransactionResult.VideoDataRejected,
                    videoState,
                    videoConsumer.Summary);
            }

            if (_operations.SendAudio(lease, input.Audio.Payload) !=
                PlaybackDecoderFfmpegPacketSendResult.Sent)
            {
                lease.Dispose();
                return Complete(
                    PlaybackDiagnosticFrameDataTransactionResult.AudioSendRejected,
                    videoState,
                    videoConsumer.Summary);
            }

            var audioConsumer = new SummaryConsumer();
            var audioState = _operations.ReceiveAudio(lease, audioConsumer);
            if (!IsAccepted(audioState, audioConsumer.Summary))
            {
                lease.Dispose();
                return Complete(
                    PlaybackDiagnosticFrameDataTransactionResult.AudioDataRejected,
                    videoState,
                    videoConsumer.Summary,
                    audioState,
                    audioConsumer.Summary);
            }

            return new(
                PlaybackDiagnosticFrameDataTransactionResult.Advanced,
                videoState,
                videoConsumer.Summary,
                audioState,
                audioConsumer.Summary,
                lease);
        }
        catch
        {
            lease.Dispose();
            return Complete(PlaybackDiagnosticFrameDataTransactionResult.Faulted);
        }
    }

    private static bool IsAccepted(
        PlaybackDecoderFfmpegFrameDataResult state,
        PlaybackDiagnosticFrameDataSummary? summary) =>
        state switch
        {
            PlaybackDecoderFfmpegFrameDataResult.Processed => summary is { ViewCount: > 0, ReferencedBytes: > 0 },
            PlaybackDecoderFfmpegFrameDataResult.NeedMoreInput => summary is null,
            _ => false,
        };

    private static PlaybackDiagnosticFrameDataTransactionCompletion Complete(
        PlaybackDiagnosticFrameDataTransactionResult result,
        PlaybackDecoderFfmpegFrameDataResult? videoState = null,
        PlaybackDiagnosticFrameDataSummary? videoSummary = null,
        PlaybackDecoderFfmpegFrameDataResult? audioState = null,
        PlaybackDiagnosticFrameDataSummary? audioSummary = null) =>
        new(result, videoState, videoSummary, audioState, audioSummary, null);

    private sealed class SummaryConsumer : IPlaybackDecoderFfmpegFrameDataConsumer
    {
        private PlaybackDecoderFfmpegFrameMetadata? _metadata;
        private int _viewCount;
        private long _referencedBytes;

        public PlaybackDiagnosticFrameDataSummary? Summary =>
            _metadata.HasValue
                ? new(_metadata.Value, _viewCount, _referencedBytes)
                : null;

        public bool Consume(
            PlaybackDecoderFfmpegFrameMetadata metadata,
            PlaybackDecoderFfmpegFrameDataView data)
        {
            if (_metadata.HasValue && _metadata.Value != metadata)
            {
                return false;
            }

            try
            {
                _metadata = metadata;
                _viewCount = checked(_viewCount + 1);
                _referencedBytes = checked(_referencedBytes + data.Length);
                return data.Length > 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
