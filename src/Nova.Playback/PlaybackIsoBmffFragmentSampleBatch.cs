namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleBatchResult
{
    Bound,
    InvalidInput,
    TrackIdMismatch,
    SampleDescriptionIndexMismatch,
    SampleCountMismatch,
    SampleIndexMismatch,
    SampleMetadataMismatch,
    PresentationOrderMismatch,
}

public sealed class PlaybackIsoBmffFragmentSampleBatch
{
    internal PlaybackIsoBmffFragmentSampleBatch(
        PlaybackIsoBmffMovieFragmentHeaderBox fragmentHeader,
        PlaybackIsoBmffTimedFragmentRun timedRun,
        PlaybackIsoBmffFragmentSamplePresentationOrder presentationOrder)
    {
        FragmentHeader = fragmentHeader;
        TimedRun = timedRun;
        PresentationOrder = presentationOrder;
    }

    public PlaybackIsoBmffMovieFragmentHeaderBox FragmentHeader { get; }

    public PlaybackIsoBmffTimedFragmentRun TimedRun { get; }

    public PlaybackIsoBmffFragmentSamplePresentationOrder PresentationOrder
    { get; }

    public uint SequenceNumber => FragmentHeader.SequenceNumber;

    public uint TrackId => TimedRun.TrackId;

    public uint SampleDescriptionIndex => TimedRun.SampleDescriptionIndex;

    public uint MediaTimescale => PresentationOrder.Source.MediaTimescale;

    public int SampleCount => TimedRun.Samples.Count;
}

public static class PlaybackIsoBmffFragmentSampleBatchBinder
{
    public static PlaybackIsoBmffFragmentSampleBatchResult Bind(
        PlaybackIsoBmffMovieFragmentHeaderBox? fragmentHeader,
        PlaybackIsoBmffTimedFragmentRun? timedRun,
        PlaybackIsoBmffFragmentSamplePresentationOrder? presentationOrder,
        out PlaybackIsoBmffFragmentSampleBatch? batch)
    {
        batch = null;
        if (fragmentHeader is null || timedRun is null ||
            presentationOrder is null)
        {
            return PlaybackIsoBmffFragmentSampleBatchResult.InvalidInput;
        }

        var rational = presentationOrder.Source;
        if (timedRun.TrackId != rational.TrackId)
        {
            return PlaybackIsoBmffFragmentSampleBatchResult.TrackIdMismatch;
        }

        if (timedRun.SampleDescriptionIndex != rational.SampleDescriptionIndex)
        {
            return
                PlaybackIsoBmffFragmentSampleBatchResult
                    .SampleDescriptionIndexMismatch;
        }

        if (rational.MediaTimescale == 0 ||
            timedRun.Samples.Count != rational.Samples.Count ||
            rational.Samples.Count != presentationOrder.Samples.Count)
        {
            return PlaybackIsoBmffFragmentSampleBatchResult.SampleCountMismatch;
        }

        var seen = new bool[rational.Samples.Count];
        for (var index = 0; index < rational.Samples.Count; index++)
        {
            var timed = timedRun.Samples[index];
            var source = rational.Samples[index];
            if (timed.SampleIndex != index || source.SampleIndex != index)
            {
                return PlaybackIsoBmffFragmentSampleBatchResult.SampleIndexMismatch;
            }

            if (!Matches(timed, source, rational.MediaTimescale))
            {
                return
                    PlaybackIsoBmffFragmentSampleBatchResult
                        .SampleMetadataMismatch;
            }

            var ordered = presentationOrder.Samples[index];
            if (ordered.SampleIndex < 0 || ordered.SampleIndex >= seen.Length ||
                seen[ordered.SampleIndex] ||
                ordered != rational.Samples[ordered.SampleIndex])
            {
                return
                    PlaybackIsoBmffFragmentSampleBatchResult
                        .PresentationOrderMismatch;
            }

            seen[ordered.SampleIndex] = true;
        }

        batch = new(fragmentHeader, timedRun, presentationOrder);
        return PlaybackIsoBmffFragmentSampleBatchResult.Bound;
    }

    private static bool Matches(
        PlaybackIsoBmffTimedFragmentSample timed,
        PlaybackIsoBmffFragmentSampleRationalTime rational,
        uint mediaTimescale) =>
        rational.MediaTimescale == mediaTimescale &&
        rational.DecodeSecondsNumerator == timed.DecodeTime &&
        rational.DecodeEndSecondsNumeratorExclusive ==
            timed.DecodeEndTimeExclusive &&
        rational.PresentationSecondsNumerator == timed.PresentationTime &&
        rational.DurationSecondsNumerator == timed.SampleDuration &&
        rational.SampleSize == timed.SampleSize &&
        rational.SampleFlags == timed.SampleFlags;
}
