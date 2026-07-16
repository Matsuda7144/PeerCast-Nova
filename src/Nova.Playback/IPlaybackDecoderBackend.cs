namespace PeerCast.Nova.Playback;

/// <summary>
/// Replaceable decoder backend boundary. Implementations are discovered and
/// constructed by the host; this contract does not load assemblies or native
/// libraries.
/// </summary>
public interface IPlaybackDecoderBackend
{
    PlaybackDecoderBackendDescriptor Descriptor { get; }

    IPlaybackDecoder Create(PlaybackDecoderCreationContext context);
}
