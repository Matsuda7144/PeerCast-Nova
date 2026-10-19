namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelSearchFilterTests
{
    [Fact]
    public void EmptyQueryPreservesOrderAndChannelIdentity()
    {
        var first = Channel("First", "pcp://example/first");
        var second = Channel("Second", "pcp://example/second");

        var filtered = GenesisChannelSearchFilter.Apply([first, second], "  \t\r\n　 ");

        Assert.Equal(2, filtered.Count);
        Assert.Same(first, filtered[0]);
        Assert.Same(second, filtered[1]);
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("NOVA")]
    [InlineData("夜配信")]
    [InlineData("flv")]
    [InlineData("sp")]
    [InlineData("pcp://example/channel")]
    [InlineData("0123456789abcdef")]
    public void QueryMatchesSearchableChannelMetadata(string query)
    {
        var channel = new GenesisChannelItem(
            "Alpha Nova",
            "pcp://example/channel",
            Description: "Jazz 夜配信",
            Type: "FLV",
            ChannelId: "0123456789ABCDEF")
        {
            YellowPage = "SP",
        };

        var filtered = GenesisChannelSearchFilter.Apply([channel], query);

        Assert.Same(channel, Assert.Single(filtered));
    }

    [Fact]
    public void OfflineFavoriteQueryMatchesLastKnownYellowPage()
    {
        var channel = new GenesisChannelItem(
            "Saved favorite",
            "pcp://example/favorite",
            Type: "RAW",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF")
        {
            YellowPage = "SP",
        };
        var favorite = new GenesisFavoriteChannelProjectionEntry(
            GenesisFavoriteChannel.FromChannel(channel),
            CurrentChannel: null);

        var filtered = GenesisChannelSearchFilter.ApplyFavorites([favorite], "sp");

        Assert.Same(favorite, Assert.Single(filtered));
    }

    [Fact]
    public void MultipleTermsMayMatchAcrossDifferentFields()
    {
        var matching = new GenesisChannelItem(
            "Alpha",
            "pcp://example/alpha",
            Description: "Evening music",
            Type: "FLV");
        var nameOnly = new GenesisChannelItem(
            "Alpha",
            "pcp://example/other",
            Description: "Talk",
            Type: "WMV");

        var filtered = GenesisChannelSearchFilter.Apply(
            [matching, nameOnly],
            "alpha music flv");

        Assert.Same(matching, Assert.Single(filtered));
    }

    [Fact]
    public void IdeographicSpaceSeparatesTermsAcrossDifferentFields()
    {
        var matching = new GenesisChannelItem(
            "八拳ch",
            "pcp://example/hakken",
            Description: "ファイナルファンタジー14");
        var nameOnly = new GenesisChannelItem(
            "八拳別枠",
            "pcp://example/other",
            Description: "雑談");

        var filtered = GenesisChannelSearchFilter.Apply(
            [matching, nameOnly],
            "八拳　ファイナル");

        Assert.Same(matching, Assert.Single(filtered));
    }

    [Fact]
    public void MatchingRowsRetainCatalogOrder()
    {
        var first = Channel("Live First", "pcp://example/first");
        var skipped = Channel("Recorded", "pcp://example/recorded");
        var second = Channel("Live Second", "pcp://example/second");

        var filtered = GenesisChannelSearchFilter.Apply(
            [first, skipped, second],
            "live");

        Assert.Equal(2, filtered.Count);
        Assert.Same(first, filtered[0]);
        Assert.Same(second, filtered[1]);
    }

    [Fact]
    public void NonMatchingQueryReturnsEmptyProjection()
    {
        var channel = Channel("Channel", "pcp://example/channel");

        var filtered = GenesisChannelSearchFilter.Apply([channel], "missing");

        Assert.Empty(filtered);
    }

    private static GenesisChannelItem Channel(string name, string source) =>
        new(name, source);
}
