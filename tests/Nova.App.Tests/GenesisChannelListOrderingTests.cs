namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelListOrderingTests
{
    [Fact]
    public void ChannelNameSortOrdersAscendingAndDescending()
    {
        var channels = new[]
        {
            Channel("Bravo", listeners: 2),
            Channel("alpha", listeners: 3),
            Channel("Charlie", listeners: 1),
        };

        var ascending = GenesisChannelListOrdering.Apply(
            channels,
            new(GenesisChannelColumn.ChannelName, GenesisChannelSortDirection.Ascending));
        var descending = GenesisChannelListOrdering.Apply(
            channels,
            new(GenesisChannelColumn.ChannelName, GenesisChannelSortDirection.Descending));

        Assert.Equal(["alpha", "Bravo", "Charlie"], ascending.Select(x => x.Name));
        Assert.Equal(["Charlie", "Bravo", "alpha"], descending.Select(x => x.Name));
    }

    [Fact]
    public void ListenerSortUsesRelayAsTieBreaker()
    {
        var channels = new[]
        {
            Channel("A", listeners: 10, relays: 4),
            Channel("B", listeners: 10, relays: 2),
            Channel("C", listeners: 5, relays: 9),
        };

        var ordered = GenesisChannelListOrdering.Apply(
            channels,
            new(GenesisChannelColumn.ListenerRelay, GenesisChannelSortDirection.Ascending));

        Assert.Equal(["C", "B", "A"], ordered.Select(x => x.Name));
    }

    [Fact]
    public void MissingNumericValuesRemainAtEndInEitherDirection()
    {
        var channels = new[]
        {
            Channel("Unknown", bitrate: null),
            Channel("Low", bitrate: 800),
            Channel("High", bitrate: 2400),
        };

        var ascending = GenesisChannelListOrdering.Apply(
            channels,
            new(GenesisChannelColumn.Bitrate, GenesisChannelSortDirection.Ascending));
        var descending = GenesisChannelListOrdering.Apply(
            channels,
            new(GenesisChannelColumn.Bitrate, GenesisChannelSortDirection.Descending));

        Assert.Equal(["Low", "High", "Unknown"], ascending.Select(x => x.Name));
        Assert.Equal(["High", "Low", "Unknown"], descending.Select(x => x.Name));
    }

    [Fact]
    public void NoSortPreservesCatalogOrder()
    {
        var channels = new[]
        {
            Channel("C"),
            Channel("A"),
            Channel("B"),
        };

        var ordered = GenesisChannelListOrdering.Apply(channels, sort: null);

        Assert.Equal(["C", "A", "B"], ordered.Select(x => x.Name));
    }

    private static GenesisChannelItem Channel(
        string name,
        int? listeners = null,
        int? relays = null,
        int? bitrate = null) =>
        new(
            name,
            $"pcp://example/{name}",
            $"[game - {name}]",
            listeners,
            relays,
            bitrate,
            TimeSpan.FromMinutes(name.Length),
            "FLV");
}
