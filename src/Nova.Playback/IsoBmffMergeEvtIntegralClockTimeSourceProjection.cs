namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
{
    Projected,
    MissingClockTimeSourceProjection,
    SourceMismatch,
    EventLimitExceeded,
    InvalidRoundingMode,
    NonIntegralValue,
    EmptyRoundedInterval,
    InvalidEventTime,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection
                source,
            PlaybackIsoBmffClockTickRoundingMode startRounding,
            PlaybackIsoBmffClockTickRoundingMode endRounding,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
                integralClockTimeProjection)
    {
        Source = source;
        StartRounding = startRounding;
        EndRounding = endRounding;
        IntegralClockTimeProjection = integralClockTimeProjection;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection
            Source
    { get; }

    public PlaybackIsoBmffClockTickRoundingMode StartRounding { get; }

    public PlaybackIsoBmffClockTickRoundingMode EndRounding { get; }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjection
            IntegralClockTimeProjection
    { get; }
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalClockTimeSourceProjection?
                    source,
                PlaybackIsoBmffClockTickRoundingMode startRounding,
                PlaybackIsoBmffClockTickRoundingMode endRounding,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                    .MissingClockTimeSourceProjection;
        }

        if (!ReferenceEquals(
                source.ClockTimeProjection.Source,
                source.Source.TimeProjection))
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjector
                .TryProject(
                    source.ClockTimeProjection,
                    startRounding,
                    endRounding,
                    out var integralClockTimeProjection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .Projected ||
            integralClockTimeProjection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .EventLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                        .EventLimitExceeded,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .InvalidRoundingMode =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                        .InvalidRoundingMode,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .NonIntegralValue =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                        .NonIntegralValue,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .EmptyRoundedInterval =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                        .EmptyRoundedInterval,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeProjectionResult
                    .InvalidEventTime =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                        .InvalidEventTime,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(
                integralClockTimeProjection.Source,
                source.ClockTimeProjection) ||
            integralClockTimeProjection.StartRounding != startRounding ||
            integralClockTimeProjection.EndRounding != endRounding)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(
            source,
            startRounding,
            endRounding,
            integralClockTimeProjection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalIntegralClockTimeSourceProjectionResult
                .Projected;
    }
}
