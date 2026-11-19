using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlayerInformationTests
{
    [Fact]
    public void LatestPcpMetadataWinsAndStaleCatalogCountsBecomeUnknown()
    {
        var catalog = new GenesisChannelItem("old", "pcp://tracker.test:7144/channel/531DC8DFC7FB42928AC2C0A626517A87",
            "catalog details", 10, 2, 1000, Type: "FLV")
        { ContactUrl = "https://example.test/old" };
        var tracker = new ChannelStatusTracker(new(Guid.NewGuid()), new(Guid.NewGuid()));
        tracker.TransitionTo(ChannelSessionState.Connecting);
        tracker.TransitionTo(ChannelSessionState.Negotiating);
        tracker.UpdateMetadata(new("new", "game", "new details", "https://example.test/new", "FLV", 2000,
            comment: "new comment", track: new("song", "music", "album", "artist", "https://example.test/track")));
        var info = GenesisPlayerChannelInfo.Create(catalog, tracker.Snapshot, null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3));
        Assert.Equal("new", info.Metadata.Name);
        Assert.Equal("https://example.test/new", info.Metadata.ContactUrl);
        Assert.Equal("new comment", info.Metadata.Comment);
        Assert.Equal("song", info.Metadata.Track!.Title);
        Assert.Null(info.Listeners);
        Assert.Null(info.Relays);
        info = GenesisPlayerChannelInfo.Create(catalog, tracker.Snapshot, new(3, 7, 4, 1), TimeSpan.FromSeconds(5), TimeSpan.Zero);
        Assert.Equal(7, info.Listeners);
        Assert.Equal(4, info.Relays);
        Assert.Equal(1, info.LocalRelays);
    }

    [Fact]
    public void PlayerArgumentsPreserveQuotedNamesAndOnlyAcceptSafeContactUrls()
    {
        const string url = "http://127.0.0.1:57133/pls/531DC8DFC7FB42928AC2C0A626517A87.pls";
        var pcrPlayerPath = Path.Combine("Player Tools", "pcrplayer64.EXE");
        var start = PcrPlayerExternalPlayerLauncher.CreateStartInfo(pcrPlayerPath, url, "船長 \"test\"", "https://example.test/?a=1&b=2");
        Assert.False(start.UseShellExecute);
        Assert.Equal(new[] { url, "船長 \"test\"", "https://example.test/?a=1&b=2" }, start.ArgumentList);
        start = PcrPlayerExternalPlayerLauncher.CreateStartInfo("PCRPlayer64.exe", url, "test\0", "file:///C:/private");
        Assert.Equal(new[] { url, "test", "" }, start.ArgumentList);
        Assert.Throws<ArgumentException>(() => PcrPlayerExternalPlayerLauncher.CreateStartInfo("PCRPlayer64.exe", "http://example.test/pls/531DC8DFC7FB42928AC2C0A626517A87.pls"));
        Assert.Throws<ArgumentException>(() => PcrPlayerExternalPlayerLauncher.CreateStartInfo("PCRPlayer64.exe", url + "?cmd=stop"));
    }

    [Theory]
    [InlineData("PCRBrowser.exe", "/stream/531DC8DFC7FB42928AC2C0A626517A87")]
    [InlineData("OtherPlayer.exe", "/pls/531DC8DFC7FB42928AC2C0A626517A87.pls")]
    public void OtherPlayerArgumentsContainOnlyTheValidatedPlaybackUrl(string executable, string playbackPath)
    {
        var url = "http://127.0.0.1:57133" + playbackPath;
        var start = PcrPlayerExternalPlayerLauncher.CreateStartInfo(executable, url, "channel name", "https://example.test/contact");

        Assert.Equal(executable, start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal([url], start.ArgumentList);
        Assert.Throws<ArgumentException>(() =>
            PcrPlayerExternalPlayerLauncher.CreateStartInfo(executable, "http://example.test" + playbackPath));
    }
}
