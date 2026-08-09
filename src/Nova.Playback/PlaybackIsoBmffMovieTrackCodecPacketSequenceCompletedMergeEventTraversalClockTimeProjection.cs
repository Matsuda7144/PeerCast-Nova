using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
{
    Projected,
    MissingTimeProjection,
    EventLimitExceeded,
    InvalidTicksPerSecond,
    SourceMismatch,
    InvalidEventTime,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTime
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTime(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTime
                time,
            uint ticksPerSecond)
    {
        Time = time;
        TicksPerSecond = ticksPerSecond;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTime
        Time
    { get; }

    public uint TicksPerSecond { get; }

    public BigInteger StartTickNumerator =>
        (BigInteger)Time.StartSecondsNumerator * TicksPerSecond;

    public BigInteger EndTickNumeratorExclusive =>
        (BigInteger)Time.EndSecondsNumeratorExclusive * TicksPerSecond;

    public BigInteger DurationTickNumerator =>
        EndTickNumeratorExclusive - StartTickNumerator;

    public ulong TickDenominator => Time.SecondsDenominator;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
                source,
            uint ticksPerSecond,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTime>
                events)
    {
        Source = source;
        TicksPerSecond = ticksPerSecond;
        Events = events;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
            Source
    { get; }

    public uint TicksPerSecond { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTime>
        Events
    { get; }

    public int EventCount => Events.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection?
                    source,
                uint ticksPerSecond,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection?
                    projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .MissingTimeProjection;
        }

        if (source.EventCount >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .EventLimitExceeded;
        }

        if (ticksPerSecond == 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .InvalidTicksPerSecond;
        }

        if (source.EventCount != source.AcknowledgementView.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .SourceMismatch;
        }

        var events = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTime[
                source.EventCount];
        for (var index = 0; index < source.EventCount; index++)
        {
            var time = source.Events[index];
            if (!ReferenceEquals(
                    time.Acknowledgement,
                    source.AcknowledgementView.Acknowledgements[index]))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                        .SourceMismatch;
            }

            if (time.SecondsDenominator == 0 ||
                time.StartSecondsNumerator < 0 ||
                time.StartSecondsNumerator >=
                    time.EndSecondsNumeratorExclusive)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                        .InvalidEventTime;
            }

            events[index] = new(time, ticksPerSecond);
        }

        projection = new(source, ticksPerSecond, Array.AsReadOnly(events));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                .Projected;
    }
}
