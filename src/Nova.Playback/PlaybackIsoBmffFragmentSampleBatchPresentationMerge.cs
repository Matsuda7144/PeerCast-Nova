using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult
{
    Merged,
    InvalidInput,
    InvalidConfiguration,
    SampleLimitExceeded,
    MediaTimescaleMismatch,
}

public sealed record PlaybackIsoBmffFragmentSampleBatchPresentationMergeOptions
{
    public const int DefaultMaximumSampleCount = 4_096;
    public const int MaximumSampleCountHardLimit = 65_536;

    public int MaximumSampleCount { get; init; } = DefaultMaximumSampleCount;
}

public readonly record struct PlaybackIsoBmffFragmentBatchPresentationSample(
    int BatchIndex,
    uint SequenceNumber,
    PlaybackIsoBmffFragmentSampleRationalTime Sample);

public sealed class PlaybackIsoBmffFragmentSampleBatchPresentationMerge
{
    internal PlaybackIsoBmffFragmentSampleBatchPresentationMerge(
        PlaybackIsoBmffFragmentSampleBatchChain source,
        ReadOnlyCollection<PlaybackIsoBmffFragmentBatchPresentationSample>
            samples)
    {
        Source = source;
        Samples = samples;
    }

    public PlaybackIsoBmffFragmentSampleBatchChain Source { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentBatchPresentationSample> Samples
    { get; }
}

public static class PlaybackIsoBmffFragmentSampleBatchPresentationMerger
{
    public static PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult Merge(
        PlaybackIsoBmffFragmentSampleBatchChain? source,
        out PlaybackIsoBmffFragmentSampleBatchPresentationMerge? merge,
        PlaybackIsoBmffFragmentSampleBatchPresentationMergeOptions? options =
            null)
    {
        merge = null;
        options ??=
            new PlaybackIsoBmffFragmentSampleBatchPresentationMergeOptions();
        if (options.MaximumSampleCount is < 0 or
            > PlaybackIsoBmffFragmentSampleBatchPresentationMergeOptions
                .MaximumSampleCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult
                    .InvalidConfiguration;
        }

        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult
                    .InvalidInput;
        }

        var sampleCount = 0;
        foreach (var batch in source.Batches)
        {
            if (batch.PresentationOrder.Samples.Count >
                options.MaximumSampleCount - sampleCount)
            {
                return
                    PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult
                        .SampleLimitExceeded;
            }

            sampleCount += batch.PresentationOrder.Samples.Count;
        }

        var samples = new PlaybackIsoBmffFragmentBatchPresentationSample[
            sampleCount];
        var target = 0;
        for (var batchIndex = 0;
            batchIndex < source.Batches.Count;
            batchIndex++)
        {
            var batch = source.Batches[batchIndex];
            foreach (var sample in batch.PresentationOrder.Samples)
            {
                if (sample.MediaTimescale != source.MediaTimescale)
                {
                    return
                        PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult
                            .MediaTimescaleMismatch;
                }

                samples[target++] = new(
                    batchIndex,
                    batch.SequenceNumber,
                    sample);
            }
        }

        Array.Sort(samples, Compare);
        merge = new(source, Array.AsReadOnly(samples));
        return PlaybackIsoBmffFragmentSampleBatchPresentationMergeResult.Merged;
    }

    private static int Compare(
        PlaybackIsoBmffFragmentBatchPresentationSample left,
        PlaybackIsoBmffFragmentBatchPresentationSample right)
    {
        var result = left.Sample.PresentationSecondsNumerator.CompareTo(
            right.Sample.PresentationSecondsNumerator);
        if (result != 0)
        {
            return result;
        }

        result = left.Sample.DecodeSecondsNumerator.CompareTo(
            right.Sample.DecodeSecondsNumerator);
        if (result != 0)
        {
            return result;
        }

        result = left.BatchIndex.CompareTo(right.BatchIndex);
        return result != 0
            ? result
            : left.Sample.SampleIndex.CompareTo(right.Sample.SampleIndex);
    }
}
