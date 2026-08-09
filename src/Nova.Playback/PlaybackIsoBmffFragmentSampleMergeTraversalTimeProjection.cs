using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
{
    Projected,
    InvalidInput,
    SampleLimitExceeded,
    InvalidMediaTimescale,
    MediaTimescaleMismatch,
    InvalidSampleTime,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalTime
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalTime(
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgement
            acknowledgement,
        uint mediaTimescale)
    {
        Acknowledgement = acknowledgement;
        MediaTimescale = mediaTimescale;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgement
        Acknowledgement
    { get; }

    public PlaybackIsoBmffFragmentBatchPresentationSample Sample =>
        Acknowledgement.Sample;

    public uint MediaTimescale { get; }

    public BigInteger DecodeSecondsNumerator =>
        Sample.Sample.DecodeSecondsNumerator;

    public BigInteger DecodeEndSecondsNumeratorExclusive =>
        Sample.Sample.DecodeEndSecondsNumeratorExclusive;

    public BigInteger PresentationSecondsNumerator =>
        Sample.Sample.PresentationSecondsNumerator;

    public BigInteger DurationSecondsNumerator =>
        Sample.Sample.DurationSecondsNumerator;

    public uint SecondsDenominator => MediaTimescale;
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection(
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView
            acknowledgementView,
        uint mediaTimescale,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleMergeTraversalTime>
            samples)
    {
        AcknowledgementView = acknowledgementView;
        MediaTimescale = mediaTimescale;
        Samples = samples;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView
        AcknowledgementView
    { get; }

    public uint MediaTimescale { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleMergeTraversalTime>
        Samples
    { get; }

    public int SampleCount => Samples.Count;
}

public static class PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult Project(
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView?
                acknowledgementView,
            out PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjection?
                projection)
    {
        projection = null;
        if (acknowledgementView is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .InvalidInput;
        }

        if (acknowledgementView.AcknowledgementCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .SampleLimitExceeded;
        }

        var mediaTimescale = acknowledgementView.Completion.Merge.Source
            .MediaTimescale;
        if (mediaTimescale is null or 0)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                    .InvalidMediaTimescale;
        }

        var samples = new PlaybackIsoBmffFragmentSampleMergeTraversalTime[
            acknowledgementView.AcknowledgementCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var acknowledgement = acknowledgementView.Acknowledgements[index];
            var sample = acknowledgement.Sample.Sample;
            if (sample.MediaTimescale != mediaTimescale)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                        .MediaTimescaleMismatch;
            }

            if (sample.DurationSecondsNumerator < BigInteger.Zero ||
                sample.DecodeEndSecondsNumeratorExclusive -
                    sample.DecodeSecondsNumerator !=
                sample.DurationSecondsNumerator)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                        .InvalidSampleTime;
            }

            samples[index] = new(acknowledgement, mediaTimescale.Value);
        }

        projection = new(
            acknowledgementView,
            mediaTimescale.Value,
            Array.AsReadOnly(samples));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalTimeProjectionResult
                .Projected;
    }
}
