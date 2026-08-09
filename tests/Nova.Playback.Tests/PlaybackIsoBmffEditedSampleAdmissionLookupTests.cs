using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditedSampleAdmissionLookupTests
{
    [Fact]
    public void Lookup_ComposesSampleNumberThroughCompleteEditAdmission()
    {
        var result = Lookup(
            1,
            ParseTimeToSample((2U, 5U)),
            new PlaybackIsoBmffReconciledSampleCount(
                2,
                0,
                PlaybackIsoBmffSampleSizeTableKind.Standard),
            ParseEditList((10UL, 0L, (short)1, (short)0)),
            out var admission);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult.Found,
            result);
        var admitted = Assert.IsType<
            PlaybackIsoBmffEditedSampleAdmission>(admission);
        Assert.Equal((ulong)1, admitted.SampleNumber);
        Assert.Equal((Int128)0, admitted.Sample.StartTimeUnits);
        Assert.Equal((Int128)5, admitted.Sample.EndTimeUnitsExclusive);
        Assert.Single(admitted.Appearances);
    }

    [Fact]
    public void Lookup_PreservesRepeatedCompleteAppearances()
    {
        var result = Lookup(
            1,
            ParseTimeToSample((1U, 2U)),
            Reconciliation(1),
            ParseEditList(
                (2UL, 0L, (short)1, (short)0),
                (2UL, 0L, (short)1, (short)0)),
            out var admission);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult.Found,
            result);
        Assert.Equal(2, admission!.Appearances.Count);
    }

    [Fact]
    public void Lookup_NotPresentedAndPartialOnlyAreDistinct()
    {
        var timing = ParseTimeToSample((1U, 5U));
        var reconciliation = Reconciliation(1);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult.NotPresented,
            Lookup(
                1,
                timing,
                reconciliation,
                ParseEditList((5UL, 10L, (short)1, (short)0)),
                out var absent));
        Assert.Null(absent);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .PartialSampleRejected,
            Lookup(
                1,
                timing,
                reconciliation,
                ParseEditList((3UL, 0L, (short)1, (short)0)),
                out var partial));
        Assert.Null(partial);
    }

    [Fact]
    public void Lookup_InvalidReconciliationAndSampleRangeAreDistinct()
    {
        var editList = ParseEditList((5UL, 0L, (short)1, (short)0));

        Assert.Equal(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidReconciliation,
            Lookup(1, null, Reconciliation(1), editList, out var invalid));
        Assert.Null(invalid);

        Assert.Equal(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .SampleNumberOutOfRange,
            Lookup(
                2,
                ParseTimeToSample((1U, 5U)),
                Reconciliation(1),
                editList,
                out var outOfRange));
        Assert.Null(outOfRange);
    }

    [Fact]
    public void Lookup_MissingEditTimingInputsAreDistinct()
    {
        var timeToSample = ParseTimeToSample((1U, 1U));
        var reconciliation = Reconciliation(1);
        var editList = ParseEditList((1UL, 0L, (short)1, (short)0));
        var movieTiming = ParseMovieTiming(1);
        var mediaTiming = ParseMediaTiming(1);

        AssertDirect(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult.MissingEditList,
            timeToSample,
            reconciliation,
            null,
            movieTiming,
            mediaTiming);
        AssertDirect(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult.MissingMovieTiming,
            timeToSample,
            reconciliation,
            editList,
            null,
            mediaTiming);
        AssertDirect(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult.MissingMediaTiming,
            timeToSample,
            reconciliation,
            editList,
            movieTiming,
            null);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65_537)]
    public void Lookup_InvalidLimitRejectsBeforeMetadataAccess(int maximum)
    {
        var result = PlaybackIsoBmffEditedSampleAdmissionLookup.Lookup(
            null,
            null,
            null,
            null,
            null,
            null,
            1,
            out var admission,
            new() { MaximumOverlapCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidConfiguration,
            result);
        Assert.Null(admission);
    }

    private static PlaybackIsoBmffEditedSampleAdmissionLookupResult Lookup(
        ulong sampleNumber,
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffReconciledSampleCount reconciliation,
        PlaybackIsoBmffEditListBox editList,
        out PlaybackIsoBmffEditedSampleAdmission? admission) =>
        PlaybackIsoBmffEditedSampleAdmissionLookup.Lookup(
            timeToSample,
            null,
            reconciliation,
            editList,
            ParseMovieTiming(1),
            ParseMediaTiming(1),
            sampleNumber,
            out admission);

    private static void AssertDirect(
        PlaybackIsoBmffEditedSampleAdmissionLookupResult expected,
        PlaybackIsoBmffTimeToSampleBox timeToSample,
        PlaybackIsoBmffReconciledSampleCount reconciliation,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming)
    {
        var result = PlaybackIsoBmffEditedSampleAdmissionLookup.Lookup(
            timeToSample,
            null,
            reconciliation,
            editList,
            movieTiming,
            mediaTiming,
            1,
            out var admission);
        Assert.Equal(expected, result);
        Assert.Null(admission);
    }

    private static PlaybackIsoBmffReconciledSampleCount Reconciliation(
        uint sampleCount) => new(
            sampleCount,
            0,
            PlaybackIsoBmffSampleSizeTableKind.Standard);

    internal static PlaybackIsoBmffTimeToSampleBox ParseTimeToSample(
        params (uint Count, uint Delta)[] entries)
    {
        var box = new byte[16 + (entries.Length * 8)];
        WriteBoxHeader(box, 0x73747473);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 8);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset, 4),
                entries[index].Count);
            BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(offset + 4, 4),
                entries[index].Delta);
        }

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(parsed);
    }

    internal static PlaybackIsoBmffEditListBox ParseEditList(
        params (ulong Duration, long MediaTime, short RateInteger,
            short RateFraction)[] entries)
    {
        var box = new byte[16 + (entries.Length * 20)];
        WriteBoxHeader(box, 0x656C7374);
        box[8] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 20);
            var entry = entries[index];
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(offset, 8),
                entry.Duration);
            BinaryPrimitives.WriteInt64BigEndian(box.AsSpan(offset + 8, 8),
                entry.MediaTime);
            BinaryPrimitives.WriteInt16BigEndian(box.AsSpan(offset + 16, 2),
                entry.RateInteger);
            BinaryPrimitives.WriteInt16BigEndian(box.AsSpan(offset + 18, 2),
                entry.RateFraction);
        }

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.Parsed,
            PlaybackIsoBmffEditListBoxParser.Parse(box, out var editList));
        return Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
    }

    internal static PlaybackIsoBmffMovieHeaderTimingBox ParseMovieTiming(
        uint timescale)
    {
        var box = new byte[108];
        WriteBoxHeader(box, 0x6D766864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        Assert.Equal(
            PlaybackIsoBmffMovieHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMovieHeaderTimingBoxParser.Parse(box, out var timing));
        return Assert.IsType<PlaybackIsoBmffMovieHeaderTimingBox>(timing);
    }

    internal static PlaybackIsoBmffMediaHeaderTimingBox ParseMediaTiming(
        uint timescale)
    {
        var box = new byte[32];
        WriteBoxHeader(box, 0x6D646864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt16BigEndian(box.AsSpan(28, 2), 0x55C4);
        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(box, out var timing));
        return Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(timing);
    }

    private static void WriteBoxHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
