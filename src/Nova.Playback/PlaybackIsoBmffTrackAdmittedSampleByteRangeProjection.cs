using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
{
    Projected,
    MissingSelection,
    SelectionLimitExceeded,
    SourceMismatch,
    InvalidReconciliation,
    SampleNumberOutOfRange,
    OffsetOverflow,
}

public sealed class PlaybackIsoBmffTrackAdmittedSampleByteRange
{
    internal PlaybackIsoBmffTrackAdmittedSampleByteRange(
        PlaybackIsoBmffTrackSampleEditAdmission source,
        PlaybackIsoBmffEditedSampleByteRange editedRange)
    {
        Source = source;
        EditedRange = editedRange;
    }

    public PlaybackIsoBmffTrackSampleEditAdmission Source { get; }

    public PlaybackIsoBmffEditedSampleByteRange EditedRange { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public PlaybackIsoBmffSampleByteRange Range => EditedRange.Range;

    public uint SampleDescriptionIndex =>
        EditedRange.SampleDescriptionIndex;
}

public sealed class PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection
{
    internal PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection(
        PlaybackIsoBmffTrackAdmittedSampleSelection source,
        PlaybackIsoBmffSampleToChunkBox sampleToChunk,
        PlaybackIsoBmffReconciledChunkTable chunkReconciliation,
        PlaybackIsoBmffChunkOffsetBox? chunkOffset,
        PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
        PlaybackIsoBmffSampleSizeBox? sampleSize,
        PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
        ReadOnlyCollection<PlaybackIsoBmffTrackAdmittedSampleByteRange> entries)
    {
        Source = source;
        SampleToChunk = sampleToChunk;
        ChunkReconciliation = chunkReconciliation;
        ChunkOffset = chunkOffset;
        ChunkLargeOffset = chunkLargeOffset;
        SampleSize = sampleSize;
        CompactSampleSize = compactSampleSize;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackAdmittedSampleSelection Source { get; }

    public PlaybackIsoBmffSampleToChunkBox SampleToChunk { get; }

    public PlaybackIsoBmffReconciledChunkTable ChunkReconciliation { get; }

    public PlaybackIsoBmffChunkOffsetBox? ChunkOffset { get; }

    public PlaybackIsoBmffChunkLargeOffsetBox? ChunkLargeOffset { get; }

    public PlaybackIsoBmffSampleSizeBox? SampleSize { get; }

    public PlaybackIsoBmffCompactSampleSizeBox? CompactSampleSize { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackAdmittedSampleByteRange> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackAdmittedSampleByteRangeProjector
{
    public static PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
        Project(
            PlaybackIsoBmffTrackAdmittedSampleSelection? source,
            PlaybackIsoBmffSampleToChunkBox? sampleToChunk,
            PlaybackIsoBmffReconciledChunkTable? chunkReconciliation,
            PlaybackIsoBmffChunkOffsetBox? chunkOffset,
            PlaybackIsoBmffChunkLargeOffsetBox? chunkLargeOffset,
            PlaybackIsoBmffSampleSizeBox? sampleSize,
            PlaybackIsoBmffCompactSampleSizeBox? compactSampleSize,
            out PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                .MissingSelection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                .SelectionLimitExceeded;
        }

        if (sampleToChunk is null || chunkReconciliation is null)
        {
            return PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                .InvalidReconciliation;
        }

        var entries = new PlaybackIsoBmffTrackAdmittedSampleByteRange[
            source.Entries.Count];
        var priorPlanIndex = -1;
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var admission = source.Entries[index];
            var planIndex = admission.PlanEntry.SequenceIndex;
            if (!admission.IsAdmitted || admission.Admission is null ||
                planIndex < 0 ||
                planIndex >= source.Source.Entries.Count ||
                planIndex <= priorPlanIndex ||
                !ReferenceEquals(
                    admission,
                    source.Source.Entries[planIndex]))
            {
                return
                    PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                        .SourceMismatch;
            }

            var result = PlaybackIsoBmffEditedSampleByteRangeLookup.Lookup(
                admission.Admission,
                sampleToChunk,
                chunkReconciliation,
                chunkOffset,
                chunkLargeOffset,
                source.Source.Plan.Reconciliation,
                sampleSize,
                compactSampleSize,
                out var editedRange);
            if (result != PlaybackIsoBmffEditedSampleByteRangeLookupResult.Found ||
                editedRange is null)
            {
                return result switch
                {
                    PlaybackIsoBmffEditedSampleByteRangeLookupResult
                        .SampleNumberOutOfRange =>
                        PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                            .SampleNumberOutOfRange,
                    PlaybackIsoBmffEditedSampleByteRangeLookupResult
                        .OffsetOverflow =>
                        PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                            .OffsetOverflow,
                    _ =>
                        PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
                            .InvalidReconciliation,
                };
            }

            entries[index] = new(admission, editedRange);
            priorPlanIndex = planIndex;
        }

        projection = new(
            source,
            sampleToChunk,
            chunkReconciliation,
            chunkOffset,
            chunkLargeOffset,
            sampleSize,
            compactSampleSize,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult
            .Projected;
    }
}
