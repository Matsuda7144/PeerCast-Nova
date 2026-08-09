using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackSampleSequencePlanCreationResult
{
    Created,
    MissingReconciliation,
    EmptySampleTable,
    InvalidFirstSampleNumber,
    InvalidMaximumSampleCount,
}

public sealed class PlaybackIsoBmffTrackSampleSequencePlanEntry
{
    internal PlaybackIsoBmffTrackSampleSequencePlanEntry(
        int sequenceIndex,
        ulong sampleNumber)
    {
        SequenceIndex = sequenceIndex;
        SampleNumber = sampleNumber;
    }

    public int SequenceIndex { get; }

    public ulong SampleNumber { get; }
}

public sealed class PlaybackIsoBmffTrackSampleSequencePlan
{
    internal PlaybackIsoBmffTrackSampleSequencePlan(
        PlaybackIsoBmffReconciledSampleCount reconciliation,
        ulong firstSampleNumber,
        int maximumSampleCount,
        ulong remainingSampleCount,
        ReadOnlyCollection<PlaybackIsoBmffTrackSampleSequencePlanEntry>
            entries)
    {
        Reconciliation = reconciliation;
        FirstSampleNumber = firstSampleNumber;
        MaximumSampleCount = maximumSampleCount;
        RemainingSampleCount = remainingSampleCount;
        Entries = entries;
    }

    public PlaybackIsoBmffReconciledSampleCount Reconciliation { get; }

    public ulong FirstSampleNumber { get; }

    public int MaximumSampleCount { get; }

    public ulong RemainingSampleCount { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackSampleSequencePlanEntry> Entries
    { get; }

    public bool IsTruncated => (ulong)Entries.Count < RemainingSampleCount;
}

public static class PlaybackIsoBmffTrackSampleSequencePlanFactory
{
    public const int MaximumPlannedSampleCount =
        PlaybackIsoBmffTrackCodecPacketSequenceFactory.MaximumPacketCount;

    public static PlaybackIsoBmffTrackSampleSequencePlanCreationResult
        TryCreate(
            PlaybackIsoBmffReconciledSampleCount? reconciliation,
            ulong firstSampleNumber,
            int maximumSampleCount,
            out PlaybackIsoBmffTrackSampleSequencePlan? plan)
    {
        plan = null;
        if (reconciliation is null)
        {
            return PlaybackIsoBmffTrackSampleSequencePlanCreationResult
                .MissingReconciliation;
        }

        if (reconciliation.SampleCount == 0)
        {
            return PlaybackIsoBmffTrackSampleSequencePlanCreationResult
                .EmptySampleTable;
        }

        if (firstSampleNumber == 0 ||
            firstSampleNumber > reconciliation.SampleCount)
        {
            return PlaybackIsoBmffTrackSampleSequencePlanCreationResult
                .InvalidFirstSampleNumber;
        }

        if (maximumSampleCount is < 1 or > MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackSampleSequencePlanCreationResult
                .InvalidMaximumSampleCount;
        }

        var remainingSampleCount =
            (ulong)reconciliation.SampleCount - firstSampleNumber + 1;
        var plannedSampleCount = (int)Math.Min(
            remainingSampleCount,
            (ulong)maximumSampleCount);
        var entries = new PlaybackIsoBmffTrackSampleSequencePlanEntry[
            plannedSampleCount];
        for (var index = 0; index < plannedSampleCount; index++)
        {
            entries[index] = new(index, firstSampleNumber + (ulong)index);
        }

        plan = new(
            reconciliation,
            firstSampleNumber,
            maximumSampleCount,
            remainingSampleCount,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackSampleSequencePlanCreationResult.Created;
    }
}
