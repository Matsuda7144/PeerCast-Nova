namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelIdentityTests
{
    [Fact]
    public void SanitizerNormalizesValidChannelId()
    {
        var snapshot = new GenesisChannelCatalogSnapshot([
            new GenesisChannelItem(
                "Example",
                "pcp://example/channel",
                ChannelId: " 0123456789abcdef0123456789abcdef "),
        ]);

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        var channel = Assert.Single(sanitized.Channels);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", channel.ChannelId);
    }

    [Theory]
    [InlineData("not-a-channel-id")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("")]
    public void SanitizerDropsInvalidChannelIdWithoutDroppingChannel(string channelId)
    {
        var snapshot = new GenesisChannelCatalogSnapshot([
            new GenesisChannelItem(
                "Example",
                "pcp://example/channel",
                ChannelId: channelId),
        ]);

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        var channel = Assert.Single(sanitized.Channels);
        Assert.Null(channel.ChannelId);
        Assert.Equal("Example", channel.Name);
        Assert.Equal("pcp://example/channel", channel.Source);
    }

    [Fact]
    public void StableIdentityPrefersChannelIdOverTrackerSpecificSource()
    {
        var left = new GenesisChannelItem(
            "Example",
            "pcp://tracker-a.example/channel/id",
            ChannelId: "0123456789abcdef0123456789abcdef");
        var right = new GenesisChannelItem(
            "Example",
            "pcp://tracker-b.example/channel/id",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");

        Assert.True(GenesisChannelIdentity.Matches(left, right));
        Assert.Equal(
            GenesisChannelIdentity.GetStableKey(left),
            GenesisChannelIdentity.GetStableKey(right));
        Assert.Equal(
            "id:0123456789ABCDEF0123456789ABCDEF",
            GenesisChannelIdentity.GetStableKey(left));
    }

    [Fact]
    public void StableIdentityFallsBackToSourceWhenChannelIdIsUnavailable()
    {
        var left = new GenesisChannelItem(
            "Example",
            "  pcp://tracker.example/channel/id  ");
        var right = new GenesisChannelItem(
            "Renamed",
            "pcp://tracker.example/channel/id",
            ChannelId: "not-a-valid-id");

        Assert.True(GenesisChannelIdentity.Matches(left, right));
        Assert.Equal(
            "source:pcp://tracker.example/channel/id",
            GenesisChannelIdentity.GetStableKey(left));
    }

    [Fact]
    public void DifferentValidChannelIdsDoNotAliasEvenWhenSourceMatches()
    {
        var left = new GenesisChannelItem(
            "Left",
            "pcp://tracker.example/channel/id",
            ChannelId: "0123456789ABCDEF0123456789ABCDEF");
        var right = new GenesisChannelItem(
            "Right",
            "pcp://tracker.example/channel/id",
            ChannelId: "ABCDEF0123456789ABCDEF0123456789");

        Assert.False(GenesisChannelIdentity.Matches(left, right));
    }
}
