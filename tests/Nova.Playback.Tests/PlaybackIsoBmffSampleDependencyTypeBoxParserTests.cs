using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffSampleDependencyTypeBoxParserTests
{
    private const uint Sdtp = 0x73647470;
    private const uint Stbl = 0x7374626C;

    [Fact]
    public void Parse_ProjectsTypedDependencyValues()
    {
        var result = PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
            CreateBox(0x00, 0x1a, 0x25),
            out var dependencyType);

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.Parsed,
            result);
        var parsed = Assert.IsType<PlaybackIsoBmffSampleDependencyTypeBox>(
            dependencyType);
        Assert.Equal(
            [
                new PlaybackIsoBmffSampleDependencyTypeEntry(
                    PlaybackIsoBmffSampleDependsOn.Unknown,
                    PlaybackIsoBmffSampleIsDependedOn.Unknown,
                    PlaybackIsoBmffSampleHasRedundancy.Unknown),
                new PlaybackIsoBmffSampleDependencyTypeEntry(
                    PlaybackIsoBmffSampleDependsOn.DependsOnOthers,
                    PlaybackIsoBmffSampleIsDependedOn.DependedOn,
                    PlaybackIsoBmffSampleHasRedundancy.NoRedundancy),
                new PlaybackIsoBmffSampleDependencyTypeEntry(
                    PlaybackIsoBmffSampleDependsOn.Independent,
                    PlaybackIsoBmffSampleIsDependedOn.NotDependedOn,
                    PlaybackIsoBmffSampleHasRedundancy.HasRedundancy),
            ],
            parsed.Entries);
        Assert.IsNotType<PlaybackIsoBmffSampleDependencyTypeEntry[]>(
            parsed.Entries);
    }

    [Fact]
    public void Parse_EmptyTableIsValid()
    {
        var result = PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
            CreateBox(),
            out var dependencyType);

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.Parsed,
            result);
        Assert.Empty(
            Assert.IsType<PlaybackIsoBmffSampleDependencyTypeBox>(
                dependencyType).Entries);
    }

    [Fact]
    public void Parse_ExtendedAndExtendsToEndHeadersAreSupported()
    {
        var extended = CreateBoxWithHeader([0x10], extended: true);
        var toEnd = CreateBoxWithHeader([0x20], extendsToEnd: true);

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
                extended,
                out var extendedTable));
        Assert.Equal(
            PlaybackIsoBmffSampleDependsOn.DependsOnOthers,
            Assert.IsType<PlaybackIsoBmffSampleDependencyTypeBox>(
                extendedTable).Entries[0].DependsOn);
        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.Parsed,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
                toEnd,
                out var toEndTable));
        Assert.Equal(
            PlaybackIsoBmffSampleDependsOn.Independent,
            Assert.IsType<PlaybackIsoBmffSampleDependencyTypeBox>(
                toEndTable).Entries[0].DependsOn);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65537)]
    public void Parse_InvalidEntryLimitRejectsBeforeInputAccess(int maximum)
    {
        var result = PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
            [],
            out var dependencyType,
            new() { MaximumEntryCount = maximum });

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                .InvalidConfiguration,
            result);
        Assert.Null(dependencyType);
    }

    [Fact]
    public void Parse_EntryLimitRejectsBeforeProjection()
    {
        var result = PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
            CreateBox(0, 0),
            out var dependencyType,
            new() { MaximumEntryCount = 1 });

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.EntryLimitExceeded,
            result);
        Assert.Null(dependencyType);
    }

    [Theory]
    [InlineData(0x40)]
    [InlineData(0x80)]
    [InlineData(0xc0)]
    public void Parse_NonzeroReservedBitsAreRejected(byte value)
    {
        var result = PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
            CreateBox(value),
            out var dependencyType);

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.NonzeroReservedBits,
            result);
        Assert.Null(dependencyType);
    }

    [Theory]
    [InlineData(0x30)]
    [InlineData(0x0c)]
    [InlineData(0x03)]
    public void Parse_ReservedDependencyValuesAreRejected(byte value)
    {
        var result = PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
            CreateBox(value),
            out var dependencyType);

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult
                .ReservedDependencyValue,
            result);
        Assert.Null(dependencyType);
    }

    [Fact]
    public void Parse_UnsupportedVersionAndNonzeroFlagsAreDistinct()
    {
        var version = CreateBox();
        version[8] = 1;
        var flags = CreateBox();
        flags[11] = 1;

        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.UnsupportedVersion,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
                version,
                out var versionTable));
        Assert.Null(versionTable);
        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.NonzeroFlags,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
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
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.WrongBoxType,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
                wrongType,
                out var wrongTable));
        Assert.Null(wrongTable);
        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.NeedMoreData,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
                shortInput,
                out var shortTable));
        Assert.Null(shortTable);
        Assert.Equal(
            PlaybackIsoBmffSampleDependencyTypeBoxParseResult.BoxLengthMismatch,
            PlaybackIsoBmffSampleDependencyTypeBoxParser.Parse(
                trailingInput,
                out var trailingTable));
        Assert.Null(trailingTable);
    }

    private static byte[] CreateBox(params byte[] entries) =>
        CreateBoxWithHeader(entries);

    private static byte[] CreateBoxWithHeader(
        byte[] entries,
        bool extended = false,
        bool extendsToEnd = false)
    {
        var headerSize = extended ? 16 : 8;
        var box = new byte[headerSize + 4 + entries.Length];
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(0, 4),
            extendsToEnd ? 0 : extended ? 1 : (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), Sdtp);
        if (extended)
        {
            BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8, 8), (ulong)box.Length);
        }

        entries.CopyTo(box, headerSize + 4);
        return box;
    }
}
