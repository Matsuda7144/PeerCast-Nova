namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentContinuitySummaryResult
{
    Projected,
    InvalidInput,
}

public enum PlaybackIsoBmffFragmentContinuityDisposition
{
    Continuous,
    DiscontinuityHandlingRequired,
}

public sealed class PlaybackIsoBmffFragmentContinuitySummary
{
    internal PlaybackIsoBmffFragmentContinuitySummary(
        PlaybackIsoBmffFragmentContinuityChain chain,
        int continuousTransitionCount,
        int sequenceDiscontinuityCount,
        int decodeGapCount,
        int decodeOverlapCount)
    {
        Chain = chain;
        ContinuousTransitionCount = continuousTransitionCount;
        SequenceDiscontinuityCount = sequenceDiscontinuityCount;
        DecodeGapCount = decodeGapCount;
        DecodeOverlapCount = decodeOverlapCount;
    }

    public PlaybackIsoBmffFragmentContinuityChain Chain { get; }

    public int TransitionCount => Chain.Transitions.Count;

    public int ContinuousTransitionCount { get; }

    public int SequenceDiscontinuityCount { get; }

    public int DecodeGapCount { get; }

    public int DecodeOverlapCount { get; }

    public PlaybackIsoBmffFragmentContinuityDisposition Disposition =>
        SequenceDiscontinuityCount == 0 &&
        DecodeGapCount == 0 &&
        DecodeOverlapCount == 0
            ? PlaybackIsoBmffFragmentContinuityDisposition.Continuous
            : PlaybackIsoBmffFragmentContinuityDisposition
                .DiscontinuityHandlingRequired;
}

public static class PlaybackIsoBmffFragmentContinuitySummaryProjector
{
    public static PlaybackIsoBmffFragmentContinuitySummaryResult Project(
        PlaybackIsoBmffFragmentContinuityChain? chain,
        out PlaybackIsoBmffFragmentContinuitySummary? summary)
    {
        summary = null;
        if (chain is null)
        {
            return PlaybackIsoBmffFragmentContinuitySummaryResult.InvalidInput;
        }

        var continuousCount = 0;
        var sequenceDiscontinuityCount = 0;
        var decodeGapCount = 0;
        var decodeOverlapCount = 0;
        foreach (var transition in chain.Transitions)
        {
            if (transition.IsContinuous)
            {
                continuousCount++;
            }

            if (!transition.Sequence.IsContinuous)
            {
                sequenceDiscontinuityCount++;
            }

            switch (transition.Decode.Kind)
            {
                case PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Gap:
                    decodeGapCount++;
                    break;
                case PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Overlap:
                    decodeOverlapCount++;
                    break;
            }
        }

        summary = new(
            chain,
            continuousCount,
            sequenceDiscontinuityCount,
            decodeGapCount,
            decodeOverlapCount);
        return PlaybackIsoBmffFragmentContinuitySummaryResult.Projected;
    }
}
