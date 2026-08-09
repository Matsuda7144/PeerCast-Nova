namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffMovieCodecPacketEventQueueTests
{
    [Fact]
    public void TryCreate_OwnsReadOnlySequenceWithExactSourceIdentity()
    {
        var projection = TimeProjection();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult.Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueFactory.TryCreate(
                projection,
                out var queue));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot>(queue);
        Assert.Same(projection, created.Projection);
        Assert.Equal(projection.Events.Count, created.Count);
        Assert.Collection(
            created.Entries,
            first =>
            {
                Assert.Equal(0, first.SequenceIndex);
                Assert.Same(projection.Events[0], first.Time);
                Assert.Same(projection.Events[0].Event, first.Event);
            },
            second =>
            {
                Assert.Equal(1, second.SequenceIndex);
                Assert.Same(projection.Events[1], second.Time);
                Assert.Same(projection.Events[1].Event, second.Event);
            });
        var entries = Assert.IsAssignableFrom<
            IList<PlaybackIsoBmffMovieCodecPacketEventQueueEntry>>(
                created.Entries);
        Assert.Throws<NotSupportedException>(() => entries.Clear());
    }

    [Fact]
    public void TryCreate_RejectsMissingProjectionAndEventLimitExceeded()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                .MissingProjection,
            null);

        var projection = TimeProjection();
        var events = new PlaybackIsoBmffMovieCodecPacketEventTime[
            PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount +
            1];
        Array.Fill(events, projection.Events[0]);
        var excessive = new
            PlaybackIsoBmffMovieCodecPacketEventTimeProjection(
                projection.Order,
                Array.AsReadOnly(events));
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                .EventLimitExceeded,
            excessive);
    }

    [Fact]
    public void TryCreate_RejectsOrderMismatch()
    {
        var projection = TimeProjection();
        var reversed = projection.Events.Reverse().ToArray();
        var mismatched = new
            PlaybackIsoBmffMovieCodecPacketEventTimeProjection(
                projection.Order,
                Array.AsReadOnly(reversed));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                .OrderMismatch,
            mismatched);
    }

    [Fact]
    public void TryCreate_RejectsInvalidExactEventTime()
    {
        var projection = TimeProjection();
        var source = projection.Events[0];
        var invalid = new PlaybackIsoBmffMovieCodecPacketEventTime(
            source.Event,
            source.MovieTimescale,
            source.SecondsDenominator - 1);
        var events = projection.Events.ToArray();
        events[0] = invalid;
        var invalidProjection = new
            PlaybackIsoBmffMovieCodecPacketEventTimeProjection(
                projection.Order,
                Array.AsReadOnly(events));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult
                .InvalidEventTime,
            invalidProjection);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult expected,
        PlaybackIsoBmffMovieCodecPacketEventTimeProjection? projection)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueFactory.TryCreate(
                projection,
                out var queue));
        Assert.Null(queue);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventTimeProjection
        TimeProjection()
    {
        var eventProjection =
            PlaybackIsoBmffMovieCodecPacketEventOrderTests.Projection();
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventOrderingResult.Ordered,
            PlaybackIsoBmffMovieCodecPacketEventOrderer.Order(
                eventProjection,
                out var order));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventTimeProjectionResult.Projected,
            PlaybackIsoBmffMovieCodecPacketEventTimeProjector.Project(
                order,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventTimeProjection>(projection);
    }
}
