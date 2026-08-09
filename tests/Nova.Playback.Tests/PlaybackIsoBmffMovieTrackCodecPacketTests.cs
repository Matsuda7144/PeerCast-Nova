namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieTrackCodecPacketTests
{
    [Fact]
    public void Bind_RetainsExactMovieAndTrackPacketSources()
    {
        var appearance = Appearance(10, 20, 48_000);
        var trackPacket = TrackPacket(48_000, appearance);
        var movieTiming = MovieTiming(1_000, 100);

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult.Bound,
            PlaybackIsoBmffMovieTrackCodecPacketBinder.Bind(
                trackPacket,
                movieTiming,
                out var movieTrackPacket));
        var bound = Assert.IsType<PlaybackIsoBmffMovieTrackCodecPacket>(
            movieTrackPacket);
        Assert.Same(trackPacket, bound.TrackPacket);
        Assert.Same(movieTiming, bound.MovieTiming);
        Assert.Equal(7u, bound.TrackId);
        Assert.Equal(48_000u, bound.MediaTimescale);
        Assert.Equal(1_000u, bound.MovieTimescale);
        Assert.Same(trackPacket.Packet.Candidate.Appearances, bound.Appearances);
        Assert.Equal(appearance, Assert.Single(bound.Appearances));
    }

    [Fact]
    public void Bind_RejectsMissingInputsAndInvalidMovieTimescale()
    {
        var trackPacket = TrackPacket(48_000, Appearance(10, 20, 48_000));
        var movieTiming = MovieTiming(1_000, 100);

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MissingTrackPacket,
            PlaybackIsoBmffMovieTrackCodecPacketBinder.Bind(
                null, movieTiming, out var noTrackPacket));
        Assert.Null(noTrackPacket);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MissingMovieTiming,
            PlaybackIsoBmffMovieTrackCodecPacketBinder.Bind(
                trackPacket, null, out var noMovieTiming));
        Assert.Null(noMovieTiming);
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidMovieTimescale,
            PlaybackIsoBmffMovieTrackCodecPacketBinder.Bind(
                trackPacket, MovieTiming(0, 100), out var invalidTimescale));
        Assert.Null(invalidTimescale);
    }

    [Fact]
    public void Bind_RejectsMissingOrInvalidAppearance()
    {
        AssertBindingResult(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MissingAppearance,
            TrackPacket(48_000));
        AssertBindingResult(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidAppearance,
            TrackPacket(48_000, Appearance(10, 20, 0)));
        AssertBindingResult(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidAppearance,
            TrackPacket(48_000, Appearance(-1, 20, 48_000)));
        AssertBindingResult(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidAppearance,
            TrackPacket(48_000, Appearance(20, 20, 48_000)));
        AssertBindingResult(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .InvalidAppearance,
            TrackPacket(
                48_000,
                Appearance(10, 20, 48_000, containsCompleteSample: false)));
    }

    [Fact]
    public void Bind_RejectsTimescaleMismatchAndMovieDurationOverflow()
    {
        AssertBindingResult(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .MediaTimescaleMismatch,
            TrackPacket(48_000, Appearance(10, 20, 44_100)));
        AssertBindingResult(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult
                .AppearanceOutsideMovieDuration,
            TrackPacket(48_000, Appearance(0, 48_001, 48_000)),
            MovieTiming(1_000, 1));
    }

    [Fact]
    public void Bind_PreservesUnknownMovieDurationWithoutInventingBound()
    {
        var endNumerator = ((Int128)ulong.MaxValue * 48_000) + 1;
        var trackPacket = TrackPacket(
            48_000,
            Appearance(endNumerator - 1, endNumerator, 48_000));
        var movieTiming = MovieTiming(1_000, null);

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketBindingResult.Bound,
            PlaybackIsoBmffMovieTrackCodecPacketBinder.Bind(
                trackPacket,
                movieTiming,
                out var movieTrackPacket));
        var bound = Assert.IsType<PlaybackIsoBmffMovieTrackCodecPacket>(
            movieTrackPacket);
        Assert.False(bound.MovieTiming.HasKnownDuration);
        Assert.Equal(
            endNumerator,
            Assert.Single(bound.Appearances).MovieEndNumeratorExclusive);
    }

    private static void AssertBindingResult(
        PlaybackIsoBmffMovieTrackCodecPacketBindingResult expected,
        PlaybackIsoBmffTrackCodecPacket trackPacket,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming = null)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketBinder.Bind(
                trackPacket,
                movieTiming ?? MovieTiming(1_000, 100),
                out var rejected));
        Assert.Null(rejected);
    }

    private static PlaybackIsoBmffTrackCodecPacket TrackPacket(
        uint mediaTimescale,
        params PlaybackIsoBmffSampleEditMovieOverlap[] appearances)
    {
        var sample = new PlaybackIsoBmffSamplePresentationTime(
            10,
            2,
            12,
            0,
            false);
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            1,
            new PlaybackIsoBmffCompleteSampleEditAdmission(
                sample,
                Array.AsReadOnly(appearances)));
        var editedRange = new PlaybackIsoBmffEditedSampleByteRange(
            admission,
            new(8, 2, 10),
            new(0, 1, 1, 0, 1, 1));
        var mediaData = new PlaybackIsoBmffEditedSampleMediaData(
            editedRange,
            Location(0x6D646174));
        var payload = new PlaybackIsoBmffOwnedEditedSamplePayload(
            mediaData,
            [1, 2]);
        var candidate = new PlaybackIsoBmffEditedSamplePacketCandidate(
            payload,
            mediaTimescale);
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            Location(0x6D703461),
            PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a,
            PlaybackIsoBmffHandlerKind.Audio);
        var packet = new PlaybackIsoBmffCodecPacketProjection(
            candidate,
            selection,
            candidate.Payload,
            null,
            null);
        return new(
            packet,
            new(0, 3, 0, 0, 7, 1_000, 0, 0),
            new(0, 0, 0, mediaTimescale, 1_000, "und"),
            new(
                0x736F756E,
                PlaybackIsoBmffHandlerKind.Audio,
                string.Empty));
    }

    private static PlaybackIsoBmffSampleEditMovieOverlap Appearance(
        Int128 startNumerator,
        Int128 endNumerator,
        uint denominator,
        bool containsCompleteSample = true) => new(
            0,
            startNumerator,
            endNumerator,
            denominator,
            containsCompleteSample);

    private static PlaybackIsoBmffMovieHeaderTimingBox MovieTiming(
        uint timescale,
        ulong? durationUnits) => new(0, 0, 0, timescale, durationUnits);

    private static PlaybackIsoBmffBoxLocation Location(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
