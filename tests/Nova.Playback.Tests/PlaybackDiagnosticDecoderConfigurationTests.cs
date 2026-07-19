namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDiagnosticDecoderConfigurationTests
{
    [Fact]
    public void Prepare_BaselineCreatesIndependentVideoAndAudioParameterOwners()
    {
        var preparation = PlaybackDiagnosticDecoderConfigurationAdapter.Prepare(
            PlaybackDiagnosticDecoderConfiguration.Baseline);

        Assert.Equal(PlaybackDiagnosticDecoderConfigurationResult.Prepared, preparation.Result);
        using var lease = Assert.IsType<PlaybackDiagnosticDecoderParameterLease>(preparation.Lease);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.H264, lease.Video.CodecId);
        Assert.Equal(16, lease.Video.Width);
        Assert.Equal(16, lease.Video.Height);
        Assert.Equal(PlaybackDecoderFfmpegCodecId.Aac, lease.Audio.CodecId);
        Assert.Equal(48_000, lease.Audio.SampleRate);
        Assert.Equal(2, lease.Audio.ChannelCount);
    }

    [Fact]
    public void Prepare_NullConfigurationFailsClosed()
    {
        var preparation = PlaybackDiagnosticDecoderConfigurationAdapter.Prepare(null);

        Assert.Equal(
            PlaybackDiagnosticDecoderConfigurationResult.InvalidConfiguration,
            preparation.Result);
        Assert.Null(preparation.Lease);
    }

    [Fact]
    public void Prepare_InvalidVideoRejectsBeforeAudioOwnerIsReturned()
    {
        var preparation = PlaybackDiagnosticDecoderConfigurationAdapter.Prepare(new()
        {
            VideoWidth = 0,
            VideoHeight = 16,
            H264Extradata = new byte[] { 1 },
            AudioSampleRate = 48_000,
            AudioChannelCount = 2,
            AacExtradata = new byte[] { 0x11, 0x90 },
        });

        Assert.Equal(
            PlaybackDiagnosticDecoderConfigurationResult.VideoParametersRejected,
            preparation.Result);
        Assert.Null(preparation.Lease);
    }

    [Fact]
    public void Prepare_InvalidAudioDisposesPartialVideoOwner()
    {
        var preparation = PlaybackDiagnosticDecoderConfigurationAdapter.Prepare(new()
        {
            VideoWidth = 16,
            VideoHeight = 16,
            H264Extradata = new byte[] { 1 },
            AudioSampleRate = 0,
            AudioChannelCount = 2,
            AacExtradata = new byte[] { 0x11, 0x90 },
        });

        Assert.Equal(
            PlaybackDiagnosticDecoderConfigurationResult.AudioParametersRejected,
            preparation.Result);
        Assert.Null(preparation.Lease);
    }

    [Fact]
    public void Dispose_ClearsBothOwnedParameterLeasesIdempotently()
    {
        var preparation = PlaybackDiagnosticDecoderConfigurationAdapter.Prepare(
            PlaybackDiagnosticDecoderConfiguration.Baseline);
        var lease = Assert.IsType<PlaybackDiagnosticDecoderParameterLease>(preparation.Lease);
        var video = lease.Video;
        var audio = lease.Audio;

        lease.Dispose();
        lease.Dispose();

        Assert.True(lease.IsDisposed);
        Assert.True(video.IsDisposed);
        Assert.True(audio.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Video);
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Audio);
    }
}
