using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSamplePresentationTimeSpanLookupTests
{
    private const uint Stts = 0x73747473;
    private const uint Ctts = 0x63747473;
    private const uint Mdhd = 0x6D646864;

    [Fact]
    public void Lookup_ConvertsPresentationIntervalAndExplicitOffset()
    {
        var result = Lookup(
            ParseTimeToSample((2, 40)),
            ParseCompositionTime(0, (2, 10)),
            Reconciliation(2, hasCompositionOffsets: true),
            ParseMediaTiming(1_000),
            sampleNumber: 2,
            out var presentationTime);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult.Found,
            result);
        Assert.Equal(
            new PlaybackIsoBmffSamplePresentationTimeSpan(
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(90),
                10,
                HasExplicitCompositionOffset: true),
            presentationTime);
    }

    [Fact]
    public void Lookup_DurationIsDifferenceOfRoundedIntervalEdges()
    {
        var result = Lookup(
            ParseTimeToSample((2, 1)),
            null,
            Reconciliation(2, hasCompositionOffsets: false),
            ParseMediaTiming(6),
            sampleNumber: 2,
            out var presentationTime);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult.Found,
            result);
        Assert.Equal(TimeSpan.FromTicks(1_666_666), presentationTime?.Start);
        Assert.Equal(TimeSpan.FromTicks(3_333_333), presentationTime?.EndExclusive);
        Assert.Equal(TimeSpan.FromTicks(1_666_667), presentationTime?.Duration);
        Assert.False(presentationTime?.HasExplicitCompositionOffset);
    }

    [Fact]
    public void Lookup_NegativeStartAndPositiveEndRemainOrdered()
    {
        var result = Lookup(
            ParseTimeToSample((1, 2)),
            ParseCompositionTime(1, (1, unchecked((uint)-1))),
            Reconciliation(1, hasCompositionOffsets: true),
            ParseMediaTiming(2),
            sampleNumber: 1,
            out var presentationTime);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult.Found,
            result);
        Assert.Equal(TimeSpan.FromMilliseconds(-500), presentationTime?.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(500), presentationTime?.EndExclusive);
        Assert.Equal(TimeSpan.FromSeconds(1), presentationTime?.Duration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Lookup_SampleBoundsArePropagated(ulong sampleNumber)
    {
        var result = Lookup(
            ParseTimeToSample((1, 1)),
            null,
            Reconciliation(1, hasCompositionOffsets: false),
            ParseMediaTiming(1),
            sampleNumber,
            out var presentationTime);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .SampleNumberOutOfRange,
            result);
        Assert.Null(presentationTime);
    }

    [Fact]
    public void Lookup_InvalidReconciliationIsRejected()
    {
        var result = Lookup(
            ParseTimeToSample((2, 1)),
            null,
            Reconciliation(1, hasCompositionOffsets: false),
            ParseMediaTiming(1),
            1,
            out var presentationTime);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .InvalidReconciliation,
            result);
        Assert.Null(presentationTime);
    }

    [Fact]
    public void Lookup_MissingMediaTimingIsRejected()
    {
        var result = Lookup(
            ParseTimeToSample((1, 1)),
            null,
            Reconciliation(1, hasCompositionOffsets: false),
            null,
            1,
            out var presentationTime);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .MissingMediaTiming,
            result);
        Assert.Null(presentationTime);
    }

    [Fact]
    public void Lookup_InvalidConversionConfigurationIsRejected()
    {
        var options = new PlaybackIsoBmffMediaTimeConversionOptions
        {
            MaximumMagnitude = TimeSpan.Zero,
        };

        var result = PlaybackIsoBmffSamplePresentationTimeSpanLookup.Lookup(
            ParseTimeToSample((1, 1)),
            null,
            Reconciliation(1, hasCompositionOffsets: false),
            ParseMediaTiming(1),
            1,
            out var presentationTime,
            options);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .InvalidConversionConfiguration,
            result);
        Assert.Null(presentationTime);
    }

    [Fact]
    public void Lookup_EndBeyondMagnitudeLimitIsRejected()
    {
        var options = new PlaybackIsoBmffMediaTimeConversionOptions
        {
            MaximumMagnitude = TimeSpan.FromSeconds(1),
        };

        var result = PlaybackIsoBmffSamplePresentationTimeSpanLookup.Lookup(
            ParseTimeToSample((1, 2)),
            null,
            Reconciliation(1, hasCompositionOffsets: false),
            ParseMediaTiming(1),
            1,
            out var presentationTime,
            options);

        Assert.Equal(
            PlaybackIsoBmffSamplePresentationTimeSpanLookupResult
                .MagnitudeExceeded,
            result);
        Assert.Null(presentationTime);
    }

    private static PlaybackIsoBmffSamplePresentationTimeSpanLookupResult Lookup(
        PlaybackIsoBmffTimeToSampleBox timeToSample,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffReconciledSampleCount reconciliation,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        ulong sampleNumber,
        out PlaybackIsoBmffSamplePresentationTimeSpan? presentationTime) =>
        PlaybackIsoBmffSamplePresentationTimeSpanLookup.Lookup(
            timeToSample,
            compositionTimeToSample,
            reconciliation,
            mediaTiming,
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

    private static PlaybackIsoBmffMediaHeaderTimingBox ParseMediaTiming(
        uint timescale)
    {
        var box = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Mdhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), timescale);
        BinaryPrimitives.WriteUInt16BigEndian(
            box.AsSpan(28, 2),
            PackLanguage('u', 'n', 'd'));
        Assert.Equal(
            PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed,
            PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(box, out var parsed));
        return Assert.IsType<PlaybackIsoBmffMediaHeaderTimingBox>(parsed);
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

    private static ushort PackLanguage(char first, char second, char third) =>
        (ushort)(((first - 'a' + 1) << 10) |
            ((second - 'a' + 1) << 5) |
            (third - 'a' + 1));
}
