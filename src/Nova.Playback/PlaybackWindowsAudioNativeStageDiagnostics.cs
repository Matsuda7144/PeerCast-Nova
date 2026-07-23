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
}

internal interface IPlaybackWindowsAudioNativeStageReporter
{
    void Report(PlaybackWindowsAudioNativeStage stage);
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
