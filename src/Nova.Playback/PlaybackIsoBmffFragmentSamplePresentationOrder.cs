using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSamplePresentationOrderResult
{
    Ordered,
    InvalidInput,
    InvalidSampleIndex,
    MediaTimescaleMismatch,
}

public sealed class PlaybackIsoBmffFragmentSamplePresentationOrder
{
    internal PlaybackIsoBmffFragmentSamplePresentationOrder(
        PlaybackIsoBmffFragmentSampleRationalTimeProjection source,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleRationalTime> samples)
    {
        Source = source;
        Samples = samples;
    }

    public PlaybackIsoBmffFragmentSampleRationalTimeProjection Source { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleRationalTime> Samples
    { get; }
}

public static class PlaybackIsoBmffFragmentSamplePresentationOrderer
{
    public static PlaybackIsoBmffFragmentSamplePresentationOrderResult Order(
        PlaybackIsoBmffFragmentSampleRationalTimeProjection? source,
        out PlaybackIsoBmffFragmentSamplePresentationOrder? order)
    {
        order = null;
        if (source is null || source.MediaTimescale == 0)
        {
            return PlaybackIsoBmffFragmentSamplePresentationOrderResult.InvalidInput;
        }

        var samples = new PlaybackIsoBmffFragmentSampleRationalTime[
            source.Samples.Count];
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = source.Samples[index];
            if (sample.SampleIndex != index)
            {
                return
                    PlaybackIsoBmffFragmentSamplePresentationOrderResult
                        .InvalidSampleIndex;
            }

            if (sample.MediaTimescale != source.MediaTimescale)
            {
                return
                    PlaybackIsoBmffFragmentSamplePresentationOrderResult
                        .MediaTimescaleMismatch;
            }

            samples[index] = sample;
        }

        Array.Sort(samples, Compare);
        order = new(source, Array.AsReadOnly(samples));
        return PlaybackIsoBmffFragmentSamplePresentationOrderResult.Ordered;
    }

    private static int Compare(
        PlaybackIsoBmffFragmentSampleRationalTime left,
        PlaybackIsoBmffFragmentSampleRationalTime right)
    {
        var result = left.PresentationSecondsNumerator.CompareTo(
            right.PresentationSecondsNumerator);
        if (result != 0)
        {
            return result;
        }

        result = left.DecodeSecondsNumerator.CompareTo(
            right.DecodeSecondsNumerator);
        return result != 0
            ? result
            : left.SampleIndex.CompareTo(right.SampleIndex);
    }
}
