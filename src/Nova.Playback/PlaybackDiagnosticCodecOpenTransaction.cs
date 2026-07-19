namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticCodecOpenTransactionResult
{
    Opened,
    InvalidConfiguration,
    VideoOpenRejected,
    AudioOpenRejected,
    Faulted,
}

internal interface IPlaybackDiagnosticCodecOpenOperations
{
    PlaybackDecoderFfmpegCodecOpenResult OpenVideo(
        PlaybackDiagnosticNativeDecoderLease lease);

    PlaybackDecoderFfmpegCodecOpenResult OpenAudio(
        PlaybackDiagnosticNativeDecoderLease lease);
}

internal sealed class PlaybackDiagnosticCodecOpenOperations :
    IPlaybackDiagnosticCodecOpenOperations
{
    private readonly PlaybackDecoderFfmpegCodecOpen _codecOpen = new();

    public PlaybackDecoderFfmpegCodecOpenResult OpenVideo(
        PlaybackDiagnosticNativeDecoderLease lease) =>
        _codecOpen.Open(lease.VideoContext);

    public PlaybackDecoderFfmpegCodecOpenResult OpenAudio(
        PlaybackDiagnosticNativeDecoderLease lease) =>
        _codecOpen.Open(lease.AudioContext);
}

public sealed record PlaybackDiagnosticCodecOpenTransactionCompletion(
    PlaybackDiagnosticCodecOpenTransactionResult Result,
    PlaybackDiagnosticNativeDecoderLease? Lease);

/// <summary>
/// Diagnostic-only finite transaction that opens the independently owned H.264
/// and AAC contexts prepared by 0.0.8h. Video opens before audio. Any rejected
/// or faulted path disposes the complete two-context lease. Packet submission and
/// frame reception are intentionally outside this boundary.
/// </summary>
public sealed class PlaybackDiagnosticCodecOpenTransaction
{
    private readonly IPlaybackDiagnosticCodecOpenOperations _operations;

    public PlaybackDiagnosticCodecOpenTransaction()
        : this(new PlaybackDiagnosticCodecOpenOperations())
    {
    }

    internal PlaybackDiagnosticCodecOpenTransaction(
        IPlaybackDiagnosticCodecOpenOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    public PlaybackDiagnosticCodecOpenTransactionCompletion Execute(
        PlaybackDiagnosticNativeDecoderLease? lease)
    {
        if (lease is null || lease.IsDisposed)
        {
            lease?.Dispose();
            return new(
                PlaybackDiagnosticCodecOpenTransactionResult.InvalidConfiguration,
                null);
        }

        try
        {
            if (_operations.OpenVideo(lease) != PlaybackDecoderFfmpegCodecOpenResult.Opened)
            {
                lease.Dispose();
                return new(
                    PlaybackDiagnosticCodecOpenTransactionResult.VideoOpenRejected,
                    null);
            }

            if (_operations.OpenAudio(lease) != PlaybackDecoderFfmpegCodecOpenResult.Opened)
            {
                lease.Dispose();
                return new(
                    PlaybackDiagnosticCodecOpenTransactionResult.AudioOpenRejected,
                    null);
            }

            return new(PlaybackDiagnosticCodecOpenTransactionResult.Opened, lease);
        }
        catch
        {
            lease.Dispose();
            return new(PlaybackDiagnosticCodecOpenTransactionResult.Faulted, null);
        }
    }
}
