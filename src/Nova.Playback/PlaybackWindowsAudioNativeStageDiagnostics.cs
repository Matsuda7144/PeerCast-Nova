namespace PeerCast.Nova.Playback;

internal enum PlaybackWindowsAudioNativeStage
{
    EnsureRenderClientEntering,
    EnsureRenderClientCompleted,
    GetBufferSizeEntering,
    GetBufferSizeCompleted,
    GetCurrentPaddingEntering,
    GetCurrentPaddingCompleted,
    GetBufferEntering,
    GetBufferCompleted,
    BufferCopyEntering,
    BufferCopyCompleted,
    ReleaseBufferEntering,
    ReleaseBufferCompleted,
    StartEntering,
    StartCompleted,
    StopEntering,
    StopCompleted,
    ResetEntering,
    ResetCompleted,
    ProductionCleanupEntering,
    ProductionCleanupCompleted,
    BufferCleanupEntering,
    BufferCleanupCompleted,
    StreamCleanupEntering,
    StreamCleanupCompleted,
    BindingCleanupEntering,
    BindingCleanupCompleted,
    ActivationCleanupEntering,
    ActivationCleanupCompleted,
}

internal interface IPlaybackWindowsAudioNativeStageReporter
{
    void Report(PlaybackWindowsAudioNativeStage stage);
}

internal interface IPlaybackWindowsAudioRenderTraceConfiguration
{
    bool CaptureAudioRenderTrace { get; }

    bool CapturePreStopPadding { get; }
}

internal sealed class PlaybackWindowsAudioRenderTraceConfiguredReporter(
    IPlaybackWindowsAudioNativeStageReporter inner,
    bool capturePreStopPadding) :
    IPlaybackWindowsAudioNativeStageReporter,
    IPlaybackWindowsAudioRenderTraceConfiguration
{
    private readonly IPlaybackWindowsAudioNativeStageReporter _inner = inner ??
        throw new ArgumentNullException(nameof(inner));

    public bool CaptureAudioRenderTrace => true;

    public bool CapturePreStopPadding { get; } = capturePreStopPadding;

    public void Report(PlaybackWindowsAudioNativeStage stage) => _inner.Report(stage);
}

internal sealed class NullPlaybackWindowsAudioNativeStageReporter :
    IPlaybackWindowsAudioNativeStageReporter
{
    public static NullPlaybackWindowsAudioNativeStageReporter Instance { get; } = new();

    private NullPlaybackWindowsAudioNativeStageReporter()
    {
    }

    public void Report(PlaybackWindowsAudioNativeStage stage)
    {
        _ = stage;
    }
}

internal sealed class TextWriterPlaybackWindowsAudioNativeStageReporter(
    TextWriter output) : IPlaybackWindowsAudioNativeStageReporter
{
    private readonly object _sync = new();
    private readonly TextWriter _output = output ??
        throw new ArgumentNullException(nameof(output));

    public void Report(PlaybackWindowsAudioNativeStage stage)
    {
        if (!Enum.IsDefined(stage))
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                _output.Write("PCN-AUDIO-NATIVE-STAGE: ");
                _output.WriteLine(stage);
                _output.Flush();
            }
        }
        catch
        {
            // Controlled diagnostics are best effort and never change output behavior.
        }
    }
}
