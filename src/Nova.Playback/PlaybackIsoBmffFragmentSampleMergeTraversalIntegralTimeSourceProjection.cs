namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
{
    Projected,
    InvalidInput,
    SourceMismatch,
    InvalidTickRate,
    InvalidRoundingMode,
    NonIntegralValue,
    EmptyRoundedInterval,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection
                timing)
    {
        Source = source;
        Timing = timing;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection
        Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjection
        Timing
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeSourceProjection?
                    source,
                uint ticksPerSecond,
                PlaybackIsoBmffClockTickRoundingMode startRounding,
                PlaybackIsoBmffClockTickRoundingMode endRounding,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(
                source.Timing.AcknowledgementView,
                source.Source.View))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjector
                .Project(
                    source.Timing,
                    ticksPerSecond,
                    startRounding,
                    endRounding,
                    out var timing);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .Projected ||
            timing is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .InvalidTickRate =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                        .InvalidTickRate,
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .InvalidRoundingMode =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                        .InvalidRoundingMode,
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .NonIntegralValue =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                        .NonIntegralValue,
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeProjectionResult
                    .EmptyRoundedInterval =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                        .EmptyRoundedInterval,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(timing.Source, source.Timing) ||
            timing.TicksPerSecond != ticksPerSecond ||
            timing.StartRounding != startRounding ||
            timing.EndRounding != endRounding)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, timing);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjectionResult
                .Projected;
    }
}
