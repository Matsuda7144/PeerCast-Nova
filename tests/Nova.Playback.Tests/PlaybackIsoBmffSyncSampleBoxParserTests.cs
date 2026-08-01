using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSyncSampleBoxParserTests
{
    private const uint Stss = 0x73747373;
    private const uint Stbl = 0x7374626C;

    [Fact]
    public void Parse_ProjectsStrictlyIncreasingSampleNumbers()
    {
        var result = PlaybackIsoBmffSyncSampleBoxParser.Parse(
            CreateBox(2, 5, uint.MaxValue),
            out var syncSample);

        Assert.Equal(PlaybackIsoBmffSyncSampleBoxParseResult.Parsed, result);
        var parsed = Assert.IsType<PlaybackIsoBmffSyncSampleBox>(syncSample);
        Assert.Equal([2U, 5U, uint.MaxValue], parsed.SampleNumbers);
        Assert.IsNotType<uint[]>(parsed.SampleNumbers);
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffSyncSampleBoxParser.Parse(
            CreateBox(),
            out var syncSample);

        Assert.Equal(PlaybackIsoBmffSyncSampleBoxParseResult.Parsed, result);
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffSyncSampleBox>(
                syncSample).SampleNumbers);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBoxWithHeader([1, 2], extended: true);
        var toEnd = CreateBoxWithHeader([3, 4], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.Parsed,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            [1U, 2U],
            Assert.IsType<PlaybackIsoBmffSyncSampleBox>(
                extendedTable).SampleNumbers);
        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.Parsed,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            [3U, 4U],
            Assert.IsType<PlaybackIsoBmffSyncSampleBox>(
                toEndTable).SampleNumbers);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffSyncSampleBoxParser.Parse(
            [],
            out var syncSample,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.InvalidConfiguration,
            result);
        Assert.Null(syncSample);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffSyncSampleBoxParser.Parse(
            CreateBox(1, 2),
            out var syncSample,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(syncSample);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Parse_PayloadLengthMustBeExact(int adjustment)
    {
        var original = CreateBox(1);
        var box = ResizeCompleteBox(original, adjustment);

        var result = PlaybackIsoBmffSyncSampleBoxParser.Parse(
            box,
            out var syncSample);

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.InvalidPayload,
            result);
        Assert.Null(syncSample);
    }

    [Fact]
    public void Parse_ZeroSampleNumberIsRejected()
    {
        var result = PlaybackIsoBmffSyncSampleBoxParser.Parse(
            CreateBox(0),
            out var syncSample);

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.InvalidSampleNumber,
            result);
        Assert.Null(syncSample);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void Parse_SampleNumbersMustStrictlyIncrease(uint secondSampleNumber)
    {
        var result = PlaybackIsoBmffSyncSampleBoxParser.Parse(
            CreateBox(2, secondSampleNumber),
            out var syncSample);

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.NonIncreasingSampleNumber,
            result);
        Assert.Null(syncSample);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(
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
            PlaybackIsoBmffSyncSampleBoxParseResult.WrongBoxType,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.NeedMoreData,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffSyncSampleBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffSyncSampleBoxParser.Parse(
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
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Stss);
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
