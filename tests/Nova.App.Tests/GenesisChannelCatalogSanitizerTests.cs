namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogSanitizerTests
{
    [Fact]
    public void SanitizerTrimsFieldsAndNormalizesNegativeMetrics()
    {
        var snapshot = new GenesisChannelCatalogSnapshot([
            new GenesisChannelItem(
                "  Example Channel  ",
                "  pcp://example/channel  ",
                "  [game - example]  ",
                -1,
                -2,
                -3,
                TimeSpan.FromMinutes(-1),
                "  FLV  "),
        ]);

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        var channel = Assert.Single(sanitized.Channels);
        Assert.Equal("Example Channel", channel.Name);
        Assert.Equal("pcp://example/channel", channel.Source);
        Assert.Equal("[game - example]", channel.Description);
        Assert.Null(channel.Listeners);
        Assert.Null(channel.Relays);
        Assert.Null(channel.BitrateKbps);
        Assert.Null(channel.Uptime);
        Assert.Equal("FLV", channel.Type);
    }

    [Fact]
    public void SanitizerSkipsInvalidRowsAndDeduplicatesTrimmedSources()
    {
        var valid = new GenesisChannelItem("Valid", "pcp://example/channel");
        var duplicate = new GenesisChannelItem(
            "Duplicate",
            "  pcp://example/channel  ");
        var snapshot = new GenesisChannelCatalogSnapshot(new GenesisChannelItem[]
        {
            null!,
            new("   ", "pcp://example/blank-name"),
            new("Blank source", "   "),
            valid,
            duplicate,
            new("Second", "pcp://example/second"),
        });

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        Assert.Equal(2, sanitized.Channels.Count);
        Assert.Equal("Valid", sanitized.Channels[0].Name);
        Assert.Equal("Second", sanitized.Channels[1].Name);
    }

    [Fact]
    public void SanitizerSkipsOversizedSourcesInsteadOfRewritingThem()
    {
        var maximumSource = new string(
            'S',
            GenesisChannelCatalogSanitizer.MaximumSourceCharacters);
        var snapshot = new GenesisChannelCatalogSnapshot([
            new GenesisChannelItem("Oversized", maximumSource + "tail"),
            new GenesisChannelItem("Valid", maximumSource),
        ]);

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        var channel = Assert.Single(sanitized.Channels);
        Assert.Equal("Valid", channel.Name);
        Assert.Equal(maximumSource, channel.Source);
    }

    [Fact]
    public void SanitizerBoundsExternalTextBeforeProjection()
    {
        var snapshot = new GenesisChannelCatalogSnapshot([
            new GenesisChannelItem(
                new string('N', GenesisChannelCatalogSanitizer.MaximumNameCharacters + 10),
                "pcp://example/channel",
                new string('D', GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters + 10),
                Type: new string('T', GenesisChannelCatalogSanitizer.MaximumTypeCharacters + 10)),
        ]);

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        var channel = Assert.Single(sanitized.Channels);
        Assert.Equal(GenesisChannelCatalogSanitizer.MaximumNameCharacters, channel.Name.Length);
        Assert.Equal("pcp://example/channel", channel.Source);
        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters,
            channel.Description?.Length);
        Assert.Equal(GenesisChannelCatalogSanitizer.MaximumTypeCharacters, channel.Type?.Length);
    }

    [Fact]
    public void SanitizerDoesNotSplitSurrogatePairsAtTextBounds()
    {
        static string SplitAt(int maximumCharacters, char fill) =>
            new string(fill, maximumCharacters - 1) + "😀tail";

        var snapshot = new GenesisChannelCatalogSnapshot([
            new GenesisChannelItem(
                SplitAt(GenesisChannelCatalogSanitizer.MaximumNameCharacters, 'N'),
                "pcp://example/channel",
                SplitAt(GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters, 'D'),
                Type: SplitAt(GenesisChannelCatalogSanitizer.MaximumTypeCharacters, 'T')),
        ]);

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(snapshot);

        var channel = Assert.Single(sanitized.Channels);
        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumNameCharacters - 1,
            channel.Name.Length);
        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumDescriptionCharacters - 1,
            channel.Description?.Length);
        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumTypeCharacters - 1,
            channel.Type?.Length);
        Assert.False(char.IsSurrogate(channel.Name[^1]));
        Assert.False(char.IsSurrogate(channel.Description![^1]));
        Assert.False(char.IsSurrogate(channel.Type![^1]));
    }

    [Fact]
    public void SanitizerCapsProjectedChannelCount()
    {
        var channels = Enumerable.Range(0, GenesisChannelCatalogSanitizer.MaximumChannels + 5)
            .Select(index => new GenesisChannelItem(
                $"Channel {index}",
                $"pcp://example/{index}"))
            .ToArray();

        var sanitized = GenesisChannelCatalogSanitizer.Sanitize(new(channels));

        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumChannels,
            sanitized.Channels.Count);
    }
}
