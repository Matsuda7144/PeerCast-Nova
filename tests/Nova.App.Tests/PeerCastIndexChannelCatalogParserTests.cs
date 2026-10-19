namespace PeerCast.Nova.App.Tests;

public sealed class PeerCastIndexChannelCatalogParserTests
{
    private static readonly Uri SpTrackerUri = new("pcp://bayonet.ddo.jp:7146/");

    [Fact]
    public void ParsesStandardIndexRowIntoPeCaRecorderPresentation()
    {
        var row = BuildRow(
            name: "Example &amp; Channel",
            channelId: "0123456789abcdef0123456789abcdef",
            tracker: "203.0.113.10:7144",
            genre: "game",
            detail: "Test &lt;Free&gt;",
            listeners: "12",
            relays: "3",
            bitrate: "1800",
            type: "FLV",
            artist: "Artist",
            album: "Album",
            title: "Title",
            broadcastTime: "02:15",
            comment: "Comment &amp; more");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(row, SpTrackerUri);

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("Example & Channel", channel.Name);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", channel.ChannelId);
        Assert.Equal(
            "pcp://203.0.113.10:7144/channel/0123456789ABCDEF0123456789ABCDEF",
            channel.Source);
        Assert.Equal(
            "[game - Test <Free>] Comment & more Artist/Title/Album",
            channel.Description);
        Assert.Equal(12, channel.Listeners);
        Assert.Equal(3, channel.Relays);
        Assert.Equal(1800, channel.BitrateKbps);
        Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15), channel.Uptime);
        Assert.Equal("FLV", channel.Type);
    }

    [Fact]
    public void RawTypeIsPreservedForPresentation()
    {
        var row = BuildRow(
            name: "RAW Channel",
            channelId: "0123456789ABCDEF0123456789ABCDEF",
            tracker: "203.0.113.10:7144",
            type: "RAW");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(row, SpTrackerUri);

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("RAW", channel.Type);
        Assert.Equal("RAW", channel.TypeText);
    }

    [Fact]
    public void MissingTipFallsBackToConfiguredYellowPageTracker()
    {
        var row = BuildRow(
            name: "Fallback",
            channelId: "ABCDEF0123456789ABCDEF0123456789",
            tracker: string.Empty);

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(row, SpTrackerUri);

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("ABCDEF0123456789ABCDEF0123456789", channel.ChannelId);
        Assert.Equal(
            "pcp://bayonet.ddo.jp:7146/channel/ABCDEF0123456789ABCDEF0123456789",
            channel.Source);
    }

    [Fact]
    public void TrackerPathFallsBackToConfiguredYellowPageTracker()
    {
        var row = BuildRow(
            name: "Fallback",
            channelId: "ABCDEF0123456789ABCDEF0123456789",
            tracker: "203.0.113.10:7144/alternate");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(row, SpTrackerUri);

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal(
            "pcp://bayonet.ddo.jp:7146/channel/ABCDEF0123456789ABCDEF0123456789",
            channel.Source);
    }

    [Fact]
    public void InvalidOrRestrictedChannelIdsAreSkipped()
    {
        var invalid = BuildRow(
            name: "Invalid",
            channelId: "not-a-channel-id",
            tracker: "203.0.113.10:7144");
        var restricted = BuildRow(
            name: "Restricted",
            channelId: new string('0', 32),
            tracker: "127.0.0.1:7144");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(
            $"{invalid}\n{restricted}\n",
            SpTrackerUri);

        Assert.Empty(snapshot.Channels);
    }

    [Fact]
    public void InvalidCountersAndBroadcastTimeBecomeUnknown()
    {
        var row = BuildRow(
            name: "Unknown Values",
            channelId: "0123456789ABCDEF0123456789ABCDEF",
            tracker: "203.0.113.10:7144",
            listeners: "-1",
            relays: "not-a-number",
            bitrate: "-20",
            broadcastTime: "bad:time");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(row, SpTrackerUri);

        var channel = Assert.Single(snapshot.Channels);
        Assert.Null(channel.Listeners);
        Assert.Null(channel.Relays);
        Assert.Null(channel.BitrateKbps);
        Assert.Null(channel.Uptime);
    }

    [Fact]
    public void MalformedRowsDoNotRejectOtherChannels()
    {
        var valid = BuildRow(
            name: "Valid",
            channelId: "0123456789ABCDEF0123456789ABCDEF",
            tracker: "203.0.113.10:7144");

        var snapshot = PeerCastIndexChannelCatalogParser.Parse(
            $"malformed<>row\r\n{valid}\r\n",
            SpTrackerUri);

        var channel = Assert.Single(snapshot.Channels);
        Assert.Equal("Valid", channel.Name);
    }

    [Fact]
    public void InvalidFallbackTrackerIsRejectedBeforeParsing()
    {
        var invalidTracker = new Uri("http://bayonet.ddo.jp/sp/");

        Assert.Throws<ArgumentException>(() =>
            PeerCastIndexChannelCatalogParser.Parse(string.Empty, invalidTracker));
    }

    private static string BuildRow(
        string name,
        string channelId,
        string tracker,
        string genre = "",
        string detail = "",
        string listeners = "",
        string relays = "",
        string bitrate = "",
        string type = "",
        string artist = "",
        string album = "",
        string title = "",
        string broadcastTime = "",
        string comment = "")
    {
        var fields = new[]
        {
            name,
            channelId,
            tracker,
            "http://example.invalid/contact",
            genre,
            detail,
            listeners,
            relays,
            bitrate,
            type,
            artist,
            album,
            title,
            "",
            "",
            broadcastTime,
            "",
            comment,
        };
        return string.Join("<>", fields);
    }
}
