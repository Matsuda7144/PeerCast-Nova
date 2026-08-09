namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieCodecPacketIndexTests
{
    [Fact]
    public void TryIndex_AssignsCallerOrderAndRetainsExactSources()
    {
        var movieTiming = MovieTiming();
        var video = MoviePacket(
            7,
            PlaybackIsoBmffHandlerKind.Video,
            movieTiming);
        var audio = MoviePacket(
            9,
            PlaybackIsoBmffHandlerKind.Audio,
            movieTiming);
        var packets = new[] { video, audio };

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.Indexed,
            PlaybackIsoBmffMovieCodecPacketIndexer.TryIndex(
                packets,
                out var index));
        packets[0] = audio;
        var indexed = Assert.IsType<PlaybackIsoBmffMovieCodecPacketIndex>(
            index);
        Assert.Same(movieTiming, indexed.MovieTiming);
        Assert.Collection(
            indexed.Streams,
            stream =>
            {
                Assert.Equal(0, stream.StreamIndex);
                Assert.Same(video, stream.Packet);
                Assert.Equal(7u, stream.TrackId);
                Assert.Equal(
                    PlaybackIsoBmffHandlerKind.Video,
                    stream.MediaKind);
            },
            stream =>
            {
                Assert.Equal(1, stream.StreamIndex);
                Assert.Same(audio, stream.Packet);
                Assert.Equal(9u, stream.TrackId);
                Assert.Equal(
                    PlaybackIsoBmffHandlerKind.Audio,
                    stream.MediaKind);
            });
    }

    [Fact]
    public void TryIndex_RejectsMissingEmptyAndOversizedPacketSets()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.MissingPackets,
            null);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.EmptyPacketSet,
            []);
        var packet = MoviePacket(1, PlaybackIsoBmffHandlerKind.Audio);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.TooManyPackets,
            Enumerable.Repeat(
                    packet,
                    PlaybackIsoBmffMovieCodecPacketIndexer
                        .MaximumPacketCount + 1)
                .ToArray());
    }

    [Fact]
    public void TryIndex_RejectsMissingPacketAndDifferentMovieSource()
    {
        var movieTiming = MovieTiming();
        var first = MoviePacket(
            1,
            PlaybackIsoBmffHandlerKind.Video,
            movieTiming);
        var sameValuesDifferentSource = MoviePacket(
            2,
            PlaybackIsoBmffHandlerKind.Audio,
            MovieTiming());

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.MissingPacket,
            [first, null!]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketIndexingResult
                .MovieTimingMismatch,
            [first, sameValuesDifferentSource]);
    }

    [Fact]
    public void TryIndex_RejectsDuplicateTrackIdentity()
    {
        var movieTiming = MovieTiming();
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.DuplicateTrackId,
            [
                MoviePacket(
                    3,
                    PlaybackIsoBmffHandlerKind.Video,
                    movieTiming),
                MoviePacket(
                    3,
                    PlaybackIsoBmffHandlerKind.Audio,
                    movieTiming),
            ]);
    }

    [Fact]
    public void TryIndex_AcceptsExactMaximumPacketCount()
    {
        var movieTiming = MovieTiming();
        var packets = Enumerable.Range(
                1,
                PlaybackIsoBmffMovieCodecPacketIndexer.MaximumPacketCount)
            .Select(index => MoviePacket(
                (uint)index,
                PlaybackIsoBmffHandlerKind.Audio,
                movieTiming))
            .ToArray();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketIndexingResult.Indexed,
            PlaybackIsoBmffMovieCodecPacketIndexer.TryIndex(
                packets,
                out var result));
        var indexed = Assert.IsType<PlaybackIsoBmffMovieCodecPacketIndex>(
            result);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketIndexer.MaximumPacketCount,
            indexed.Streams.Count);
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketIndexer.MaximumPacketCount - 1,
            indexed.Streams[^1].StreamIndex);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketIndexingResult expected,
        IReadOnlyList<PlaybackIsoBmffMovieTrackCodecPacket>? packets)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketIndexer.TryIndex(
                packets,
                out var index));
        Assert.Null(index);
    }

    internal static PlaybackIsoBmffMovieTrackCodecPacket MoviePacket(
        uint trackId,
        PlaybackIsoBmffHandlerKind mediaKind,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming = null,
        IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap>? appearances =
            null,
        uint? mediaTimescale = null,
        ulong sampleNumber = 1,
        PlaybackIsoBmffMovieTrackCodecPacket? metadataSource = null)
    {
        var timescale = mediaTimescale ??
            (mediaKind == PlaybackIsoBmffHandlerKind.Audio
                ? 48_000u
                : 90_000u);
        var sample = new PlaybackIsoBmffSamplePresentationTime(
            10,
            2,
            12,
            0,
            false);
        appearances ??=
        [
            new PlaybackIsoBmffSampleEditMovieOverlap(
                0,
                10,
                12,
                timescale,
                true),
        ];
        var admission = new PlaybackIsoBmffEditedSampleAdmission(
            sampleNumber,
            new PlaybackIsoBmffCompleteSampleEditAdmission(
                sample,
                Array.AsReadOnly(appearances.ToArray())));
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
            timescale);
        var codec = mediaKind == PlaybackIsoBmffHandlerKind.Audio
            ? PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a
            : PlaybackIsoBmffSampleEntryCodecCandidate.Avc1;
        var selection = new PlaybackIsoBmffEditedSampleDescriptionSelection(
            1,
            Location(mediaKind == PlaybackIsoBmffHandlerKind.Audio
                ? 0x6D703461u
                : 0x61766331u),
            codec,
            mediaKind);
        var packet = new PlaybackIsoBmffCodecPacketProjection(
            candidate,
            selection,
            candidate.Payload,
            null,
            null);
        var handlerType = mediaKind == PlaybackIsoBmffHandlerKind.Audio
            ? 0x736F756Eu
            : 0x76696465u;
        var trackPacket = new PlaybackIsoBmffTrackCodecPacket(
            packet,
            metadataSource?.TrackPacket.TrackIdentity ??
                new(0, 3, 0, 0, trackId, 1_000, 0, 0),
            metadataSource?.TrackPacket.MediaTiming ??
                new(0, 0, 0, timescale, 1_000, "und"),
            metadataSource?.TrackPacket.Handler ??
                new(handlerType, mediaKind, string.Empty));
        return new(
            trackPacket,
            movieTiming ?? metadataSource?.MovieTiming ?? MovieTiming());
    }

    internal static PlaybackIsoBmffMovieHeaderTimingBox MovieTiming() =>
        new(0, 0, 0, 1_000, 100);

    private static PlaybackIsoBmffBoxLocation Location(uint type) =>
        new(new(type, 8, 8, null), 0, 8);
}
