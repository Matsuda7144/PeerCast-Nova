namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackCodecPacketBindingResult
{
    Bound,
    MissingPacket,
    MissingTrackIdentity,
    MissingMediaTiming,
    MissingHandler,
    InvalidTrackIdentity,
    MediaTimescaleMismatch,
    HandlerMismatch,
}

public sealed class PlaybackIsoBmffTrackCodecPacket
{
    internal PlaybackIsoBmffTrackCodecPacket(
        PlaybackIsoBmffCodecPacketProjection packet,
        PlaybackIsoBmffTrackHeaderIdentityBox trackIdentity,
        PlaybackIsoBmffMediaHeaderTimingBox mediaTiming,
        PlaybackIsoBmffHandlerReferenceBox handler)
    {
        Packet = packet;
        TrackIdentity = trackIdentity;
        MediaTiming = mediaTiming;
        Handler = handler;
    }

    public PlaybackIsoBmffCodecPacketProjection Packet { get; }

    public PlaybackIsoBmffTrackHeaderIdentityBox TrackIdentity { get; }

    public PlaybackIsoBmffMediaHeaderTimingBox MediaTiming { get; }

    public PlaybackIsoBmffHandlerReferenceBox Handler { get; }

    public uint TrackId => TrackIdentity.TrackId;

    public uint MediaTimescale => MediaTiming.Timescale;

    public PlaybackIsoBmffHandlerKind MediaKind => Handler.Kind;
}

public static class PlaybackIsoBmffTrackCodecPacketBinder
{
    public static PlaybackIsoBmffTrackCodecPacketBindingResult Bind(
        PlaybackIsoBmffCodecPacketProjection? packet,
        PlaybackIsoBmffTrackHeaderIdentityBox? trackIdentity,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        PlaybackIsoBmffHandlerReferenceBox? handler,
        out PlaybackIsoBmffTrackCodecPacket? trackPacket)
    {
        trackPacket = null;
        if (packet is null)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingResult.MissingPacket;
        }

        if (trackIdentity is null)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingResult
                .MissingTrackIdentity;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingResult
                .MissingMediaTiming;
        }

        if (handler is null)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingResult.MissingHandler;
        }

        if (trackIdentity.TrackId == 0)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingResult
                .InvalidTrackIdentity;
        }

        if (packet.Candidate.MediaTimescale != mediaTiming.Timescale)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingResult
                .MediaTimescaleMismatch;
        }

        if (packet.MediaKind != handler.Kind)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingResult.HandlerMismatch;
        }

        trackPacket = new(packet, trackIdentity, mediaTiming, handler);
        return PlaybackIsoBmffTrackCodecPacketBindingResult.Bound;
    }
}
