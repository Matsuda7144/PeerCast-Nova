namespace PeerCast.Nova.Playback;

/// <summary>
/// Synchronous and non-blocking decoder selection. The creation context owns
/// the decoded-output sink for this runner lifetime. Returning null means that
/// the recognized container is not supported by the installed backend set.
/// </summary>
public interface IPlaybackDecoderFactory
{
    IPlaybackDecoder? Create(PlaybackDecoderCreationContext context);
}
