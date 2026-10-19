namespace PeerCast.Nova.App.Tests;

public sealed class GenesisAggregatedYellowPageDisplayBoundsTests
{
    [Fact]
    public async Task AggregatedDisplayIsBoundedWithoutDroppingOriginIds()
    {
        const string channelId = "0123456789ABCDEF0123456789ABCDEF";
        var channel = new GenesisChannelItem(
            "Shared",
            "pcp://example/channel",
            ChannelId: channelId);
        var definitions = Enumerable.Range(0, 40)
            .Select(index => new GenesisYellowPageDefinition(
                $"yp-{index:D2}",
                $"{index:D2}{new string('Y', 126)}",
                new Uri($"https://yp-{index:D2}.example/index.txt")))
            .ToArray();
        var catalogs = definitions.ToDictionary(
            static definition => definition.Id,
            _ => (IGenesisChannelCatalog)new FixedCatalog(new([channel])),
            StringComparer.Ordinal);
        var catalog = new GenesisAggregatingYellowPageChannelCatalog(
            new GenesisYellowPageConfiguration(definitions),
            new FixedFactory(catalogs));

        var detailed = await catalog.ReadDetailedAsync();

        var entry = Assert.Single(detailed.Entries);
        Assert.Equal(
            GenesisChannelCatalogSanitizer.MaximumYellowPageCharacters,
            entry.Channel.YellowPageText.Length);
        Assert.Equal(definitions.Length, entry.YellowPageIds.Count);
    }

    private sealed class FixedFactory : IGenesisYellowPageChannelCatalogFactory
    {
        private readonly IReadOnlyDictionary<string, IGenesisChannelCatalog> _catalogs;

        public FixedFactory(
            IReadOnlyDictionary<string, IGenesisChannelCatalog> catalogs)
        {
            _catalogs = catalogs;
        }

        public IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition) =>
            _catalogs[definition.Id];
    }

    private sealed class FixedCatalog : IGenesisChannelCatalog
    {
        private readonly GenesisChannelCatalogSnapshot _snapshot;

        public FixedCatalog(GenesisChannelCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_snapshot);
        }
    }
}
