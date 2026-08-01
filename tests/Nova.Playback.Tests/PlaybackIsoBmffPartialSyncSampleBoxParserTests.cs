using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffPartialSyncSampleBoxParserTests
{
    private const uint Stps = 0x73747073;
    private const uint Stbl = 0x7374626C;

    [Fact]
    public void Parse_ProjectsStrictlyIncreasingSampleNumbers()
    {
        var result = PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
            CreateBox(2, 5, uint.MaxValue),
            out var partialSyncSample);

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffPartialSyncSampleBox>(
            partialSyncSample);
        Assert.Equal([2U, 5U, uint.MaxValue], parsed.SampleNumbers);
        Assert.IsNotType<uint[]>(parsed.SampleNumbers);
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
            CreateBox(),
            out var partialSyncSample);

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.Parsed,
            result);
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffPartialSyncSampleBox>(
                partialSyncSample).SampleNumbers);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBoxWithHeader([1, 2], extended: true);
        var toEnd = CreateBoxWithHeader([3, 4], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.Parsed,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            [1U, 2U],
            Assert.IsType<PlaybackIsoBmffPartialSyncSampleBox>(
                extendedTable).SampleNumbers);
        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.Parsed,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            [3U, 4U],
            Assert.IsType<PlaybackIsoBmffPartialSyncSampleBox>(
                toEndTable).SampleNumbers);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
            [],
            out var partialSyncSample,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(partialSyncSample);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
            CreateBox(1, 2),
            out var partialSyncSample,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(partialSyncSample);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(1);
        var box = ResizeCompleteBox(original, adjustment);

        var result = PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
            box,
            out var partialSyncSample);

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.InvalidPayload,
            result);
        Assert.Null(partialSyncSample);
    }

    [Fact]
    public void Parse_ZeroSampleNumberIsRejected()
    {
        var result = PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
            CreateBox(0),
            out var partialSyncSample);

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.InvalidSampleNumber,
            result);
        Assert.Null(partialSyncSample);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void Parse_SampleNumbersMustStrictlyIncrease(uint secondSampleNumber)
    {
        var result = PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
            CreateBox(2, secondSampleNumber),
            out var partialSyncSample);

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult
                .NonIncreasingSampleNumber,
            result);
        Assert.Null(partialSyncSample);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                flags,
                out var flagsTable));
        Assert.Null(flagsTable);
    }

    [Fact]
    public void Parse_WrongTypeAndCompleteLengthFailuresAreDistinct()
    {
        var wrongType = CreateBox();
        BinaryPrimitives.WriteUInt32BigEndian(wrongType.AsSpan(4, 4), Stbl);
        var shortInput = CreateBox();
        BinaryPrimitives.WriteUInt32BigEndian(
            shortInput.AsSpan(0, 4),
            (uint)shortInput.Length + 1);
        var original = CreateBox();
        var trailingInput = new byte[original.Length + 1];
        original.CopyTo(trailingInput, 0);

        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.WrongBoxType,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.NeedMoreData,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffPartialSyncSampleBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffPartialSyncSampleBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(params uint[] sampleNumbers) =>
        CreateBoxWithHeader(sampleNumbers);

    private static byte[] CreateBoxWithHeader(
        uint[] sampleNumbers,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 8 + (sampleNumbers.Length * 4)];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stps);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(headerSize + 4, 4),
            (uint)sampleNumbers.Length);
        for (var index = 0; index < sampleNumbers.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(headerSize + 8 + (index * 4), 4),
                sampleNumbers[index]);
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
