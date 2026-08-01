using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleSizeBoxParserTests
{
    private const uint Stsz = 0x7374737A;
    private const uint Stbl = 0x7374626C;

    [Fact]
    public void Parse_ProjectsIndividualSizesAndAggregate()
    {
        var result = PlaybackIsoBmffSampleSizeBoxParser.Parse(
            CreateVariableBox(7, 0, uint.MaxValue),
            out var sampleSize);

        Assert.Equal(PlaybackIsoBmffSampleSizeBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffSampleSizeBox>(sampleSize);
        Assert.Equal((uint)0, parsed.DefaultSampleSize);
        Assert.Equal((uint)3, parsed.SampleCount);
        Assert.Equal([7U, 0U, uint.MaxValue], parsed.SampleSizes);
        Assert.IsNotType<uint[]>(parsed.SampleSizes);
        Assert.Equal((ulong)uint.MaxValue + 7, parsed.TotalSampleBytes);
    }

    [Fact]
    public void Parse_UniformSizeDoesNotExpandEntries()
    {
        var result = PlaybackIsoBmffSampleSizeBoxParser.Parse(
            CreateUniformBox(uint.MaxValue, uint.MaxValue),
            out var sampleSize,
            new() { MaximumEntryCount = 0 });

        Assert.Equal(PlaybackIsoBmffSampleSizeBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffSampleSizeBox>(sampleSize);
        Assert.Equal(uint.MaxValue, parsed.DefaultSampleSize);
        Assert.Equal(uint.MaxValue, parsed.SampleCount);
        Assert.Empty(parsed.SampleSizes);
        Assert.Equal(
            (ulong)uint.MaxValue * uint.MaxValue,
            parsed.TotalSampleBytes);
    }

    [Fact]
    public void Parse_EmptyVariableTableIsValid()
    {
        var result = PlaybackIsoBmffSampleSizeBoxParser.Parse(
            CreateVariableBox(),
            out var sampleSize);

        Assert.Equal(PlaybackIsoBmffSampleSizeBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffSampleSizeBox>(sampleSize);
        Assert.Equal((uint)0, parsed.SampleCount);
        Assert.Empty(parsed.SampleSizes);
        Assert.Equal((ulong)0, parsed.TotalSampleBytes);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateVariableBox([1, 2], extended: true);
        var toEnd = CreateUniformBox(3, 2, extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            (ulong)3,
            Assert.IsType<PlaybackIsoBmffSampleSizeBox>(
                extendedTable).TotalSampleBytes);
        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            (ulong)6,
            Assert.IsType<PlaybackIsoBmffSampleSizeBox>(
                toEndTable).TotalSampleBytes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffSampleSizeBoxParser.Parse(
            [],
            out var sampleSize,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(sampleSize);
    }

    [Fact]
    public void Parse_VariableEntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffSampleSizeBoxParser.Parse(
            CreateVariableBox(1, 2),
            out var sampleSize,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(sampleSize);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_VariablePayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateVariableBox(1);
        var box = ResizeCompleteBox(original, adjustment);

        var result = PlaybackIsoBmffSampleSizeBoxParser.Parse(
            box,
            out var sampleSize);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.InvalidPayload,
            result);
        Assert.Null(sampleSize);
    }

    [Fact]
    public void Parse_UniformPayloadRejectsUnexpectedEntryBytes()
    {
        var original = CreateUniformBox(4, 2);
        var box = ResizeCompleteBox(original, 4);

        var result = PlaybackIsoBmffSampleSizeBoxParser.Parse(
            box,
            out var sampleSize);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.InvalidPayload,
            result);
        Assert.Null(sampleSize);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateVariableBox();
        version[8] = 1;
        var flags = CreateVariableBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(
                flags,
                out var flagsTable));
        Assert.Null(flagsTable);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateVariableBox();
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Stbl);
        var shortInput = CreateVariableBox();
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateVariableBox();
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.WrongBoxType,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.NeedMoreData,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffSampleSizeBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffSampleSizeBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateVariableBox(params uint[] sampleSizes) =>
        CreateVariableBox(sampleSizes, extended: false, extendsToEnd: false);

    private static byte[] CreateVariableBox(
        uint[] sampleSizes,
        bool extended = false,
        bool extendsToEnd = false) =>
        CreateBox(0, (uint)sampleSizes.Length, sampleSizes, extended, extendsToEnd);

    private static byte[] CreateUniformBox(
        uint sampleSize,
        uint sampleCount,
        bool extended = false,
        bool extendsToEnd = false) =>
        CreateBox(sampleSize, sampleCount, [], extended, extendsToEnd);

    private static byte[] CreateBox(
        uint sampleSize,
        uint sampleCount,
        uint[] entries,
        bool extended,
        bool extendsToEnd)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 12 + (entries.Length * 4)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stsz);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            sampleSize);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 8, 4),
            sampleCount);
        for (var index = 0; index < entries.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(headerSize + 12 + (index * 4), 4),
                entries[index]);
        }

        return box;
    }

    private static byte[] ResizeCompleteBox(byte[] original, int adjustment)
    {
        var box = new byte[original.Length + adjustment];
        original.AsSpan(0, Math.Min(original.Length, box.Length)).CopyTo(box);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4), (uint)box.Length);
        return box;
    }
}
