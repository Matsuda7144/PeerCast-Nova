using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
{
    Bound,
    MissingPacketCompositionProjection,
    MissingTrackIdentity,
    EntryLimitExceeded,
    SourceMismatch,
    InvalidTrackIdentity,
    MediaTimescaleMismatch,
    HandlerMismatch,
}

public sealed class PlaybackIsoBmffTrackCodecPacketBinding
{
    internal PlaybackIsoBmffTrackCodecPacketBinding(
        PlaybackIsoBmffTrackCodecPacketComposition source,
        PlaybackIsoBmffTrackCodecPacket trackPacket)
    {
        Source = source;
        TrackPacket = trackPacket;
    }

    public PlaybackIsoBmffTrackCodecPacketComposition Source { get; }

    public PlaybackIsoBmffTrackCodecPacket TrackPacket { get; }

    public ulong SampleNumber => Source.SampleNumber;
}

public sealed class PlaybackIsoBmffTrackCodecPacketBindingProjection
{
    internal PlaybackIsoBmffTrackCodecPacketBindingProjection(
        PlaybackIsoBmffTrackCodecPacketCompositionProjection source,
        PlaybackIsoBmffTrackHeaderIdentityBox trackIdentity,
        PlaybackIsoBmffMediaHeaderTimingBox mediaTiming,
        PlaybackIsoBmffHandlerReferenceBox handler,
        ReadOnlyCollection<PlaybackIsoBmffTrackCodecPacketBinding> entries)
    {
        Source = source;
        TrackIdentity = trackIdentity;
        MediaTiming = mediaTiming;
        Handler = handler;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackCodecPacketCompositionProjection Source
    { get; }

    public PlaybackIsoBmffTrackHeaderIdentityBox TrackIdentity { get; }

    public PlaybackIsoBmffMediaHeaderTimingBox MediaTiming { get; }

    public PlaybackIsoBmffHandlerReferenceBox Handler { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackCodecPacketBinding> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackCodecPacketBindingProjector
{
    public static PlaybackIsoBmffTrackCodecPacketBindingProjectionResult Bind(
        PlaybackIsoBmffTrackCodecPacketCompositionProjection? source,
        PlaybackIsoBmffTrackHeaderIdentityBox? trackIdentity,
        out PlaybackIsoBmffTrackCodecPacketBindingProjection? projection)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                .MissingPacketCompositionProjection;
        }

        if (trackIdentity is null)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                .MissingTrackIdentity;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                .EntryLimitExceeded;
        }

        var descriptionProjection = source.Source.Source.Source;
        var mediaTiming = descriptionProjection.Source.MediaTiming;
        var handler = descriptionProjection.Handler;
        var entries = new PlaybackIsoBmffTrackCodecPacketBinding[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]) ||
                !ReferenceEquals(
                    entry.Packet.Candidate,
                    entry.Source.Source.Source.Source.Candidate))
            {
                return PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                    .SourceMismatch;
            }

            var result = PlaybackIsoBmffTrackCodecPacketBinder.Bind(
                entry.Packet,
                trackIdentity,
                mediaTiming,
                handler,
                out var trackPacket);
            if (result != PlaybackIsoBmffTrackCodecPacketBindingResult.Bound ||
                trackPacket is null)
            {
                return result switch
                {
                    PlaybackIsoBmffTrackCodecPacketBindingResult
                        .InvalidTrackIdentity =>
                        PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                            .InvalidTrackIdentity,
                    PlaybackIsoBmffTrackCodecPacketBindingResult
                        .MediaTimescaleMismatch =>
                        PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                            .MediaTimescaleMismatch,
                    PlaybackIsoBmffTrackCodecPacketBindingResult
                        .HandlerMismatch =>
                        PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                            .HandlerMismatch,
                    _ => PlaybackIsoBmffTrackCodecPacketBindingProjectionResult
                        .SourceMismatch,
                };
            }

            entries[index] = new(entry, trackPacket);
        }

        projection = new(
            source,
            trackIdentity,
            mediaTiming,
            handler,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackCodecPacketBindingProjectionResult.Bound;
    }
}
