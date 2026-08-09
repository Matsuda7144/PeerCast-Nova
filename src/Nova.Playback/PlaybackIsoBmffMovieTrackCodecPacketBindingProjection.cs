using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
{
    Bound,
    MissingTrackBindingProjection,
    MissingMovieTiming,
    EntryLimitExceeded,
    SourceMismatch,
    InvalidMovieTimescale,
    MissingAppearance,
    InvalidAppearance,
    MediaTimescaleMismatch,
    AppearanceOutsideMovieDuration,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketBinding
{
    internal PlaybackIsoBmffMovieTrackCodecPacketBinding(
        PlaybackIsoBmffTrackCodecPacketBinding source,
        PlaybackIsoBmffMovieTrackCodecPacket movieTrackPacket)
    {
        Source = source;
        MovieTrackPacket = movieTrackPacket;
    }

    public PlaybackIsoBmffTrackCodecPacketBinding Source { get; }

    public PlaybackIsoBmffMovieTrackCodecPacket MovieTrackPacket { get; }

    public ulong SampleNumber => Source.SampleNumber;
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketBindingProjection
{
    internal PlaybackIsoBmffMovieTrackCodecPacketBindingProjection(
        PlaybackIsoBmffTrackCodecPacketBindingProjection source,
        PlaybackIsoBmffMovieHeaderTimingBox movieTiming,
        ReadOnlyCollection<PlaybackIsoBmffMovieTrackCodecPacketBinding> entries)
    {
        Source = source;
        MovieTiming = movieTiming;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackCodecPacketBindingProjection Source { get; }

    public PlaybackIsoBmffMovieHeaderTimingBox MovieTiming { get; }

    public IReadOnlyList<PlaybackIsoBmffMovieTrackCodecPacketBinding> Entries
    { get; }
}

public static class PlaybackIsoBmffMovieTrackCodecPacketBindingProjector
{
    public static PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
        Bind(
            PlaybackIsoBmffTrackCodecPacketBindingProjection? source,
            PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
            out PlaybackIsoBmffMovieTrackCodecPacketBindingProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                .MissingTrackBindingProjection;
        }

        if (movieTiming is null)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                .MissingMovieTiming;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                .EntryLimitExceeded;
        }

        var entries = new PlaybackIsoBmffMovieTrackCodecPacketBinding[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]) ||
                !ReferenceEquals(
                    entry.TrackPacket.Packet,
                    entry.Source.Packet))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                        .SourceMismatch;
            }

            var result = PlaybackIsoBmffMovieTrackCodecPacketBinder.Bind(
                entry.TrackPacket,
                movieTiming,
                out var movieTrackPacket);
            if (result !=
                    PlaybackIsoBmffMovieTrackCodecPacketBindingResult.Bound ||
                movieTrackPacket is null)
            {
                return MapFailure(result);
            }

            entries[index] = new(entry, movieTrackPacket);
        }

        projection = new(source, movieTiming, Array.AsReadOnly(entries));
        return PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
            .Bound;
    }

    private static PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
        MapFailure(PlaybackIsoBmffMovieTrackCodecPacketBindingResult result) =>
        result switch
        {
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidMovieTimescale =>
                PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                    .InvalidMovieTimescale,
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MissingAppearance =>
                PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                    .MissingAppearance,
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidAppearance =>
                PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                    .InvalidAppearance,
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MediaTimescaleMismatch =>
                PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                    .MediaTimescaleMismatch,
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .AppearanceOutsideMovieDuration =>
                PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                    .AppearanceOutsideMovieDuration,
            _ => PlaybackIsoBmffMovieTrackCodecPacketBindingProjectionResult
                .SourceMismatch,
        };
}
