using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class DefaultRecordingFilePathProviderTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    private static readonly RecordingSessionId TestSessionId = new(
        Guid.ParseExact(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "N"));

    [Fact]
    public void GetPath_UsesTimestampChannelAndStableIdentifiers()
    {
        var provider = CreateProvider(
            new DateTimeOffset(
                2026,
                9,
                23,
                1,
                2,
                3,
                TimeSpan.Zero));

        var path = provider.GetPath(
            CreateRequest(
                name: "ゾエアch",
                streamExtension: "flv"),
            TestSessionId);

        Assert.Equal(
            Path.Combine(
                provider.BaseDirectory,
                "20260923_010203_ゾエアch_531DC8DFC7FB42928AC2C0A626517A87_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.flv"),
            path);
    }

    [Theory]
    [InlineData("bad/name", "bad_name")]
    [InlineData("bad\\name", "bad_name")]
    [InlineData("bad:name", "bad_name")]
    [InlineData("bad***name", "bad_name")]
    [InlineData("  channel.  ", "channel")]
    public void GetPath_SanitizesUnsafeChannelNames(
        string name,
        string expectedFragment)
    {
        var provider = CreateProvider(DateTimeOffset.UnixEpoch);

        var fileName = Path.GetFileName(
            provider.GetPath(
                CreateRequest(name, "flv"),
                TestSessionId));

        Assert.Contains(
            $"_{expectedFragment}_",
            fileName,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void GetPath_PrefixesReservedWindowsNames(
        string name)
    {
        var provider = CreateProvider(DateTimeOffset.UnixEpoch);

        var fileName = Path.GetFileName(
            provider.GetPath(
                CreateRequest(name, "flv"),
                TestSessionId));

        Assert.Contains(
            $"_{name}_".ToUpperInvariant(),
            fileName.ToUpperInvariant(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void GetPath_EmptyChannelName_UsesFallback()
    {
        var provider = CreateProvider(DateTimeOffset.UnixEpoch);

        var fileName = Path.GetFileName(
            provider.GetPath(
                CreateRequest(
                    name: null,
                    streamExtension: "flv"),
                TestSessionId));

        Assert.Contains(
            "_channel_",
            fileName,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GetPath_LongChannelName_IsBounded()
    {
        var provider = CreateProvider(DateTimeOffset.UnixEpoch);
        var longName = new string(
            'x',
            DefaultRecordingFilePathProvider.MaximumNameComponentLength + 50);

        var fileName = Path.GetFileName(
            provider.GetPath(
                CreateRequest(
                    longName,
                    "flv"),
                TestSessionId));

        var expected = new string(
            'x',
            DefaultRecordingFilePathProvider.MaximumNameComponentLength);

        Assert.Contains(
            $"_{expected}_",
            fileName,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FLV", ".flv")]
    [InlineData(".Mp4", ".mp4")]
    [InlineData("", ".bin")]
    [InlineData(null, ".bin")]
    [InlineData("../flv", ".bin")]
    [InlineData("verylongextension", ".bin")]
    public void GetPath_NormalizesOrRejectsExtension(
        string? extension,
        string expectedExtension)
    {
        var provider = CreateProvider(DateTimeOffset.UnixEpoch);

        var path = provider.GetPath(
            CreateRequest(
                "Channel",
                extension),
            TestSessionId);

        Assert.Equal(
            expectedExtension,
            Path.GetExtension(path));
    }

    [Fact]
    public void GetPath_SameSecondDifferentSessions_DoNotCollide()
    {
        var provider = CreateProvider(DateTimeOffset.UnixEpoch);

        var first = provider.GetPath(
            CreateRequest(
                "Channel",
                "flv"),
            TestSessionId);

        var second = provider.GetPath(
            CreateRequest(
                "Channel",
                "flv"),
            new RecordingSessionId(
                Guid.ParseExact(
                    "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                    "N")));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Constructor_StoresAbsoluteBaseDirectory()
    {
        var relative = Path.Combine(
            ".",
            "recordings");

        var provider = new DefaultRecordingFilePathProvider(
            relative,
            static () => DateTimeOffset.UnixEpoch);

        Assert.True(
            Path.IsPathRooted(provider.BaseDirectory));
    }

    [Fact]
    public void DynamicBaseDirectoryProvider_UsesLatestDirectoryForSubsequentPaths()
    {
        var currentDirectory = Path.Combine(
            Path.GetTempPath(),
            "PeerCast-Nova-DynamicPathTests",
            Guid.NewGuid().ToString("N"),
            "initial");
        var provider = new DefaultRecordingFilePathProvider(
            () => currentDirectory,
            static () => DateTimeOffset.UnixEpoch);

        var firstPath = provider.GetPath(
            CreateRequest("ChannelA", "flv"),
            TestSessionId);
        Assert.StartsWith(Path.GetFullPath(currentDirectory), firstPath, StringComparison.Ordinal);

        var nextDirectory = Path.Combine(
            Path.GetDirectoryName(currentDirectory)!,
            "updated");
        currentDirectory = nextDirectory;

        Assert.Equal(Path.GetFullPath(nextDirectory), provider.BaseDirectory);
        var secondPath = provider.GetPath(
            CreateRequest("ChannelA", "flv"),
            TestSessionId);
        Assert.StartsWith(Path.GetFullPath(nextDirectory), secondPath, StringComparison.Ordinal);
        Assert.NotEqual(firstPath, secondPath);
    }

    private static DefaultRecordingFilePathProvider CreateProvider(
        DateTimeOffset now)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PeerCast-Nova-RecordingPathTests",
            Guid.NewGuid().ToString("N"));

        return new DefaultRecordingFilePathProvider(
            directory,
            () => now);
    }

    private static RecordingSinkStartRequest CreateRequest(
        string? name,
        string? streamExtension)
    {
        return new RecordingSinkStartRequest(
            new RecordingCandidate(
                TestChannelId,
                new ChannelMetadataSnapshot(
                    name: name,
                    streamExtension: streamExtension)),
            Guid.ParseExact(
                "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                "N"));
    }
}
