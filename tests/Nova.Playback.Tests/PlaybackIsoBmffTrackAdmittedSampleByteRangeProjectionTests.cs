using Result = PeerCast.Nova.Playback.PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffTrackAdmittedSampleByteRangeProjectionTests
{
    [Fact]
    public void Project_RetainsExactAdmittedSamplesAndAbsoluteRanges()
    {
        var source = Selection();
        var sampleToChunk = SampleToChunk();
        var reconciliation = ChunkReconciliation();
        var chunkOffset = ChunkOffset();
        var sampleSize = SampleSize();

        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjector.Project(
                source,
                sampleToChunk,
                reconciliation,
                chunkOffset,
                null,
                sampleSize,
                null,
                out var projection));
        var projected = Assert.IsType<
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection>(projection);
        Assert.Same(source, projected.Source);
        Assert.Same(sampleToChunk, projected.SampleToChunk);
        Assert.Same(reconciliation, projected.ChunkReconciliation);
        Assert.Same(chunkOffset, projected.ChunkOffset);
        Assert.Same(sampleSize, projected.SampleSize);
        Assert.Collection(
            projected.Entries,
            first => AssertEntry(first, source.Entries[0], 100),
            second => AssertEntry(second, source.Entries[1], 104));
    }

    [Fact]
    public void Project_RejectsMissingSelection()
    {
        AssertResult(Result.MissingSelection, null, ChunkReconciliation());
    }

    [Fact]
    public void Project_RejectsMissingChunkReconciliation()
    {
        AssertResult(Result.InvalidReconciliation, Selection(), null);
    }

    [Fact]
    public void Project_RejectsChunkSampleCountMismatch()
    {
        AssertResult(
            Result.InvalidReconciliation,
            Selection(),
            new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 2));
    }

    private static void AssertEntry(
        PlaybackIsoBmffTrackAdmittedSampleByteRange entry,
        PlaybackIsoBmffTrackSampleEditAdmission source,
        ulong expectedOffset)
    {
        Assert.Same(source, entry.Source);
        Assert.Same(source.Admission, entry.EditedRange.Admission);
        Assert.Equal(source.SampleNumber, entry.SampleNumber);
        Assert.Equal(
            new PlaybackIsoBmffSampleByteRange(
                expectedOffset,
                4,
                expectedOffset + 4),
            entry.Range);
        Assert.Equal(1U, entry.SampleDescriptionIndex);
    }

    private static void AssertResult(
        Result expected,
        PlaybackIsoBmffTrackAdmittedSampleSelection? source,
        PlaybackIsoBmffReconciledChunkTable? reconciliation)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjector.Project(
                source,
                SampleToChunk(),
                reconciliation,
                ChunkOffset(),
                null,
                SampleSize(),
                null,
                out var projection));
        Assert.Null(projection);
    }

    private static PlaybackIsoBmffTrackAdmittedSampleSelection Selection() =>
        PlaybackIsoBmffTrackAdmittedSampleSelectionTests.Selection();

    private static PlaybackIsoBmffSampleToChunkBox SampleToChunk() =>
        PlaybackIsoBmffEditedSampleByteRangeLookupTests.ParseSampleToChunk(
            (1, 3, 1));

    private static PlaybackIsoBmffReconciledChunkTable ChunkReconciliation() =>
        new(1, 1, PlaybackIsoBmffChunkOffsetTableKind.Standard, 3);

    private static PlaybackIsoBmffChunkOffsetBox ChunkOffset() =>
        PlaybackIsoBmffEditedSampleByteRangeLookupTests.ParseChunkOffset(100);

    private static PlaybackIsoBmffSampleSizeBox SampleSize() =>
        PlaybackIsoBmffEditedSampleByteRangeLookupTests
            .ParseUniformSampleSize(4, 3);

    internal static PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection
        Projection()
    {
        Assert.Equal(
            Result.Projected,
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjector.Project(
                Selection(),
                SampleToChunk(),
                ChunkReconciliation(),
                ChunkOffset(),
                null,
                SampleSize(),
                null,
                out var projection));
        return Assert.IsType<
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection>(projection);
    }
}
