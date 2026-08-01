using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSamplePresentationTimeLookupTests
{
    private const uint Stts = 0x73747473;
    private const uint Ctts = 0x63747473;

    [Fact]
    public void Lookup_AbsentCompositionTableUsesDecodeTimeAndImplicitZero()
    {
        var result = Lookup(
            ParseTimeToSample((2, 10)),
            null,
            Reconciliation(2, hasCompositionOffsets: false),
            sampleNumber: 2,
            out var presentationTime);

        Assert.Equal(PlaybackIsoBmffSamplePresentationTimeLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffSamplePresentationTime(
                10,
                10,
                20,
                0,
                HasExplicitCompositionOffset: false),
            presentationTime);
    }

    [Fact]
    public void Lookup_PositiveCompositionOffsetAdvancesPresentationTime()
    {
        var result = Lookup(
            ParseTimeToSample((2, 10)),
            ParseCompositionTime(0, (2, 5)),
            Reconciliation(2, hasCompositionOffsets: true),
            sampleNumber: 2,
            out var presentationTime);

        Assert.Equal(PlaybackIsoBmffSamplePresentationTimeLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffSamplePresentationTime(
                15,
                10,
                25,
                5,
                HasExplicitCompositionOffset: true),
            presentationTime);
    }

    [Fact]
    public void Lookup_NegativeCompositionOffsetCanProduceNegativeTime()
    {
        var result = Lookup(
            ParseTimeToSample((1, 10)),
            ParseCompositionTime(1, (1, unchecked((uint)-5))),
            Reconciliation(1, hasCompositionOffsets: true),
            sampleNumber: 1,
            out var presentationTime);

        Assert.Equal(PlaybackIsoBmffSamplePresentationTimeLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffSamplePresentationTime(
                -5,
                10,
                5,
                -5,
                HasExplicitCompositionOffset: true),
            presentationTime);
    }

    [Fact]
    public void Lookup_SeparateRunBoundariesAreComposedForSameSample()
    {
        var result = Lookup(
            ParseTimeToSample((1, 10), (2, 20)),
            ParseCompositionTime(1, (2, 3), (1, unchecked((uint)-4))),
            Reconciliation(3, hasCompositionOffsets: true),
            sampleNumber: 3,
            out var presentationTime);

        Assert.Equal(PlaybackIsoBmffSamplePresentationTimeLookupResult.Found, result);
        Assert.Equal(
            new PlaybackIsoBmffSamplePresentationTime(
                26,
                20,
                46,
                -4,
                HasExplicitCompositionOffset: true),
            presentationTime);
    }

    [Fact]
    public void Lookup_Int128PreservesTimeBeyondSigned64BitRange()
    {
        var result = Lookup(
            ParseTimeToSample((uint.MaxValue, uint.MaxValue)),
            ParseCompositionTime(0, (uint.MaxValue, uint.MaxValue)),
            Reconciliation(uint.MaxValue, hasCompositionOffsets: true),
            sampleNumber: uint.MaxValue,
            out var presentationTime);

        Assert.Equal(PlaybackIsoBmffSamplePresentationTimeLookupResult.Found, result);
        var decodeStart =
            (Int128)(uint.MaxValue - 1) * uint.MaxValue;
        var expectedStart = decodeStart + uint.MaxValue;
        Assert.Equal(
            new PlaybackIsoBmffSamplePresentationTime(
                expectedStart,
                uint.MaxValue,
                expectedStart + uint.MaxValue,
                uint.MaxValue,
                HasExplicitCompositionOffset: true),
            presentationTime);
        Assert.True(presentationTime?.StartTimeUnits > long.MaxValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Lookup_SampleBoundsArePropagated(ulong sampleNumber)
    {
        var result = Lookup(
            ParseTimeToSample((1, 10)),
            null,
            Reconciliation(1, hasCompositionOffsets: false),
            sampleNumber,
            out var presentationTime);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeLookupResult.SampleNumberOutOfRange,
            result);
        Assert.Null(presentationTime);
    }

    [Fact]
    public void Lookup_InvalidDecodeOrCompositionReconciliationIsRejected()
    {
        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeLookupResult.InvalidReconciliation,
            Lookup(
                ParseTimeToSample((2, 10)),
                null,
                Reconciliation(1, hasCompositionOffsets: false),
                1,
                out var decodeMismatch));
        Assert.Null(decodeMismatch);
        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeLookupResult.InvalidReconciliation,
            Lookup(
                ParseTimeToSample((1, 10)),
                ParseCompositionTime(0, (1, 0)),
                Reconciliation(1, hasCompositionOffsets: false),
                1,
                out var compositionMismatch));
        Assert.Null(compositionMismatch);
    }

    private static PlaybackIsoBmffSamplePresentationTimeLookupResult Lookup(
        PlaybackIsoBmffTimeToSampleBox timeToSample,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffReconciledSampleCount reconciliation,
        ulong sampleNumber,
        out PlaybackIsoBmffSamplePresentationTime? presentationTime) =>
        PlaybackIsoBmffSamplePresentationTimeLookup.Lookup(
            timeToSample,
            compositionTimeToSample,
            reconciliation,
            sampleNumber,
            out presentationTime);

    private static PlaybackIsoBmffReconciledSampleCount Reconciliation(
        uint sampleCount,
        bool hasCompositionOffsets) =>
        new(
            sampleCount,
            0,
            PlaybackIsoBmffSampleSizeTableKind.Standard,
            HasCompositionOffsets: hasCompositionOffsets);

    private static PlaybackIsoBmffTimeToSampleBox ParseTimeToSample(
        params (uint Count, uint Delta)[] entries)
    {
        var box = CreateRunBox(Stts, version: 0, entries);
        Assert.Equal(
            PlaybackIsoBmffTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffTimeToSampleBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffTimeToSampleBox>(parsed);
    }

    private static PlaybackIsoBmffCompositionTimeToSampleBox
        ParseCompositionTime(
            byte version,
            params (uint Count, uint Offset)[] entries)
    {
        var box = CreateRunBox(Ctts, version, entries);
        Assert.Equal(
            PlaybackIsoBmffCompositionTimeToSampleBoxParseResult.Parsed,
            PlaybackIsoBmffCompositionTimeToSampleBoxParser.Parse(
                box,
                out var parsed));
        return Assert.IsType<PlaybackIsoBmffCompositionTimeToSampleBox>(parsed);
    }

    private static byte[] CreateRunBox(
        uint type,
        byte version,
        params (uint Count, uint Value)[] entries)
    {
        var box = new byte[16 + (entries.Length * 8)];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
        box[8] = version;
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
                entries[index].Value);
        }

        return box;
    }
}
