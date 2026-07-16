namespace PeerCast.Nova.Playback;

/// <summary>
/// Content-discarding sink used when a decoder lifecycle is exercised without
/// an audio or video consumer.
/// </summary>
public sealed class NullPlaybackDecodedFrameSink : IPlaybackDecodedFrameSink
{
    private NullPlaybackDecodedFrameSink()
    {
    }

    public static NullPlaybackDecodedFrameSink Instance { get; } = new();

    public ValueTask WriteAudioAsync(
        PlaybackDecodedAudioFrame frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteVideoAsync(
        PlaybackDecodedVideoFrame frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
