using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleDecodeTimeLookupTests
{
    private const uint Stts = 0x73747473;

    [Fact]
    public void Lookup_SampleWithinFirstRunUsesRunRelativeOffset()
    {
        var result = Lookup(
            Parse((3, 1_000), (2, 2_000)),
            sampleCount: 5,
            sampleNumber: 2,
            out var decodeTime);

        Assert.Equal(PlaybackIsoBmffSampleDecodeTimeLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffSampleDecodeTime(1_000, 1_000, 2_000),
            decodeTime);
    }

    [Fact]
    public void Lookup_SampleAcrossRunsIncludesPrecedingRunDuration()
    {
        var result = Lookup(
            Parse((3, 1_000), (2, 2_000)),
            sampleCount: 5,
            sampleNumber: 4,
            out var decodeTime);

        Assert.Equal(PlaybackIsoBmffSampleDecodeTimeLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffSampleDecodeTime(3_000, 2_000, 5_000),
            decodeTime);
    }

    [Fact]
    public void Lookup_ZeroDeltaPreservesEqualStartAndEnd()
    {
        var result = Lookup(
            Parse((2, 0), (1, 5)),
            sampleCount: 3,
            sampleNumber: 2,
            out var decodeTime);

        Assert.Equal(PlaybackIsoBmffSampleDecodeTimeLookupResult.Found, result);
        Assert.Equal(new PlaybackIsoBmffSampleDecodeTime(0, 0, 0), decodeTime);
    }

    [Fact]
    public void Lookup_LargeUniformRunDoesNotExpandSamples()
    {
        var result = Lookup(
            Parse((uint.MaxValue, uint.MaxValue)),
            sampleCount: uint.MaxValue,
            sampleNumber: uint.MaxValue,
            out var decodeTime);

        Assert.Equal(PlaybackIsoBmffSampleDecodeTimeLookupResult.Found, result);
        var expectedStart = (ulong)(uint.MaxValue - 1) * uint.MaxValue;
        Assert.Equal(
            new PlaybackIsoBmffSampleDecodeTime(
                expectedStart,
                uint.MaxValue,
                expectedStart + uint.MaxValue),
            decodeTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Lookup_SampleNumberMustBeInRange(ulong sampleNumber)
    {
        var result = Lookup(
            Parse((5, 1)),
            sampleCount: 5,
            sampleNumber,
            out var decodeTime);

        Assert.Equal(
            PlaybackIsoBmffSampleDecodeTimeLookupResult.SampleNumberOutOfRange,
            result);
        Assert.Null(decodeTime);
    }

    [Fact]
    public void Lookup_ReconciledCountMustMatchTimeTable()
    {
        var result = Lookup(
            Parse((5, 1)),
            sampleCount: 4,
            sampleNumber: 1,
            out var decodeTime);

        Assert.Equal(
            PlaybackIsoBmffSampleDecodeTimeLookupResult.InvalidReconciliation,
            result);
        Assert.Null(decodeTime);
    }

    [Fact]
    public void Lookup_MissingInputsAreInvalid()
    {
        var reconciliation = Reconciliation(1);

        Assert.Equal(
            PlaybackIsoBmffSampleDecodeTimeLookupResult.InvalidReconciliation,
            PlaybackIsoBmffSampleDecodeTimeLookup.Lookup(
                null,
                reconciliation,
                1,
                out var missingTable));
        Assert.Null(missingTable);
        Assert.Equal(
            PlaybackIsoBmffSampleDecodeTimeLookupResult.InvalidReconciliation,
            PlaybackIsoBmffSampleDecodeTimeLookup.Lookup(
                Parse((1, 1)),
                null,
                1,
                out var missingReconciliation));
        Assert.Null(missingReconciliation);
    }

    [Fact]
    public void Lookup_EmptyTableHasNoSample()
    {
        var result = Lookup(
            Parse(),
            sampleCount: 0,
            sampleNumber: 1,
            out var decodeTime);

        Assert.Equal(
            PlaybackIsoBmffSampleDecodeTimeLookupResult.SampleNumberOutOfRange,
            result);
        Assert.Null(decodeTime);
    }

    private static PlaybackIsoBmffSampleDecodeTimeLookupResult Lookup(
        PlaybackIsoBmffTimeToSampleBox timeToSample,
        uint sampleCount,
        ulong sampleNumber,
        out PlaybackIsoBmffSampleDecodeTime? decodeTime) =>
        PlaybackIsoBmffSampleDecodeTimeLookup.Lookup(
            timeToSample,
            Reconciliation(sampleCount),
            sampleNumber,
            out decodeTime);

    private static PlaybackIsoBmffReconciledSampleCount Reconciliation(
        uint sampleCount) =>
        new(sampleCount, 0, PlaybackIsoBmffSampleSizeTableKind.Standard);

    private static PlaybackIsoBmffTimeToSampleBox Parse(
        params (uint Count, uint Delta)[] entries)
    {
        var box = new byte[16 + (entries.Length * 8)];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stts);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 16 + (index * 8);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset, 4),
                entries[index].Count);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset + 4, 4),
                entries[index].Delta);
        }

        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(parsed);
    }
}
