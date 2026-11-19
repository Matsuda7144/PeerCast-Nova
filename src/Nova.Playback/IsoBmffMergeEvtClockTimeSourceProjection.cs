namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
{
    Projected,
    MissingTimeSourceProjection,
    SourceMismatch,
    EventLimitExceeded,
    InvalidTicksPerSecond,
    InvalidEventTime,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection
                source,
            uint ticksPerSecond,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
                clockTimeProjection)
    {
        Source = source;
        TicksPerSecond = ticksPerSecond;
        ClockTimeProjection = clockTimeProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection
            Source
    { get; }

    public uint TicksPerSecond { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjection
            ClockTimeProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeSourceProjection?
                    source,
                uint ticksPerSecond,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                    .MissingTimeSourceProjection;
        }

        if (!ReferenceEquals(
                source.TimeProjection.AcknowledgementView,
                source.Source.View))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjector
                .TryProject(
                    source.TimeProjection,
                    ticksPerSecond,
                    out var clockTimeProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .Projected ||
            clockTimeProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .EventLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                        .EventLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .InvalidTicksPerSecond =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                        .InvalidTicksPerSecond,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeProjectionResult
                    .InvalidEventTime =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                        .InvalidEventTime,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(
                clockTimeProjection.Source,
                source.TimeProjection) ||
            clockTimeProjection.TicksPerSecond != ticksPerSecond)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, ticksPerSecond, clockTimeProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjectionResult
                .Projected;
    }
}
