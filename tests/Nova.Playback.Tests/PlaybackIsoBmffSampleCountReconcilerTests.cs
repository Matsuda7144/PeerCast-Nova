using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleCountReconcilerTests
{
    private const uint Stts = 0x73747473;
    private const uint Stsz = 0x7374737A;
    private const uint Stz2 = 0x73747A32;
    private const uint Ctts = 0x63747473;
    private const uint Sdtp = 0x73647470;
    private const uint Stss = 0x73747373;
    private const uint Stps = 0x73747073;

    [Fact]
    public void Reconcile_ProjectsStandardSampleSizeTable()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.Equal(
            new PlaybackIsoBmffReconciledSampleCount(
                3,
                12,
                PlaybackIsoBmffSampleSizeTableKind.Standard),
            reconciliation);
    }

    [Fact]
    public void Reconcile_ProjectsCompactSampleSizeTable()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            null,
            ParseCompactSampleSize(1, 2, 3),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.Equal(
            new PlaybackIsoBmffReconciledSampleCount(
                3,
                6,
                PlaybackIsoBmffSampleSizeTableKind.Compact),
            reconciliation);
    }

    [Fact]
    public void Reconcile_EmptyTablesAreValid()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(0),
            ParseSampleSize(4, 0),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.Equal((uint)0, reconciliation?.SampleCount);
        Assert.Equal((ulong)0, reconciliation?.TotalSampleBytes);
    }

    [Fact]
    public void Reconcile_TimeToSampleIsRequired()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            null,
            ParseSampleSize(4, 1),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.MissingTimeToSample,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_ExactlyOneSampleSizeTableIsRequired()
    {
        var timing = ParseTimeToSample(1);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult
                .MissingSampleSizeTable,
            PlaybackIsoBmffSampleCountReconciler.Reconcile(
                timing,
                null,
                null,
                out var missing));
        Assert.Null(missing);
        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult
                .ConflictingSampleSizeTables,
            PlaybackIsoBmffSampleCountReconciler.Reconcile(
                timing,
                ParseSampleSize(4, 1),
                ParseCompactSampleSize(4),
                out var conflicting));
        Assert.Null(conflicting);
    }

    [Fact]
    public void Reconcile_StandardSampleCountMustMatchTiming()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(2),
            ParseSampleSize(4, 3),
            null,
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.SampleCountMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_CompactSampleCountMustMatchTiming()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(2),
            null,
            ParseCompactSampleSize(1, 2, 3),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.SampleCountMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Reconcile_MatchingCompositionCountIsProjected(byte version)
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            ParseCompositionTimeToSample(version, 3),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.True(reconciliation?.HasCompositionOffsets);
    }

    [Fact]
    public void Reconcile_CompositionCountMustMatchWhenPresent()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            ParseCompositionTimeToSample(0, 2),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult
                .CompositionSampleCountMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_MatchingDependencyCountIsProjected()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            null,
            ParseSampleDependencyType(3),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.True(reconciliation?.HasSampleDependencies);
    }

    [Fact]
    public void Reconcile_DependencyCountMustMatchWhenPresent()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            null,
            ParseSampleDependencyType(2),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult
                .DependencySampleCountMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_InRangeSyncSamplesAreProjected()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            null,
            null,
            ParseSyncSample(1, 3),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.True(reconciliation?.HasSyncSampleTable);
    }

    [Fact]
    public void Reconcile_EmptySyncSampleTableIsPresentAndValid()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(0),
            ParseSampleSize(4, 0),
            null,
            null,
            null,
            ParseSyncSample(),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.True(reconciliation?.HasSyncSampleTable);
    }

    [Fact]
    public void Reconcile_SyncSampleNumberMustBeInRange()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            null,
            null,
            ParseSyncSample(1, 4),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult
                .SyncSampleNumberOutOfRange,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_InRangePartialSyncSamplesAreProjected()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            null,
            null,
            null,
            ParsePartialSyncSample(1, 3),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.True(reconciliation?.HasPartialSyncSampleTable);
    }

    [Fact]
    public void Reconcile_EmptyPartialSyncSampleTableIsPresentAndValid()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(0),
            ParseSampleSize(4, 0),
            null,
            null,
            null,
            null,
            ParsePartialSyncSample(),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult.Reconciled,
            result);
        Assert.True(reconciliation?.HasPartialSyncSampleTable);
    }

    [Fact]
    public void Reconcile_PartialSyncSampleNumberMustBeInRange()
    {
        var result = PlaybackIsoBmffSampleCountReconciler.Reconcile(
            ParseTimeToSample(3),
            ParseSampleSize(4, 3),
            null,
            null,
            null,
            null,
            ParsePartialSyncSample(1, 4),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffSampleCountReconciliationResult
                .PartialSyncSampleNumberOutOfRange,
            result);
        Assert.Null(reconciliation);
    }

    private static PlaybackIsoBmffTimeToSampleBox ParseTimeToSample(
        uint sampleCount)
    {
        var entryCount = sampleCount == 0 ? 0 : 1;
        var box = new byte[16 + (entryCount * 8)];
        WriteHeader(box, Stts);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)entryCount);
        if (entryCount != 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(16, 4),
                sampleCount);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), 1);
        }

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(parsed);
    }

    private static PlaybackIsoBmffSampleSizeBox ParseSampleSize(
        uint defaultSize,
        uint sampleCount)
    {
        var box = new byte[20];
        WriteHeader(box, Stsz);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            defaultSize);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            sampleCount);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleSizeBox>(parsed);
    }

    private static PlaybackIsoBmffCompactSampleSizeBox ParseCompactSampleSize(
        params byte[] sizes)
    {
        var box = new byte[20 + sizes.Length];
        WriteHeader(box, Stz2);
        box[15] = 8;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(16, 4),
            (uint)sizes.Length);
        sizes.CopyTo(box, 20);

        Assert.Equal(
            PlaybackIsoBmffCompactSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffCompactSampleSizeBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffCompactSampleSizeBox>(parsed);
    }

    private static PlaybackIsoBmffCompositionTimeToSampleBox
        ParseCompositionTimeToSample(byte version, uint sampleCount)
    {
        var entryCount = sampleCount == 0 ? 0 : 1;
        var box = new byte[16 + (entryCount * 8)];
        WriteHeader(box, Ctts);
        box[8] = version;
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)entryCount);
        if (entryCount != 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(16, 4),
                sampleCount);
        }

        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(parsed);
    }

    private static PlaybackIsoBmffSampleDependencyTypeBox
        ParseSampleDependencyType(int sampleCount)
    {
        var box = new byte[12 + sampleCount];
        WriteHeader(box, Sdtp);

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffSampleDependencyTypeBox>(parsed);
    }

    private static PlaybackIsoBmffSyncSampleBox ParseSyncSample(
        params uint[] sampleNumbers)
    {
        var box = new byte[16 + (sampleNumbers.Length * 4)];
        WriteHeader(box, Stss);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)sampleNumbers.Length);
        for (var index = 0; index < sampleNumbers.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(16 + (index * 4), 4),
                sampleNumbers[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.Parsed,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffSyncSampleBox>(parsed);
    }

    private static PlaybackIsoBmffPartialSyncSampleBox ParsePartialSyncSample(
        params uint[] sampleNumbers)
    {
        var box = new byte[16 + (sampleNumbers.Length * 4)];
        WriteHeader(box, Stps);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)sampleNumbers.Length);
        for (var index = 0; index < sampleNumbers.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(16 + (index * 4), 4),
                sampleNumbers[index]);
        }

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.Parsed,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffPartialSyncSampleBox>(parsed);
    }

    private static void WriteHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
