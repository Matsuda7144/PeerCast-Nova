using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
{
    Projected,
    InvalidInput,
    SourceMismatch,
    InvalidInterval,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection
                source,
            BigInteger originTick,
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection
                timing)
    {
        Source = source;
        OriginTick = originTick;
        Timing = timing;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection
            Source
    { get; }

    public BigInteger OriginTick { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjection
        Timing
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalIntegralTimeSourceProjection?
                    source,
                BigInteger originTick,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
                    .InvalidInput;
        }

        if (!ReferenceEquals(source.Timing.Source, source.Source.Timing))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
                    .SourceMismatch;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjector
                .Project(source.Timing, originTick, out var timing);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
                    .Projected ||
            timing is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginProjectionResult
                    .InvalidInterval =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
                        .InvalidInterval,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
                        .SourceMismatch,
            };
        }

        if (!ReferenceEquals(timing.Source, source.Timing) ||
            timing.OriginTick != originTick)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, originTick, timing);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalClockOriginSourceProjectionResult
                .Projected;
    }
}
