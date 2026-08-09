namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceSetTests
{
    [Fact]
    public void TryCreate_AssignsCallerOrderAndRetainsExactSequences()
    {
        var movieTiming =
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        var video = PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
            7,
            PlaybackIsoBmffHandlerKind.Video,
            movieTiming);
        var audio = PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
            9,
            PlaybackIsoBmffHandlerKind.Audio,
            movieTiming);
        var sequences = new[] { video, audio };

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory.TryCreate(
                sequences,
                out var sequenceSet));
        sequences[0] = audio;
        var created = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSet>(sequenceSet);
        Assert.Same(movieTiming, created.MovieTiming);
        Assert.Collection(
            created.Streams,
            stream => AssertStream(
                stream,
                0,
                7,
                PlaybackIsoBmffHandlerKind.Video,
                video),
            stream => AssertStream(
                stream,
                1,
                9,
                PlaybackIsoBmffHandlerKind.Audio,
                audio));
    }

    [Fact]
    public void TryCreate_RejectsMissingEmptyAndExcessiveSets()
    {
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .MissingSequences,
            null);
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .EmptySequenceSet,
            []);
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .SequenceLimitExceeded,
            Enumerable.Range(
                    1,
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory
                        .MaximumSequenceCount + 1)
                .Select(index =>
                    PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                        (uint)index))
                .ToArray());
    }

    [Fact]
    public void TryCreate_RejectsMissingAndForeignMovieSources()
    {
        var first = PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence();
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .MissingSequence,
            [first, null!]);
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .MovieTimingMismatch,
            [
                first,
                PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(8),
            ]);
    }

    [Fact]
    public void TryCreate_RejectsDuplicateTrackIdentity()
    {
        var movieTiming =
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        AssertResult(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .DuplicateTrackId,
            [
                PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                    7,
                    PlaybackIsoBmffHandlerKind.Video,
                    movieTiming),
                PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                    7,
                    PlaybackIsoBmffHandlerKind.Audio,
                    movieTiming),
            ]);
    }

    [Fact]
    public void TryCreate_AcceptsExactMaximumSequenceCount()
    {
        var movieTiming =
            PlaybackIsoBmffMovieCodecPacketIndexTests.MovieTiming();
        var sequences = Enumerable.Range(
                1,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory
                    .MaximumSequenceCount)
            .Select(index =>
                PlaybackIsoBmffTrackCodecPacketSequenceTests.Sequence(
                    (uint)index,
                    PlaybackIsoBmffHandlerKind.Audio,
                    movieTiming))
            .ToArray();

        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                .Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory.TryCreate(
                sequences,
                out var sequenceSet));
        Assert.Equal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory
                .MaximumSequenceCount,
            Assert.IsType<PlaybackIsoBmffMovieTrackCodecPacketSequenceSet>(
                sequenceSet).Streams.Count);
    }

    private static void AssertStream(
        PlaybackIsoBmffIndexedTrackCodecPacketSequence stream,
        int streamIndex,
        uint trackId,
        PlaybackIsoBmffHandlerKind mediaKind,
        PlaybackIsoBmffTrackCodecPacketSequence sequence)
    {
        Assert.Equal(streamIndex, stream.StreamIndex);
        Assert.Equal(trackId, stream.TrackId);
        Assert.Equal(mediaKind, stream.MediaKind);
        Assert.Same(sequence, stream.Sequence);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult expected,
        IReadOnlyList<PlaybackIsoBmffTrackCodecPacketSequence>? sequences)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory.TryCreate(
                sequences,
                out var sequenceSet));
        Assert.Null(sequenceSet);
    }
}
