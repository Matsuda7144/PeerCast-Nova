namespace PeerCast.Nova.Playback;

/// <summary>
/// Replaceable encoded-media decoder lifecycle. Implementations must not
/// retain encoded input memory after each callback completes and must await
/// decoded-output sink callbacks before reusing their payload memory.
/// </summary>
public interface IPlaybackDecoder : IAsyncDisposable
{
    ValueTask DecodeAsync(
        PlaybackEncodedInput input,
        CancellationToken cancellationToken);

    ValueTask ResetAsync(CancellationToken cancellationToken);

    ValueTask CompleteAsync(CancellationToken cancellationToken);
}
