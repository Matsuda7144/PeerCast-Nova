namespace PeerCast.Nova.Playback;

/// <summary>
/// Awaited decoded-output boundary. Frame payload memory is valid only until
/// the corresponding callback completes. A sink that needs to retain data
/// must copy it before completing the returned <see cref="ValueTask"/>.
/// Decoder implementations must await each callback, so sink delay provides
/// explicit backpressure without an unbounded decoder-owned output queue.
/// </summary>
public interface IPlaybackDecodedFrameSink
{
    ValueTask WriteAudioAsync(
        PlaybackDecodedAudioFrame frame,
        CancellationToken cancellationToken);

    ValueTask WriteVideoAsync(
        PlaybackDecodedVideoFrame frame,
        CancellationToken cancellationToken);
}
