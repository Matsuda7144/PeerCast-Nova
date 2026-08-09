using System.Collections.ObjectModel;
using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
{
    Projected,
    MissingStepView,
    InvalidEventLimit,
    EventLimitExceeded,
    MissingAppearance,
    InvalidAppearance,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep step,
        int appearanceIndex,
        PlaybackIsoBmffSampleEditMovieOverlap appearance)
    {
        Step = step;
        AppearanceIndex = appearanceIndex;
        Appearance = appearance;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStep Step
    { get; }

    public int AppearanceIndex { get; }

    public PlaybackIsoBmffSampleEditMovieOverlap Appearance { get; }

    public int GlobalMergeIndex => Step.GlobalMergeIndex;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceAdvanceRole Role =>
        Step.Role;

    public PlaybackIsoBmffMovieTrackCodecPacket Packet => Step.Packet;

    public Int128 MovieStartNumerator => Appearance.MovieStartNumerator;

    public Int128 MovieEndNumeratorExclusive =>
        Appearance.MovieEndNumeratorExclusive;

    public uint Denominator => Appearance.Denominator;
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
                stepView,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent>
                events)
    {
        StepView = stepView;
        Events = events;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView
        StepView
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent> Events
    { get; }

    public int EventCount => Events.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
{
    public const int MaximumEventCount =
        PlaybackIsoBmffMovieCodecPacketEventProjector.MaximumEventCount;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeStepView?
                    stepView,
                int maximumEventCount,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjection?
                    projection)
    {
        projection = null;
        if (stepView is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                    .MissingStepView;
        }

        if (maximumEventCount <= 0 || maximumEventCount > MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                    .InvalidEventLimit;
        }

        var eventCount = 0;
        foreach (var step in stepView.Steps)
        {
            var appearances = step.Packet.Appearances;
            if (appearances.Count == 0)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                        .MissingAppearance;
            }

            if (appearances.Count > maximumEventCount - eventCount)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                        .EventLimitExceeded;
            }

            eventCount += appearances.Count;
        }

        var events = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent[
                eventCount];
        var eventIndex = 0;
        foreach (var step in stepView.Steps)
        {
            var appearances = step.Packet.Appearances;
            for (var appearanceIndex = 0;
                appearanceIndex < appearances.Count;
                appearanceIndex++)
            {
                var appearance = appearances[appearanceIndex];
                if (!IsValid(step.Packet, appearance))
                {
                    return
                        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                            .InvalidAppearance;
                }

                events[eventIndex++] = new(
                    step,
                    appearanceIndex,
                    appearance);
            }
        }

        Array.Sort(events, Compare);
        projection = new(stepView, Array.AsReadOnly(events));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjectionResult
                .Projected;
    }

    private static bool IsValid(
        PlaybackIsoBmffMovieTrackCodecPacket packet,
        PlaybackIsoBmffSampleEditMovieOverlap appearance) =>
        appearance.Denominator == packet.MediaTimescale &&
        appearance.MovieStartNumerator >= 0 &&
        appearance.MovieStartNumerator <
            appearance.MovieEndNumeratorExclusive &&
        appearance.ContainsCompleteSample;

    private static int Compare(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent left,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent right)
    {
        var result = CompareRational(
            left.MovieStartNumerator,
            left.Denominator,
            right.MovieStartNumerator,
            right.Denominator);
        if (result != 0)
        {
            return result;
        }

        result = CompareRational(
            left.MovieEndNumeratorExclusive,
            left.Denominator,
            right.MovieEndNumeratorExclusive,
            right.Denominator);
        if (result != 0)
        {
            return result;
        }

        result = left.GlobalMergeIndex.CompareTo(right.GlobalMergeIndex);
        return result != 0
            ? result
            : left.AppearanceIndex.CompareTo(right.AppearanceIndex);
    }

    private static int CompareRational(
        Int128 leftNumerator,
        uint leftDenominator,
        Int128 rightNumerator,
        uint rightDenominator) =>
        ((BigInteger)leftNumerator * rightDenominator).CompareTo(
            (BigInteger)rightNumerator * leftDenominator);
}
