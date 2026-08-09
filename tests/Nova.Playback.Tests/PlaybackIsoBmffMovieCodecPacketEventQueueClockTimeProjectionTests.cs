using System.Numerics;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionTests
{
    [Fact]
    public void Project_RetainsSnapshotAndProjectsExactRationalTicks()
    {
        var snapshot = Snapshot();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjector.Project(
                snapshot,
                1_000,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection>(
                projection);
        Assert.Same(snapshot, projected.Snapshot);
        Assert.Equal(1_000u, projected.TicksPerSecond);
        Assert.Collection(
            projected.Events,
            first =>
            {
                Assert.Same(snapshot.Entries[0], first.Entry);
                Assert.Equal(new BigInteger(10_000), first.StartTickNumerator);
                Assert.Equal(new BigInteger(12_000),
                    first.EndTickNumeratorExclusive);
                Assert.Equal(new BigInteger(2_000),
                    first.DurationTickNumerator);
                Assert.Equal(90_000_000ul, first.TickDenominator);
            },
            second => Assert.Same(snapshot.Entries[1], second.Entry));
    }

    [Fact]
    public void Project_UsesBigIntegerForMaximumTickRate()
    {
        var snapshot = Snapshot();

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                .Projected,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjector.Project(
                snapshot,
                uint.MaxValue,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjection>(
                projection);
        var first = projected.Events[0];
        Assert.Equal(
            (BigInteger)first.Entry.Time.StartSecondsNumerator * uint.MaxValue,
            first.StartTickNumerator);
    }

    [Fact]
    public void Project_RejectsMissingSnapshotAndZeroTickRate()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                .MissingSnapshot,
            null,
            1_000);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                .InvalidTicksPerSecond,
            Snapshot(),
            0);
    }

    [Fact]
    public void Project_RejectsInvalidSequence()
    {
        var snapshot = Snapshot();
        var entries = snapshot.Entries.ToArray();
        entries[0] = new PlaybackIsoBmffMovieCodecPacketEventQueueEntry(
            1,
            entries[0].Time);
        var invalid = new PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot(
            snapshot.Projection,
            Array.AsReadOnly(entries));

        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
                .InvalidSequence,
            invalid,
            1_000);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjectionResult
            expected,
        PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot? snapshot,
        uint ticksPerSecond)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueClockTimeProjector.Project(
                snapshot,
                ticksPerSecond,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot Snapshot()
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
                out var timeProjection));
        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueCreationResult.Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueFactory.TryCreate(
                timeProjection,
                out var snapshot));
        return Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueSnapshot>(snapshot);
    }
}
