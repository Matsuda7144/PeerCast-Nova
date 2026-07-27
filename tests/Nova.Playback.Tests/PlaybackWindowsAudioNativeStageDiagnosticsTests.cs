namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsAudioNativeStageDiagnosticsTests
{
    [Fact]
    public void StageAllowlistIsExact()
    {
        Assert.Equal(
            [
                "EnsureRenderClientEntering",
                "EnsureRenderClientCompleted",
                "GetBufferSizeEntering",
                "GetBufferSizeCompleted",
                "GetCurrentPaddingEntering",
                "GetCurrentPaddingCompleted",
                "GetBufferEntering",
                "GetBufferCompleted",
                "BufferCopyEntering",
                "BufferCopyCompleted",
                "ReleaseBufferEntering",
                "ReleaseBufferCompleted",
                "StartEntering",
                "StartCompleted",
                "StopEntering",
                "StopCompleted",
                "ResetEntering",
                "ResetCompleted",
                "ProductionCleanupEntering",
                "ProductionCleanupCompleted",
                "BufferCleanupEntering",
                "BufferCleanupCompleted",
                "StreamCleanupEntering",
                "StreamCleanupCompleted",
                "BindingCleanupEntering",
                "BindingCleanupCompleted",
                "ActivationCleanupEntering",
                "ActivationCleanupCompleted",
            ],
            Enum.GetNames<PlaybackWindowsAudioNativeStage>());
    }

    [Fact]
    public void TextWriterReporterEmitsOnlyOneExactAllowlistedStageAndFlushes()
    {
        var output = new RecordingWriter();
        var reporter = new TextWriterPlaybackWindowsAudioNativeStageReporter(output);

        reporter.Report(PlaybackWindowsAudioNativeStage.GetBufferEntering);

        Assert.Equal(
            "PCN-AUDIO-NATIVE-STAGE: GetBufferEntering" + Environment.NewLine,
            output.ToString());
        Assert.Equal(1, output.FlushCount);
    }

    [Fact]
    public void InvalidStageAndReporterFailureRemainNoOp()
    {
        var output = new StringWriter();
        var reporter = new TextWriterPlaybackWindowsAudioNativeStageReporter(output);
        reporter.Report((PlaybackWindowsAudioNativeStage)int.MaxValue);
        Assert.Equal(string.Empty, output.ToString());

        var throwing = new TextWriterPlaybackWindowsAudioNativeStageReporter(
            new ThrowingWriter());
        var exception = Record.Exception(
            () => throwing.Report(PlaybackWindowsAudioNativeStage.StartEntering));
        Assert.Null(exception);
    }

    private sealed class RecordingWriter : StringWriter
    {
        public int FlushCount { get; private set; }

        public override void Flush()
        {
            FlushCount++;
            base.Flush();
        }
    }

    private sealed class ThrowingWriter : StringWriter
    {
        public override void Write(string? value) =>
            throw new IOException("Injected diagnostic writer failure.");
    }
}
