namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieTrackCodecPacketBindingResult
{
    Bound,
    MissingTrackPacket,
    MissingMovieTiming,
    InvalidMovieTimescale,
    MissingAppearance,
    InvalidAppearance,
    MediaTimescaleMismatch,
    AppearanceOutsideMovieDuration,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacket
{
    internal PlaybackIsoBmffMovieTrackCodecPacket(
        PlaybackIsoBmffTrackCodecPacket trackPacket,
        PlaybackIsoBmffMovieHeaderTimingBox movieTiming)
    {
        TrackPacket = trackPacket;
        MovieTiming = movieTiming;
    }

    public PlaybackIsoBmffTrackCodecPacket TrackPacket { get; }

    public PlaybackIsoBmffMovieHeaderTimingBox MovieTiming { get; }

    public uint TrackId => TrackPacket.TrackId;

    public uint MediaTimescale => TrackPacket.MediaTimescale;

    public uint MovieTimescale => MovieTiming.Timescale;

    public IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap> Appearances =>
        TrackPacket.Packet.Candidate.Appearances;
}

public static class PlaybackIsoBmffMovieTrackCodecPacketBinder
{
    public static PlaybackIsoBmffMovieTrackCodecPacketBindingResult Bind(
        PlaybackIsoBmffTrackCodecPacket? trackPacket,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        out PlaybackIsoBmffMovieTrackCodecPacket? movieTrackPacket)
    {
        movieTrackPacket = null;
        if (trackPacket is null)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MissingTrackPacket;
        }

        if (movieTiming is null)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MissingMovieTiming;
        }

        if (movieTiming.Timescale == 0)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidMovieTimescale;
        }

        var appearances = trackPacket.Packet.Candidate.Appearances;
        if (appearances.Count == 0)
        {
            return PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MissingAppearance;
        }

        foreach (var appearance in appearances)
        {
            if (appearance.Denominator == 0 ||
                appearance.MovieStartNumerator < 0 ||
                appearance.MovieStartNumerator >=
                    appearance.MovieEndNumeratorExclusive ||
                !appearance.ContainsCompleteSample)
            {
                return PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                    .InvalidAppearance;
            }

            if (appearance.Denominator != trackPacket.MediaTimescale)
            {
                return PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                    .MediaTimescaleMismatch;
            }

            if (movieTiming.DurationUnits is { } movieDurationUnits &&
                appearance.MovieEndNumeratorExclusive >
                    (Int128)movieDurationUnits * appearance.Denominator)
            {
                return PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                    .AppearanceOutsideMovieDuration;
            }
        }

        movieTrackPacket = new(trackPacket, movieTiming);
        return PlaybackIsoBmffMovieTrackCodecPacketBindingResult.Bound;
    }
}
