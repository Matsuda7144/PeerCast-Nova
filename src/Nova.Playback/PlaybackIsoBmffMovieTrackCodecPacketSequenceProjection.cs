namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
{
    Projected,
    MissingMoviePacketBindingProjection,
    EmptyPacketSequence,
    PacketLimitExceeded,
    SourceMismatch,
    InvalidSampleNumber,
    MovieTimingMismatch,
    TrackSourceMismatch,
    MediaTimingMismatch,
    HandlerMismatch,
    NonIncreasingSampleNumber,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection(
        PlaybackIsoBmffMovieTrackCodecPacketBindingProjection source,
        PlaybackIsoBmffTrackCodecPacketSequence sequence)
    {
        Source = source;
        Sequence = sequence;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketBindingProjection Source
    { get; }

    public PlaybackIsoBmffTrackCodecPacketSequence Sequence { get; }
}

public static class PlaybackIsoBmffMovieTrackCodecPacketSequenceProjector
{
    public static PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
        Project(
            PlaybackIsoBmffMovieTrackCodecPacketBindingProjection? source,
            out PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                .MissingMoviePacketBindingProjection;
        }

        if (source.Entries.Count == 0)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                .EmptyPacketSequence;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackCodecPacketSequenceFactory.MaximumPacketCount)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                .PacketLimitExceeded;
        }

        var packets = new PlaybackIsoBmffMovieTrackCodecPacket[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]) ||
                !ReferenceEquals(
                    entry.MovieTrackPacket.TrackPacket,
                    entry.Source.TrackPacket) ||
                !ReferenceEquals(
                    entry.MovieTrackPacket.MovieTiming,
                    source.MovieTiming))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .SourceMismatch;
            }

            packets[index] = entry.MovieTrackPacket;
        }

        var result = PlaybackIsoBmffTrackCodecPacketSequenceFactory.TryCreate(
            packets,
            out var sequence);
        if (result !=
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult.Created ||
            sequence is null)
        {
            return result switch
            {
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .EmptyPacketSequence =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .EmptyPacketSequence,
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .PacketLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .PacketLimitExceeded,
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .InvalidSampleNumber =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .InvalidSampleNumber,
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .MovieTimingMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .MovieTimingMismatch,
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .TrackSourceMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .TrackSourceMismatch,
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .MediaTimingMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .MediaTimingMismatch,
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .HandlerMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .HandlerMismatch,
                PlaybackIsoBmffTrackCodecPacketSequenceCreationResult
                    .NonIncreasingSampleNumber =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                        .NonIncreasingSampleNumber,
                _ => PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
                    .SourceMismatch,
            };
        }

        projection = new(source, sequence);
        return PlaybackIsoBmffMovieTrackCodecPacketSequenceProjectionResult
            .Projected;
    }
}
