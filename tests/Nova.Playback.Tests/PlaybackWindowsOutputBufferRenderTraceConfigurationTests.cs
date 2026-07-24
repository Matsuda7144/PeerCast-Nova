namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsOutputBufferRenderTraceConfigurationTests
{
    [Fact]
    public void Ordinary_reporters_keep_render_trace_disabled()
    {
        var nullConfiguration = PlaybackWindowsOutputBufferProcessingBoundary
            .ResolveAudioRenderTraceConfiguration(
                NullPlaybackWindowsAudioNativeStageReporter.Instance);
        var textConfiguration = PlaybackWindowsOutputBufferProcessingBoundary
            .ResolveAudioRenderTraceConfiguration(
                new TextWriterPlaybackWindowsAudioNativeStageReporter(new StringWriter()));

        Assert.False(nullConfiguration.CaptureAudioRenderTrace);
        Assert.False(nullConfiguration.CapturePreStopPadding);
        Assert.False(textConfiguration.CaptureAudioRenderTrace);
        Assert.False(textConfiguration.CapturePreStopPadding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configured_reporter_enables_trace_and_preserves_pre_stop_intent(
        bool capturePreStopPadding)
    {
        var reporter = new PlaybackWindowsAudioRenderTraceConfiguredReporter(
            NullPlaybackWindowsAudioNativeStageReporter.Instance,
            capturePreStopPadding);

        var configuration = PlaybackWindowsOutputBufferProcessingBoundary
            .ResolveAudioRenderTraceConfiguration(reporter);

        Assert.True(configuration.CaptureAudioRenderTrace);
        Assert.Equal(capturePreStopPadding, configuration.CapturePreStopPadding);
    }

    [Fact]
    public void Pre_stop_only_configuration_fails_closed()
    {
        Assert.Throws<ArgumentException>(() =>
            PlaybackWindowsOutputBufferProcessingBoundary
                .ResolveAudioRenderTraceConfiguration(new InvalidReporter()));
    }

    [Fact]
    public void Null_reporter_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PlaybackWindowsOutputBufferProcessingBoundary
                .ResolveAudioRenderTraceConfiguration(null!));
    }

    private sealed class InvalidReporter :
        IPlaybackWindowsAudioNativeStageReporter,
        IPlaybackWindowsAudioRenderTraceConfiguration
    {
        public bool CaptureAudioRenderTrace => false;

        public bool CapturePreStopPadding => true;

        public void Report(PlaybackWindowsAudioNativeStage stage)
        {
            _ = stage;
        }
    }
}
