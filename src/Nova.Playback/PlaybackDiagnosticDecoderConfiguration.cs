namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticDecoderConfigurationResult
{
    Prepared,
    InvalidConfiguration,
    VideoParametersRejected,
    AudioParametersRejected,
    Faulted,
}

public sealed record PlaybackDiagnosticDecoderConfiguration
{
    public int VideoWidth { get; init; }

    public int VideoHeight { get; init; }

    public ReadOnlyMemory<byte> H264Extradata { get; init; }

    public int AudioSampleRate { get; init; }

    public int AudioChannelCount { get; init; }

    public ReadOnlyMemory<byte> AacExtradata { get; init; }

    public static PlaybackDiagnosticDecoderConfiguration Baseline { get; } = new()
    {
        VideoWidth = 16,
        VideoHeight = 16,
        H264Extradata = new byte[]
        {
            0x01, 0x42, 0x00, 0x1E, 0xFF, 0xE1, 0x00, 0x0A,
            0x67, 0x42, 0x00, 0x1E, 0x95, 0xA8, 0x28, 0x0F,
            0x00, 0x44, 0x01, 0x00, 0x04, 0x68, 0xCE, 0x3C, 0x80,
        },
        AudioSampleRate = 48_000,
        AudioChannelCount = 2,
        AacExtradata = new byte[] { 0x11, 0x90 },
    };
}

public sealed class PlaybackDiagnosticDecoderParameterLease : IDisposable, IAsyncDisposable
{
    private PlaybackDecoderFfmpegCodecParametersLease? _video;
    private PlaybackDecoderFfmpegCodecParametersLease? _audio;

    internal PlaybackDiagnosticDecoderParameterLease(
        PlaybackDecoderFfmpegCodecParametersLease video,
        PlaybackDecoderFfmpegCodecParametersLease audio)
    {
        _video = video;
        _audio = audio;
    }

    public bool IsDisposed => _video is null || _audio is null;

    internal PlaybackDecoderFfmpegCodecParametersLease Video =>
        _video ?? throw new ObjectDisposedException(nameof(PlaybackDiagnosticDecoderParameterLease));

    internal PlaybackDecoderFfmpegCodecParametersLease Audio =>
        _audio ?? throw new ObjectDisposedException(nameof(PlaybackDiagnosticDecoderParameterLease));

    public void Dispose()
    {
        var audio = Interlocked.Exchange(ref _audio, null);
        var video = Interlocked.Exchange(ref _video, null);
        try
        {
            audio?.Dispose();
        }
        finally
        {
            video?.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed record PlaybackDiagnosticDecoderParameterPreparation(
    PlaybackDiagnosticDecoderConfigurationResult Result,
    PlaybackDiagnosticDecoderParameterLease? Lease);

/// <summary>
/// Converts explicit, bounded diagnostic decoder metadata into the existing
/// managed codec-parameter ownership boundary. It performs no native call.
/// </summary>
public static class PlaybackDiagnosticDecoderConfigurationAdapter
{
    public static PlaybackDiagnosticDecoderParameterPreparation Prepare(
        PlaybackDiagnosticDecoderConfiguration? configuration)
    {
        if (configuration is null)
        {
            return new(
                PlaybackDiagnosticDecoderConfigurationResult.InvalidConfiguration,
                null);
        }

        PlaybackDecoderFfmpegCodecParametersLease? video = null;
        try
        {
            var videoCreation = PlaybackDecoderFfmpegCodecParametersFactory.Create(
                PlaybackDecoderFfmpegCodecParametersInput.ForH264(
                    configuration.VideoWidth,
                    configuration.VideoHeight,
                    configuration.H264Extradata));
            if (videoCreation.Result != PlaybackDecoderFfmpegCodecParametersResult.Created ||
                videoCreation.Lease is null)
            {
                return new(
                    PlaybackDiagnosticDecoderConfigurationResult.VideoParametersRejected,
                    null);
            }

            video = videoCreation.Lease;
            var audioCreation = PlaybackDecoderFfmpegCodecParametersFactory.Create(
                PlaybackDecoderFfmpegCodecParametersInput.ForAac(
                    configuration.AudioSampleRate,
                    configuration.AudioChannelCount,
                    configuration.AacExtradata));
            if (audioCreation.Result != PlaybackDecoderFfmpegCodecParametersResult.Created ||
                audioCreation.Lease is null)
            {
                video.Dispose();
                return new(
                    PlaybackDiagnosticDecoderConfigurationResult.AudioParametersRejected,
                    null);
            }

            var lease = new PlaybackDiagnosticDecoderParameterLease(video, audioCreation.Lease);
            video = null;
            return new(PlaybackDiagnosticDecoderConfigurationResult.Prepared, lease);
        }
        catch
        {
            video?.Dispose();
            return new(PlaybackDiagnosticDecoderConfigurationResult.Faulted, null);
        }
    }
}
